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

using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.Mods;

public sealed class ModServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ModServiceImportsDisablesAndEnablesJar()
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", "modded");
        Directory.CreateDirectory(instanceDirectory);
        var sourceJar = Path.Combine(TempRoot, "example.jar");
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(sourceJar, "fake jar");

        var instance = new GameInstance { InstanceDirectory = instanceDirectory };
        var service = CreateService();

        var imported = await service.ImportAsync(instance, sourceJar);
        await service.SetEnabledAsync(imported, false);
        var disabled = (await service.GetModsAsync(instance)).Single();

        Assert.False(disabled.IsEnabled);
        Assert.Equal("example.jar.disabled", disabled.FileName);
        Assert.True(File.Exists(Path.Combine(instanceDirectory, "mods", "example.jar.disabled")));

        await service.SetEnabledAsync(disabled, true);
        var enabled = (await service.GetModsAsync(instance)).Single();

        Assert.True(enabled.IsEnabled);
        Assert.Equal("example.jar", enabled.FileName);
        Assert.True(File.Exists(Path.Combine(instanceDirectory, "mods", "example.jar")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetEnabledAsyncRejectsExistingTargetWithoutChangingEitherFile(bool enabled)
    {
        var modsDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instances", "conflict", "mods")).FullName;
        var enabledPath = Path.Combine(modsDirectory, "example.jar");
        var disabledPath = Path.Combine(modsDirectory, "example.jar.disabled");
        await File.WriteAllTextAsync(enabledPath, "enabled-content");
        await File.WriteAllTextAsync(disabledPath, "disabled-content");
        var sourcePath = enabled ? disabledPath : enabledPath;
        var targetPath = enabled ? enabledPath : disabledPath;
        var mod = CreateLocalMod(sourcePath, isEnabled: !enabled);
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ModEnabledStateConflictException>(
            () => service.SetEnabledAsync(mod, enabled));

        Assert.Equal(targetPath, exception.TargetPath);
        Assert.Equal("enabled-content", await File.ReadAllTextAsync(enabledPath));
        Assert.Equal("disabled-content", await File.ReadAllTextAsync(disabledPath));
    }

    [Fact]
    public async Task SetEnabledAsyncRejectsExistingTargetDirectoryWithoutChangingSource()
    {
        var modsDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instances", "directory-conflict", "mods")).FullName;
        var sourcePath = Path.Combine(modsDirectory, "example.jar.disabled");
        var targetPath = Path.Combine(modsDirectory, "example.jar");
        await File.WriteAllTextAsync(sourcePath, "source-content");
        Directory.CreateDirectory(targetPath);
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ModEnabledStateConflictException>(
            () => service.SetEnabledAsync(CreateLocalMod(sourcePath, isEnabled: false), enabled: true));

        Assert.Equal(targetPath, exception.TargetPath);
        Assert.Equal("source-content", await File.ReadAllTextAsync(sourcePath));
        Assert.True(Directory.Exists(targetPath));
    }

    [Fact]
    public async Task MoveFileWithoutOverwriteMapsTargetCreatedAfterPrecheckToConflict()
    {
        var modsDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instances", "race-conflict", "mods")).FullName;
        var sourcePath = Path.Combine(modsDirectory, "example.jar.disabled");
        var targetPath = Path.Combine(modsDirectory, "example.jar");
        await File.WriteAllTextAsync(sourcePath, "source-content");
        await File.WriteAllTextAsync(targetPath, "target-content");

        var exception = Assert.Throws<ModEnabledStateConflictException>(
            () => ModService.MoveFileWithoutOverwrite(sourcePath, targetPath));

        Assert.Equal(targetPath, exception.TargetPath);
        Assert.Equal("source-content", await File.ReadAllTextAsync(sourcePath));
        Assert.Equal("target-content", await File.ReadAllTextAsync(targetPath));
    }

    [Fact]
    public async Task ModServiceImportAsyncOverwritesExistingJarWhenRequested()
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", "overwrite");
        Directory.CreateDirectory(instanceDirectory);
        var sourceJar = Path.Combine(TempRoot, "replace-me.jar");
        await File.WriteAllTextAsync(sourceJar, "first");

        var instance = new GameInstance { InstanceDirectory = instanceDirectory };
        var service = CreateService();

        await service.ImportAsync(instance, sourceJar);
        await File.WriteAllTextAsync(sourceJar, "second");

        await service.ImportAsync(instance, sourceJar, overwriteExisting: true);

        var importedPath = Path.Combine(instanceDirectory, "mods", "replace-me.jar");
        Assert.Equal("second", await File.ReadAllTextAsync(importedPath));
        Assert.Single(await service.GetModsAsync(instance));
    }

    private ModService CreateService()
    {
        return new ModService(new LauncherPathProvider(TempRoot));
    }

    private static LocalMod CreateLocalMod(string fullPath, bool isEnabled) => new()
    {
        Name = "Example",
        FileName = Path.GetFileName(fullPath),
        FullPath = fullPath,
        IsEnabled = isEnabled
    };

    private static (string EntryName, byte[] Content) TextEntry(string entryName, string content)
    {
        return (entryName, Encoding.UTF8.GetBytes(content));
    }

}
