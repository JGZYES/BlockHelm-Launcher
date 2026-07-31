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
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modrinth.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Mods;

/// <summary>
/// Mod 更新检查服务 - 支持 Modrinth、CurseForge 等平台
/// </summary>
public sealed class ModUpdateService : IModUpdateService
{
    private const string ModrinthBaseUrl = "https://api.modrinth.com/v2";
    private readonly IModMetadataExtractor metadataExtractor;
    private readonly HttpClient httpClient;
    private readonly ILogger<ModUpdateService> logger;

    public ModUpdateService(
        IModMetadataExtractor metadataExtractor,
        HttpClient? httpClient = null,
        ILogger<ModUpdateService>? logger = null)
    {
        this.metadataExtractor = metadataExtractor;
        this.httpClient = httpClient ?? new HttpClient();
        this.logger = logger ?? NullLogger<ModUpdateService>.Instance;
    }

    public async Task<ModUpdateCheckResult> CheckForUpdateAsync(
        LocalMod mod,
        string minecraftVersion,
        string? loaderType = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ModUpdateCheckResult
        {
            ModId = mod.ModId ?? mod.Name,
            ModName = mod.Name,
            CurrentVersion = mod.Version
        };

        try
        {
            // 1. 先尝试从 Modrinth 查找
            var updateInfo = await TryCheckModrinthAsync(
                mod, minecraftVersion, loaderType, cancellationToken);

            if (updateInfo is not null)
            {
                result.UpdateInfo = updateInfo;
                return result;
            }

            // 2. 如果有 ProjectReference，使用它
            if (mod.ProjectReference is not null)
            {
                result.ModId = mod.ProjectReference.ProjectId;
            }

            // 3. 未找到更新
            logger.LogDebug(
                "No update found for mod {ModId} ({ModName})",
                mod.ModId, mod.Name);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            logger.LogWarning(ex, "Failed to check for mod update: {ModName}", mod.Name);
        }

        return result;
    }

    public async Task<IReadOnlyList<ModUpdateCheckResult>> CheckForUpdatesAsync(
        IEnumerable<LocalMod> mods,
        string minecraftVersion,
        string? loaderType = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var modList = mods.ToList();
        var results = new List<ModUpdateCheckResult>();
        var semaphore = new SemaphoreSlim(3, 3); // 并发限制

        using var progressLock = new SemaphoreSlim(1, 1);
        var completedCount = 0;
        var totalCount = modList.Count;

        var tasks = modList.Select(async mod =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await CheckForUpdateAsync(mod, minecraftVersion, loaderType, cancellationToken);
                lock (results)
                {
                    results.Add(result);
                }
            }
            finally
            {
                semaphore.Release();
                await progressLock.WaitAsync();
                try
                {
                    Interlocked.Increment(ref completedCount);
                    progress?.Report((double)completedCount / totalCount);
                }
                finally
                {
                    progressLock.Release();
                }
            }
        });

        await Task.WhenAll(tasks);
        return results.OrderBy(r => r.ModName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string?> DownloadUpdateAsync(
        ModUpdateInfo updateInfo,
        string targetDirectory,
        IProgress<(string Status, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(updateInfo.LatestFileUrl))
            return null;

        try
        {
            Directory.CreateDirectory(targetDirectory);
            var fileName = Path.GetFileName(new Uri(updateInfo.LatestFileUrl).AbsolutePath);
            if (string.IsNullOrEmpty(fileName))
                fileName = $"{updateInfo.ModId}-{updateInfo.LatestVersion}.jar";

            var targetPath = Path.Combine(targetDirectory, fileName);

            progress?.Report(("下载中...", 0));

            using var response = await httpClient.GetAsync(
                updateInfo.LatestFileUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;

                if (totalBytes.HasValue)
                {
                    var pct = (double)totalRead / totalBytes.Value * 100;
                    progress?.Report(("下载中...", pct));
                }
            }

            progress?.Report(("完成", 100));
            return targetPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download mod update: {ModId}", updateInfo.ModId);
            return null;
        }
    }

    private async Task<ModUpdateInfo?> TryCheckModrinthAsync(
        LocalMod mod,
        string minecraftVersion,
        string? loaderType,
        CancellationToken cancellationToken)
    {
        var searchQuery = mod.ModId ?? mod.Name;
        if (string.IsNullOrWhiteSpace(searchQuery))
            return null;

        try
        {
            // Modrinth 搜索
            var facets = new List<List<string>>
            {
                new() { "project_type:mod" }
            };

            if (!string.IsNullOrWhiteSpace(minecraftVersion))
                facets.Add(new List<string> { $"versions:{minecraftVersion}" });

            if (!string.IsNullOrWhiteSpace(loaderType) &&
                !string.Equals(loaderType, "Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                facets.Add(new List<string> { $"categories:{loaderType.ToLowerInvariant()}" });
            }

            var url = $"{ModrinthBaseUrl}/search?limit=10&query={Uri.EscapeDataString(searchQuery)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";

            var response = await httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, cancellationToken);
            var hits = response?.Hits ?? [];

            // 优先匹配 modId 或 slug
            var matchedHit = hits.FirstOrDefault(h =>
                string.Equals(h.ProjectId, mod.ModId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(h.Slug, mod.ModId, StringComparison.OrdinalIgnoreCase) ||
                h.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));

            if (matchedHit is null && hits.Count > 0)
                matchedHit = hits[0]; // 使用第一个结果

            if (matchedHit is null)
                return null;

            // 获取该项目的版本列表
            var versionsUrl = $"{ModrinthBaseUrl}/project/{matchedHit.ProjectId}/version?loaders=[{GetLoaderFilter(loaderType)}]&game_versions=[{Uri.EscapeDataString(minecraftVersion)}]";

            var versionsResponse = await httpClient.GetFromJsonAsync<List<ModrinthVersion>>(versionsUrl, cancellationToken);
            var versions = versionsResponse ?? [];

            // 查找最新的 release 版本
            var latestVersion = versions
                .Where(v => string.Equals(v.VersionType, "release", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.DatePublished)
                .FirstOrDefault() ?? versions.FirstOrDefault();

            if (latestVersion is null)
                return null;

            // 比较版本
            if (string.Equals(latestVersion.VersionNumber, mod.Version, StringComparison.OrdinalIgnoreCase))
                return null; // 已是最新

            var file = latestVersion.Files.FirstOrDefault(f => f.IsPrimary) ?? latestVersion.Files.FirstOrDefault();
            var downloadUrl = file?.Url ?? string.Empty;

            return new ModUpdateInfo
            {
                ModId = mod.ModId,
                Name = mod.Name,
                CurrentVersion = mod.Version ?? string.Empty,
                LatestVersion = latestVersion.VersionNumber,
                LatestVersionId = latestVersion.Id,
                LatestFileUrl = downloadUrl,
                Changelog = latestVersion.Changelog,
                LatestReleaseDate = latestVersion.DatePublished,
                SourcePlatform = "Modrinth",
                Slug = matchedHit.Slug
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Modrinth lookup failed for {ModName}", mod.Name);
            return null;
        }
    }

    private static string GetLoaderFilter(string? loaderType)
    {
        return loaderType switch
        {
            "Fabric" => "\"fabric\"",
            "Forge" => "\"forge\"",
            "NeoForge" => "\"neoforge\"",
            "Quilt" => "\"quilt\"",
            _ => "\"fabric\",\"forge\",\"neoforge\",\"quilt\""
        };
    }
}