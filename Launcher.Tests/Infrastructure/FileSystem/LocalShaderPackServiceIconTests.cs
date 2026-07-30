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

namespace Launcher.Tests.Infrastructure.FileSystem;

/// <summary>
/// 验证 LocalShaderPackService 从 zip 根 pack.png 提取光影包图标，缺失时回退默认占位图标。
/// </summary>
public sealed class LocalShaderPackServiceIconTests : TestTempDirectory
{
    [Fact]
    public async Task GetShaderPacksAsyncExtractsIconFromRootPackPng()
    {
        var instance = await PlaceShaderPackAsync("with-icon.zip",
            ("pack.png", CreatePng()));
        var service = new LocalShaderPackService(pathProvider: new LauncherPathProvider(TempRoot));

        var pack = (await service.GetShaderPacksAsync(instance)).Single();

        Assert.False(string.IsNullOrWhiteSpace(pack.IconSource));
        Assert.StartsWith("file:///", pack.IconSource);
    }

    [Fact]
    public async Task GetShaderPacksAsyncLeavesIconSourceNullWhenNoPackPng()
    {
        var instance = await PlaceShaderPackAsync("no-icon.zip",
            ("shaders/final.fsh", "// shader source"u8.ToArray()));
        var service = new LocalShaderPackService(pathProvider: new LauncherPathProvider(TempRoot));

        var pack = (await service.GetShaderPacksAsync(instance)).Single();

        Assert.Null(pack.IconSource);
    }

    private async Task<GameInstance> PlaceShaderPackAsync(string packName, params (string EntryName, byte[] Content)[] entries)
    {
        var instanceDir = Path.Combine(TempRoot, "instance-" + Guid.NewGuid().ToString("N"));
        var shaderDir = Directory.CreateDirectory(Path.Combine(instanceDir, "shaderpacks")).FullName;
        var archivePath = Path.Combine(shaderDir, packName);

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
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
