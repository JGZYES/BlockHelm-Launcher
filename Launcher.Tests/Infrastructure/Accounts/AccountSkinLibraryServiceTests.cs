/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.Accounts;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class AccountSkinLibraryServiceTests : TestTempDirectory
{
    [Fact]
    public async Task MicrosoftCurrentSkinIsAddedToSharedLibraryByContentAndModel()
    {
        Directory.CreateDirectory(TempRoot);
        var legacySkinPath = Path.Combine(TempRoot, "legacy.png");
        await File.WriteAllBytesAsync(legacySkinPath, [9, 10, 11, 12]);
        using var httpClient = new HttpClient();
        var pathProvider = new LauncherPathProvider(
            applicationBaseDirectory: TempRoot,
            applicationDataDirectory: TempRoot);
        var service = new AccountSkinLibraryService(httpClient, pathProvider);
        var legacySkin = new LauncherSkinRecord
        {
            Id = "legacy-skin",
            Source = new Uri(legacySkinPath).AbsoluteUri,
            SkinModel = MinecraftSkinModel.Slim,
            ContentHash = "legacy-hash"
        };
        var microsoft = new LauncherAccount
        {
            Id = "microsoft",
            DisplayName = "Microsoft",
            Uuid = "microsoft-uuid",
            Kind = LauncherAccountKind.Microsoft,
            SkinLibrary = [legacySkin],
            ActiveSkinId = legacySkin.Id,
            SkinSource = legacySkin.Source,
            SkinModel = legacySkin.SkinModel
        };

        Assert.Empty(service.GetSharedSkins());

        var synchronizedByAccount = await service.SyncMicrosoftAccountSkinsAsync([microsoft]);

        var synchronized = Assert.Single(service.GetSharedSkins());
        Assert.Equal(synchronized.Id, Assert.Single(synchronizedByAccount).Value.Id);
        Assert.Contains("_shared-library", Assert.Single(synchronizedByAccount).Value.Source);

        await service.SyncMicrosoftAccountSkinsAsync([microsoft]);
        Assert.Equal(synchronized.Id, Assert.Single(service.GetSharedSkins()).Id);
    }
}
