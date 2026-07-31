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

namespace Launcher.Domain.Models;

/// <summary>
/// Mod JAR 文件中提取的元数据
/// </summary>
public sealed class ModMetadata
{
    public string? ModId { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? DisplayName { get; set; }
    public string? Authors { get; set; }
    public string? Description { get; set; }
    public string? License { get; set; }
    public string? Icon { get; set; }
    public IReadOnlyList<string> Depends { get; set; } = [];
    public IReadOnlyList<string> Recommends { get; set; } = [];
    public string? LoaderType { get; set; }
    public IReadOnlyList<string> MinecraftVersions { get; set; } = [];
    public string? SourceFile { get; set; }
}

/// <summary>
/// Mod 更新信息
/// </summary>
public sealed class ModUpdateInfo
{
    public string? ModId { get; set; }
    public string? Name { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string? LatestVersionId { get; set; }
    public string? LatestFileUrl { get; set; }
    public string? Changelog { get; set; }
    public DateTimeOffset? LatestReleaseDate { get; set; }
    public string SourcePlatform { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public bool HasUpdate => !string.Equals(CurrentVersion, LatestVersion, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Mod 更新检查结果
/// </summary>
public sealed class ModUpdateCheckResult
{
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string? CurrentVersion { get; set; }
    public ModUpdateInfo? UpdateInfo { get; set; }
    public bool HasUpdate => UpdateInfo?.HasUpdate ?? false;
    public string? ErrorMessage { get; set; }
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}