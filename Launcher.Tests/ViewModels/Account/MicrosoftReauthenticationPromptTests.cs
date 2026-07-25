/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Account;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Account;

public sealed class MicrosoftReauthenticationPromptTests
{
    [Fact]
    public async Task ExpiredAccountOpensConfirmationBeforeBrowserLoginState()
    {
        var account = new LauncherAccount
        {
            Id = "microsoft-account",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            Kind = LauncherAccountKind.Microsoft
        };
        var accountList = new AccountListViewModel(
            new FakeAccountStore(new AccountStoreSnapshot([account], account.Id)));
        await accountList.InitializeAsync(new LauncherSettings());
        var viewModel = new AccountDialogViewModel(
            accountList,
            Stub<IMicrosoftAccountService>(),
            Stub<IThirdPartyAccountService>(),
            Stub<IOfflineAccountUuidService>(),
            Stub<IStatusService>());

        viewModel.OpenMicrosoftReauthenticationDialog(account);

        Assert.True(viewModel.IsAddAccountDialogOpen);
        Assert.True(viewModel.IsMicrosoftReauthenticationPromptStep);
        Assert.False(viewModel.IsMicrosoftReauthenticationStep);
        Assert.False(viewModel.IsAddAccountDialogBusy);
        Assert.True(viewModel.CanShowAddAccountCancelButton);
        Assert.True(viewModel.CanConfirmAddAccountDialog);
        Assert.Equal(Strings.Dialog_MicrosoftAccountExpiredTitle, viewModel.AddAccountDialogTitle);
        Assert.Equal(Strings.Dialog_MicrosoftAccountExpiredSubtitle, viewModel.AddAccountDialogSubtitle);
        Assert.Contains(account.DisplayName, viewModel.MicrosoftLoginMessage);

        viewModel.BeginMicrosoftAccountReauthentication();

        Assert.False(viewModel.IsMicrosoftReauthenticationPromptStep);
        Assert.True(viewModel.IsMicrosoftReauthenticationStep);
        Assert.True(viewModel.IsAddAccountDialogBusy);
        Assert.False(viewModel.CanShowAddAccountCancelButton);
        Assert.False(viewModel.CanConfirmAddAccountDialog);
    }

    [Fact]
    public async Task CancelIsAvailableWhileExpiredAccountPromptIsVisible()
    {
        var account = new LauncherAccount
        {
            Id = "microsoft-account",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            Kind = LauncherAccountKind.Microsoft
        };
        var accountList = new AccountListViewModel(
            new FakeAccountStore(new AccountStoreSnapshot([account], account.Id)));
        await accountList.InitializeAsync(new LauncherSettings());
        var viewModel = new AccountDialogViewModel(
            accountList,
            Stub<IMicrosoftAccountService>(),
            Stub<IThirdPartyAccountService>(),
            Stub<IOfflineAccountUuidService>(),
            Stub<IStatusService>());
        viewModel.OpenMicrosoftReauthenticationDialog(account);

        viewModel.CancelAddAccountDialog();

        Assert.False(viewModel.IsAddAccountDialogOpen);
    }

    private static T Stub<T>() where T : class =>
        DispatchProxy.Create<T, DefaultInterfaceProxy>();

    public class DefaultInterfaceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType ?? typeof(void);
            if (returnType == typeof(void))
                return null;
            if (returnType == typeof(Task))
                return Task.CompletedTask;
            if (returnType.IsGenericType
                && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var valueType = returnType.GetGenericArguments()[0];
                var value = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(valueType)
                    .Invoke(null, [value]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private sealed class FakeAccountStore(AccountStoreSnapshot snapshot) : IAccountStore
    {
        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
