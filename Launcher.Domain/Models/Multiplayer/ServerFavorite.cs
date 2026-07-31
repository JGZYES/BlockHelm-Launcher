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
/// 服务器收藏
/// </summary>
public sealed class ServerFavorite
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = 25565;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? Motd { get; set; }
    public int? Players { get; set; }
    public int? MaxPlayers { get; set; }
    public string? Version { get; set; }
    public ServerFavoriteCategory Category { get; set; } = ServerFavoriteCategory.Default;
    public int DisplayOrder { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayedAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public ServerStatus Status { get; set; } = ServerStatus.Unknown;
}

/// <summary>
/// 服务器收藏分类
/// </summary>
public enum ServerFavoriteCategory
{
    Default,
    Survival,
    Creative,
    Minigame,
    Roleplay,
    Technical,
    Other
}

/// <summary>
/// 服务器状态
/// </summary>
public enum ServerStatus
{
    Unknown,
    Online,
    Offline,
    Maintenance
}

/// <summary>
/// 服务器收藏夹汇总
/// </summary>
public sealed class ServerFavoritesSummary
{
    public int TotalCount { get; set; }
    public int OnlineCount { get; set; }
    public IReadOnlyList<ServerFavoriteCategorySummary> Categories { get; set; } = [];
}

/// <summary>
/// 分类汇总
/// </summary>
public sealed class ServerFavoriteCategorySummary
{
    public ServerFavoriteCategory Category { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Count { get; set; }
    public IReadOnlyList<ServerFavorite> Servers { get; set; } = [];
}