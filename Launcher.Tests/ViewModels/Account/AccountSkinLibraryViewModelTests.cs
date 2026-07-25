/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using Launcher.App.Services;
using Launcher.App.ViewModels.Account;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Account;

public sealed class AccountSkinLibraryViewModelTests
{
    [Fact]
    public async Task OfflineAccountCanApplyLibrarySkinWithoutMicrosoftUpload()
    {
        var skin = new LauncherSkinRecord
        {
            Id = "local-skin",
            Source = @"C:\skins\local-skin.png",
            SkinModel = MinecraftSkinModel.Slim,
            ContentHash = "skin-hash"
        };
        var account = new LauncherAccount
        {
            Id = "offline-account",
            DisplayName = "Offline",
            Uuid = "00000000-0000-0000-0000-000000000001",
            Kind = LauncherAccountKind.Offline
        };
        var accountStore = new RecordingAccountStore(
            new AccountStoreSnapshot([account], account.Id));
        var accountList = new AccountListViewModel(accountStore);
        await accountList.InitializeAsync(new LauncherSettings());

        var microsoftUploadCount = 0;
        var microsoftAccountService = CreateProxy<IMicrosoftAccountService>((method, arguments) =>
        {
            if (method.Name == nameof(IMicrosoftAccountService.UploadSkinAsync))
            {
                microsoftUploadCount++;
                return Task.FromResult((LauncherAccount)arguments![0]!);
            }

            return CreateDefaultReturnValue(method.ReturnType);
        });
        var dialogService = CreateProxy<IAccountDialogService>();
        var retryHandler = new MicrosoftAccountOperationRetryHandler(accountList, dialogService);
        using var operations = new AccountAppearanceOperationCoordinator();
        var profile = new AccountProfileViewModel(
            accountList,
            microsoftAccountService,
            CreateProxy<IThirdPartyAccountService>(),
            operations,
            retryHandler,
            floatingMessageService: null,
            NullLogger.Instance);
        profile.SetAccount(account);
        var skinLibraryService = new InMemorySkinLibraryService([skin]);
        var viewModel = new AccountSkinLibraryViewModel(
            accountList,
            microsoftAccountService,
            skinLibraryService,
            new AccountSkinModelDialogViewModel(),
            dialogService,
            CreateProxy<IFilePickerService>(),
            CreateProxy<IMinecraftSkinFileValidator>(),
            profile,
            retryHandler,
            NullLogger.Instance);
        viewModel.SetAccount(account);

        await viewModel.ApplySkinAsync();

        var updated = Assert.IsType<LauncherAccount>(accountList.SelectedAccount);
        Assert.Equal(skin.Id, updated.ActiveSkinId);
        Assert.Equal(skinLibraryService.AvatarSource, updated.AvatarSource);
        Assert.Equal(0, microsoftUploadCount);
        Assert.Equal(1, accountStore.SaveCount);
    }

    private static T CreateProxy<T>(
        Func<MethodInfo, object?[]?, object?>? handler = null)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object? CreateDefaultReturnValue(Type returnType)
    {
        if (returnType == typeof(void))
            return null;
        if (returnType == typeof(Task))
            return Task.CompletedTask;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [result]);
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    private class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            return Handler?.Invoke(targetMethod, args) ?? CreateDefaultReturnValue(targetMethod.ReturnType);
        }
    }

    private sealed class InMemorySkinLibraryService(
        IReadOnlyList<LauncherSkinRecord> sharedSkins) : IAccountSkinLibraryService
    {
        public string AvatarSource { get; } = "file:///avatars/offline-account.png";

        public IReadOnlyList<LauncherSkinRecord> GetAvailableSkins(LauncherAccount account) =>
            account.IsThirdParty ? account.SkinLibrary : sharedSkins;

        public IReadOnlyList<LauncherSkinRecord> GetSharedSkins() => sharedSkins;

        public Task MigrateLegacySkinsAsync(
            IReadOnlyList<LauncherAccount> accounts,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, LauncherSkinRecord>> SyncMicrosoftAccountSkinsAsync(
            IReadOnlyList<LauncherAccount> accounts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LauncherSkinRecord>>(
                new Dictionary<string, LauncherSkinRecord>());

        public Task<LauncherSkinRecord> ImportSkinAsync(
            LauncherAccount account,
            string skinFilePath,
            MinecraftSkinModel skinModel,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> CreateAvatarSourceAsync(
            LauncherAccount account,
            LauncherSkinRecord skin,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(AvatarSource);

        public Task DeleteSkinAsync(
            LauncherAccount account,
            LauncherSkinRecord skin,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAccountStore(AccountStoreSnapshot snapshot) : IAccountStore
    {
        public int SaveCount { get; private set; }

        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
