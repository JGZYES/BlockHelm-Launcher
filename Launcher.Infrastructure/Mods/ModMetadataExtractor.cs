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
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Mods;

/// <summary>
/// 从 Mod JAR 文件中提取元数据（支持 Fabric、Forge、NeoForge、Quilt）
/// </summary>
public sealed class ModMetadataExtractor : IModMetadataExtractor
{
    private readonly ILogger<ModMetadataExtractor> logger;

    public ModMetadataExtractor(ILogger<ModMetadataExtractor>? logger = null)
    {
        this.logger = logger ?? NullLogger<ModMetadataExtractor>.Instance;
    }

    public async Task<ModMetadata?> ExtractAsync(
        string jarFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(jarFilePath))
            return null;

        try
        {
            using var stream = File.OpenRead(jarFilePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var metadata = new ModMetadata
            {
                SourceFile = jarFilePath
            };

            // 尝试读取 fabric.mod.json
            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry is not null)
            {
                await ExtractFabricMetadataAsync(fabricEntry, metadata, cancellationToken);
                return metadata;
            }

            // 尝试读取 mods.toml (Forge/NeoForge)
            var forgeEntry = archive.GetEntry("META-INF/mods.toml");
            if (forgeEntry is not null)
            {
                ExtractForgeMetadata(forgeEntry, metadata);
                return metadata;
            }

            // 尝试读取 mcmod.info（旧格式）
            var legacyEntry = archive.GetEntry("mcmod.info");
            if (legacyEntry is not null)
            {
                await ExtractLegacyMetadataAsync(legacyEntry, metadata, cancellationToken);
                return metadata;
            }

            // 从 JAR 文件名推断
            InferFromFileName(jarFilePath, metadata);
            return metadata;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to extract mod metadata from {FilePath}", jarFilePath);
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<string, ModMetadata?>> ExtractBatchAsync(
        IReadOnlyList<string> jarFilePaths,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, ModMetadata?>(StringComparer.OrdinalIgnoreCase);
        var tasks = jarFilePaths.Select(async path =>
        {
            var metadata = await ExtractAsync(path, cancellationToken);
            lock (results)
            {
                results[path] = metadata;
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    private async Task ExtractFabricMetadataAsync(
        ZipArchiveEntry entry,
        ModMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = entry.Open();
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            metadata.ModId = GetStringOrDefault(root, "id");
            metadata.Version = GetStringOrDefault(root, "version");
            metadata.Name = GetStringOrDefault(root, "name");
            metadata.DisplayName = metadata.Name;
            metadata.Description = GetStringOrDefault(root, "description");
            metadata.License = GetStringOrDefault(root, "license");
            metadata.LoaderType = "fabric";

            if (root.TryGetProperty("depends", out var depends))
            {
                metadata.Depends = depends.EnumerateObject()
                    .Select(p => p.Name)
                    .ToList();
            }

            if (root.TryGetProperty("recommends", out var recommends))
            {
                metadata.Recommends = recommends.EnumerateObject()
                    .Select(p => p.Name)
                    .ToList();
            }

            if (root.TryGetProperty("contact", out var contact) &&
                contact.TryGetProperty("homepage", out _))
            {
                // 可以提取联系信息
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to parse fabric.mod.json");
        }
    }

    private void ExtractForgeMetadata(ZipArchiveEntry entry, ModMetadata metadata)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            // 简单的 TOML 解析（仅提取关键字段）
            var modId = ExtractTomlString(content, "modId");
            var version = ExtractTomlString(content, "version");
            var displayName = ExtractTomlString(content, "displayName");
            var description = ExtractTomlString(content, "description");

            metadata.ModId = modId;
            metadata.Version = version;
            metadata.Name = displayName ?? modId;
            metadata.DisplayName = displayName;
            metadata.Description = description;

            // 检测 Forge 或 NeoForge
            if (content.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
                metadata.LoaderType = "neoforge";
            else
                metadata.LoaderType = "forge";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to parse mods.toml");
        }
    }

    private async Task ExtractLegacyMetadataAsync(
        ZipArchiveEntry entry,
        ModMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = entry.Open();
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                metadata.ModId = GetStringOrDefault(first, "modid");
                metadata.Version = GetStringOrDefault(first, "version");
                metadata.Name = GetStringOrDefault(first, "name");
                metadata.DisplayName = metadata.Name;
                metadata.Description = GetStringOrDefault(first, "description");
                metadata.LoaderType = "forge";
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to parse mcmod.info");
        }
    }

    private void InferFromFileName(string filePath, ModMetadata metadata)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        metadata.Name = fileName;
        metadata.DisplayName = fileName;

        // 尝试从文件名推断版本号 (modid-version.jar)
        var lastDashIndex = fileName.LastIndexOf('-');
        if (lastDashIndex > 0)
        {
            var possibleVersion = fileName.Substring(lastDashIndex + 1);
            if (possibleVersion.Any(char.IsDigit))
            {
                metadata.Version = possibleVersion;
                metadata.ModId = fileName.Substring(0, lastDashIndex);
            }
            else
            {
                metadata.ModId = fileName;
            }
        }
        else
        {
            metadata.ModId = fileName;
        }
    }

    private static string? GetStringOrDefault(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        return null;
    }

    private static string? ExtractTomlString(string content, string key)
    {
        var pattern = $"[\"']([^\"']*)[\"']\\s*\\.\\s*{key}\\s*=\\s*[\"']([^\"']*)[\"']";
        // 简单提取: modId = "xxx"
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            $"{key}\\s*=\\s*\"([^\"]*)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success)
            return match.Groups[1].Value;

        // 尝试单引号
        match = System.Text.RegularExpressions.Regex.Match(
            content,
            $"{key}\\s*=\\s*'([^']*)'",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }
}