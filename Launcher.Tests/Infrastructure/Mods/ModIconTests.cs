/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Mods;

/// <summary>
/// 验证 ModService 从各 Loader 声明中识别内嵌图标入口，并在缺失时回退 jar 根 pack.png。
/// </summary>
public sealed class ModIconTests : TestTempDirectory
{
    [Fact]
    public async Task GetModsAsyncExtractsIconFromFabricModJson()
    {
        var instance = await PlaceJarAsync("fabric-mod.jar",
            ("fabric.mod.json", "{\"id\":\"demo\",\"name\":\"Demo\",\"version\":\"1.0\",\"icon\":\"icon.png\"}"u8.ToArray()),
            ("icon.png", CreatePng()));
        var service = new ModService(new LauncherPathProvider(TempRoot));

        var mod = (await service.GetModsAsync(instance)).Single();

        Assert.False(string.IsNullOrWhiteSpace(mod.IconSource));
        Assert.StartsWith("file:///", mod.IconSource);
    }

    [Fact]
    public async Task GetModsAsyncLeavesIconSourceNullWhenNoIconAvailable()
    {
        var instance = await PlaceJarAsync("plain-mod.jar",
            ("fabric.mod.json", "{\"id\":\"demo\",\"name\":\"Demo\",\"version\":\"1.0\"}"u8.ToArray()));
        var service = new ModService(new LauncherPathProvider(TempRoot));

        var mod = (await service.GetModsAsync(instance)).Single();

        Assert.Null(mod.IconSource);
    }

    [Fact]
    public async Task GetModsAsyncFallsBackToRootPackPng()
    {
        var instance = await PlaceJarAsync("packpng-mod.jar",
            ("fabric.mod.json", "{\"id\":\"demo\",\"name\":\"Demo\",\"version\":\"1.0\"}"u8.ToArray()),
            ("pack.png", CreatePng()));
        var service = new ModService(new LauncherPathProvider(TempRoot));

        var mod = (await service.GetModsAsync(instance)).Single();

        Assert.False(string.IsNullOrWhiteSpace(mod.IconSource));
    }

    [Fact]
    public async Task GetModsAsyncDoesNotThrowForCorruptJar()
    {
        var instanceDir = Path.Combine(TempRoot, "instance-" + Guid.NewGuid().ToString("N"));
        var modsDir = Directory.CreateDirectory(Path.Combine(instanceDir, "mods")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(modsDir, "broken.jar"), [1, 2, 3]);
        var instance = new GameInstance { InstanceDirectory = instanceDir };
        var service = new ModService(new LauncherPathProvider(TempRoot));

        var mod = (await service.GetModsAsync(instance)).Single();

        Assert.Null(mod.IconSource);
    }

    private async Task<GameInstance> PlaceJarAsync(string jarName, params (string EntryName, byte[] Content)[] entries)
    {
        var instanceDir = Path.Combine(TempRoot, "instance-" + Guid.NewGuid().ToString("N"));
        var modsDir = Directory.CreateDirectory(Path.Combine(instanceDir, "mods")).FullName;
        var jarPath = Path.Combine(modsDir, jarName);

        using var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            await stream.WriteAsync(content);
        }

        return new GameInstance { InstanceDirectory = instanceDir };
    }

    private static byte[] CreatePng()
    {
        var pixels = new byte[] { 0x66, 0xAA, 0xDD, byte.MaxValue };
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
