/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.Accounts;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class MicrosoftAccountFactoryTests : TestTempDirectory
{
    [Fact]
    public async Task CachedProfileCreatesSkinRecordWithReportedVariant()
    {
        var handler = new SkinResponseHandler(CreateSkinPng());
        using var httpClient = new HttpClient(handler);
        var pathProvider = new LauncherPathProvider(
            applicationBaseDirectory: TempRoot,
            applicationDataDirectory: TempRoot);
        var skinLibrary = new AccountSkinLibraryService(httpClient, pathProvider);
        var factory = new MicrosoftAccountFactory(
            new AccountAvatarService(httpClient, pathProvider),
            new AccountSkinCacheService(httpClient, pathProvider),
            skinLibrary);
        var profile = CreateProfile("slim");

        var account = await factory.CreateAccountFromProfileAsync(
            profile,
            forceRefreshAvatar: false,
            CancellationToken.None);

        Assert.Equal(MinecraftSkinModel.Slim, account.SkinModel);
        Assert.Equal(MinecraftSkinModel.Slim, Assert.Single(account.SkinLibrary).SkinModel);
        Assert.Equal(MinecraftSkinModel.Slim, Assert.Single(skinLibrary.GetSharedSkins()).SkinModel);
        Assert.Contains("_shared-library", account.SkinSource, StringComparison.OrdinalIgnoreCase);
        var privateSkinDirectory = Path.Combine(
            pathProvider.DefaultAccountDataDirectory,
            "microsoft",
            "skins",
            MinecraftAccountHelpers.NormalizeUuid(profile.UUID));
        Assert.False(Directory.Exists(privateSkinDirectory));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CachedProfileDoesNotCreateClassicRecordWhenVariantIsMissing()
    {
        var handler = new SkinResponseHandler(CreateSkinPng());
        using var httpClient = new HttpClient(handler);
        var pathProvider = new LauncherPathProvider(
            applicationBaseDirectory: TempRoot,
            applicationDataDirectory: TempRoot);
        var skinLibrary = new AccountSkinLibraryService(httpClient, pathProvider);
        var factory = new MicrosoftAccountFactory(
            new AccountAvatarService(httpClient, pathProvider),
            new AccountSkinCacheService(httpClient, pathProvider),
            skinLibrary);
        var profile = CreateProfile(string.Empty);

        var account = await factory.CreateAccountFromProfileAsync(
            profile,
            forceRefreshAvatar: false,
            CancellationToken.None);

        Assert.Null(account.SkinModel);
        Assert.Null(account.ActiveSkinId);
        Assert.Empty(account.SkinLibrary);
        Assert.Empty(skinLibrary.GetSharedSkins());
        Assert.Equal(0, handler.RequestCount);
    }

    private static JEProfile CreateProfile(string variant) => new()
    {
        UUID = "00112233445566778899aabbccddeeff",
        Username = "Player",
        Skins =
        [
            new JEProfileSkin(
                "active",
                "ACTIVE",
                "https://example.test/skin.png",
                "active",
                variant)
        ]
    };

    private static byte[] CreateSkinPng()
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x44;
            pixels[index + 1] = 0x88;
            pixels[index + 2] = 0xCC;
            pixels[index + 3] = byte.MaxValue;
        }

        var bitmap = BitmapSource.Create(
            size,
            size,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class SkinResponseHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }
}
