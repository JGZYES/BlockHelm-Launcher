/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Infrastructure.Accounts;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class AccountAvatarServiceTests : TestTempDirectory
{
    [Fact]
    public async Task CreatesOfflineAvatarFromLocalSkinUri()
    {
        Directory.CreateDirectory(TempRoot);
        var skinPath = Path.Combine(TempRoot, "skin.png");
        await File.WriteAllBytesAsync(skinPath, CreateSkinPng());
        var avatarDirectory = Path.Combine(TempRoot, "avatars");
        using var httpClient = new HttpClient();
        var service = new AccountAvatarService(httpClient, avatarDirectory);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            "offline-account",
            new Uri(skinPath).AbsoluteUri,
            forceRefresh: true,
            CancellationToken.None,
            useRemoteFallback: false);

        Assert.NotNull(avatarSource);
        var avatarPath = new Uri(avatarSource).LocalPath;
        Assert.True(File.Exists(avatarPath));
        using var stream = File.OpenRead(avatarPath);
        var frame = BitmapFrame.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        Assert.Equal(576, frame.PixelWidth);
        Assert.Equal(576, frame.PixelHeight);
    }

    private static byte[] CreateSkinPng()
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x44;
            pixels[i + 1] = 0x88;
            pixels[i + 2] = 0xCC;
            pixels[i + 3] = byte.MaxValue;
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
}
