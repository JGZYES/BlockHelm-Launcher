/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Accounts;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class AccountSkinCacheServiceTests : TestTempDirectory
{
    [Fact]
    public async Task MicrosoftAndOfflineAccountsShareLibraryWhileThirdPartyRemainsIsolated()
    {
        Directory.CreateDirectory(TempRoot);
        var cacheRoot = Path.Combine(TempRoot, "cache");
        var offlineSkinPath = Path.Combine(TempRoot, "offline.png");
        var thirdPartySkinPath = Path.Combine(TempRoot, "third-party.png");
        await File.WriteAllBytesAsync(offlineSkinPath, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(thirdPartySkinPath, [5, 6, 7, 8]);
        var service = new AccountSkinCacheService(new HttpClient(), cacheRoot);
        var offline = CreateAccount("offline", LauncherAccountKind.Offline);
        var microsoft = CreateAccount("microsoft", LauncherAccountKind.Microsoft);
        var thirdParty = CreateAccount("third-party", LauncherAccountKind.ThirdParty);

        var imported = await service.ImportSkinAsync(
            offline,
            offlineSkinPath,
            MinecraftSkinModel.Slim,
            CancellationToken.None);

        var microsoftLibrary = service.GetAvailableSkins(microsoft);
        var sharedSkin = Assert.Single(microsoftLibrary);
        Assert.Equal(imported.Id, sharedSkin.Id);
        Assert.Equal(MinecraftSkinModel.Slim, sharedSkin.SkinModel);
        Assert.Empty(service.GetAvailableSkins(thirdParty));

        await service.ImportSkinAsync(
            thirdParty,
            thirdPartySkinPath,
            MinecraftSkinModel.Classic,
            CancellationToken.None);

        Assert.Single(service.GetAvailableSkins(offline));
        Assert.Single(service.GetAvailableSkins(thirdParty));

        await service.DeleteSkinAsync(
            microsoft,
            sharedSkin,
            CancellationToken.None);

        Assert.Empty(service.GetAvailableSkins(offline));
        Assert.Single(service.GetAvailableSkins(thirdParty));
    }

    [Fact]
    public async Task ExistingMicrosoftSkinIsCopiedIntoSharedLibrary()
    {
        var cacheRoot = Path.Combine(TempRoot, "cache");
        var legacyDirectory = Path.Combine(cacheRoot, "microsoft-uuid");
        Directory.CreateDirectory(legacyDirectory);
        var legacySkinPath = Path.Combine(legacyDirectory, "v1-legacy.png");
        await File.WriteAllBytesAsync(legacySkinPath, [9, 10, 11, 12]);
        var legacySkin = new LauncherSkinRecord
        {
            Id = "legacy-skin",
            Source = new Uri(legacySkinPath).AbsoluteUri,
            SkinModel = MinecraftSkinModel.Slim
        };
        var microsoft = new LauncherAccount
        {
            Id = "microsoft",
            DisplayName = "Microsoft",
            Uuid = "microsoft-uuid",
            Kind = LauncherAccountKind.Microsoft,
            SkinLibrary = [legacySkin]
        };
        var offline = CreateAccount("offline", LauncherAccountKind.Offline);
        var service = new AccountSkinCacheService(new HttpClient(), cacheRoot);

        service.MigrateAccountSkinsToLibrary(microsoft, CancellationToken.None);
        var migrated = Assert.Single(service.GetAvailableSkins(microsoft));
        var visibleFromOffline = Assert.Single(service.GetAvailableSkins(offline));

        Assert.Equal(MinecraftSkinModel.Slim, migrated.SkinModel);
        Assert.Equal(migrated.Id, visibleFromOffline.Id);
        Assert.Contains("_shared-library", visibleFromOffline.Source, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(legacySkinPath));
    }

    private static LauncherAccount CreateAccount(string id, LauncherAccountKind kind) => new()
    {
        Id = id,
        DisplayName = id,
        Uuid = $"{id}-uuid",
        Kind = kind
    };
}
