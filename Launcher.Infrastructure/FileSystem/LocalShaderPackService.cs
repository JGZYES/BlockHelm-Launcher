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
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LocalShaderPackService : ILocalShaderPackService
{
    private const string SupportedArchiveExtension = ".zip";
    private const string ShaderPackIconEntryName = "pack.png";
    private readonly LauncherPathProvider pathProvider;
    private readonly ILogger<LocalShaderPackService> logger;
    private readonly IUserFileDeletionService userFileDeletionService;
    private readonly string iconCacheDirectory;

    public LocalShaderPackService(
        ILogger<LocalShaderPackService>? logger = null,
        IUserFileDeletionService? userFileDeletionService = null,
        LauncherPathProvider? pathProvider = null)
    {
        this.pathProvider = pathProvider ?? new LauncherPathProvider();
        this.logger = logger ?? NullLogger<LocalShaderPackService>.Instance;
        this.userFileDeletionService = userFileDeletionService ?? new UserFileDeletionService();
        // 光影包图标缓存独立于资源包目录，避免互相清理；缓存键含归档修改信息，包更新后自然失效。
        iconCacheDirectory = Path.Combine(this.pathProvider.DefaultDataDirectory, "cache", "shaderpacks", "icons");
    }

    public Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Task.Run<IReadOnlyList<LocalShaderPack>>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var shaderPacksDirectory = GetShaderPacksDirectory(instance);
                if (!Directory.Exists(shaderPacksDirectory))
                {
                    logger.LogDebug(
                        "No local shader packs directory found. InstanceId={InstanceId} ShaderPacksDirectory={ShaderPacksDirectory}",
                        instance.Id,
                        shaderPacksDirectory);
                    return [];
                }

                var shaderPacks = Directory.EnumerateFiles(
                        shaderPacksDirectory,
                        $"*{SupportedArchiveExtension}",
                        SearchOption.TopDirectoryOnly)
                    .Select(ToLocalShaderPack)
                    .OrderByDescending(shaderPack => shaderPack.CreatedAt)
                    .ThenBy(shaderPack => shaderPack.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                logger.LogDebug(
                    "Local shader packs loaded. InstanceId={InstanceId} Count={ShaderPackCount}",
                    instance.Id,
                    shaderPacks.Length);
                return shaderPacks;
            },
            cancellationToken);
    }

    public Task<LocalShaderPackImportResult> ImportAsync(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        return Task.Run(
            () => ImportCore(instance, archivePath, cancellationToken),
            cancellationToken);
    }

    public Task DeleteAsync(LocalShaderPack shaderPack, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shaderPack);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(shaderPack.FullPath))
                {
                    logger.LogDebug(
                        "Skipping local shader pack delete because file does not exist. Path={Path}",
                        shaderPack.FullPath);
                    return;
                }

                userFileDeletionService.DeleteFile(shaderPack.FullPath);
                logger.LogInformation("Local shader pack deleted. Name={Name}", shaderPack.Name);
                logger.LogDebug("Deleted local shader pack path. Path={Path}", shaderPack.FullPath);
            },
            cancellationToken);
    }

    public Task DeleteAsync(IEnumerable<LocalShaderPack> shaderPacks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shaderPacks);

        return Task.Run(
            async () =>
            {
                foreach (var shaderPack in shaderPacks.DistinctBy(shaderPack => shaderPack.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DeleteAsync(shaderPack, cancellationToken);
                }
            },
            cancellationToken);
    }

    private LocalShaderPackImportResult ImportCore(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(normalizedArchivePath))
        {
            logger.LogDebug(
                "Skipping local shader pack import because archive does not exist. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.FileNotFound);
        }

        if (!normalizedArchivePath.EndsWith(SupportedArchiveExtension, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "Skipping local shader pack import because archive type is unsupported. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnsupportedArchive);
        }

        logger.LogDebug(
            "Importing local shader pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
            instance.Id,
            normalizedArchivePath);

        try
        {
            var shaderPacksDirectory = GetShaderPacksDirectory(instance);
            Directory.CreateDirectory(shaderPacksDirectory);

            var targetPath = ResolveUniqueFilePath(shaderPacksDirectory, Path.GetFileName(normalizedArchivePath));
            File.Copy(normalizedArchivePath, targetPath, overwrite: false);

            var importedShaderPack = ToLocalShaderPack(targetPath);
            logger.LogDebug(
                "Local shader pack archive imported. InstanceId={InstanceId} ArchivePath={ArchivePath} ShaderPackPath={ShaderPackPath}",
                instance.Id,
                normalizedArchivePath,
                targetPath);
            return LocalShaderPackImportResult.Success(importedShaderPack);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local shader pack archive because a file operation failed. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local shader pack archive because access was denied. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected failure while importing local shader pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        }
    }

    private LocalShaderPack ToLocalShaderPack(string path)
    {
        var file = new FileInfo(path);
        return new LocalShaderPack
        {
            Name = Path.GetFileNameWithoutExtension(file.Name),
            FileName = file.Name,
            FullPath = file.FullName,
            // 光影包（Iris/OptiFine）标准图标位于 zip 根目录 pack.png；找不到时返回 null，UI 回退默认图标。
            IconSource = EmbeddedArchiveIconCache.TryCacheIcon(file, ShaderPackIconEntryName, iconCacheDirectory, logger),
            CreatedAt = new DateTimeOffset(file.CreationTimeUtc)
        };
    }

    private static string ResolveUniqueFilePath(string directory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var index = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
            index++;
        }

        return candidate;
    }

    private static string GetShaderPacksDirectory(GameInstance instance)
    {
        return Path.Combine(instance.InstanceDirectory, "shaderpacks");
    }
}
