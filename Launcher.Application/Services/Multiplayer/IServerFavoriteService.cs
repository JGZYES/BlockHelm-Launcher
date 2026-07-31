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

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

/// <summary>
/// 服务器收藏服务接口
/// </summary>
public interface IServerFavoriteService
{
    /// <summary>
    /// 获取所有服务器收藏
    /// </summary>
    Task<IReadOnlyList<ServerFavorite>> GetAllFavoritesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按分类获取服务器收藏
    /// </summary>
    Task<IReadOnlyList<ServerFavorite>> GetFavoritesByCategoryAsync(
        ServerFavoriteCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取收藏汇总
    /// </summary>
    Task<ServerFavoritesSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加服务器收藏
    /// </summary>
    Task<ServerFavorite> AddFavoriteAsync(
        string name,
        string address,
        int port = 25565,
        string? description = null,
        ServerFavoriteCategory category = ServerFavoriteCategory.Default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新服务器收藏
    /// </summary>
    Task UpdateFavoriteAsync(
        string favoriteId,
        string? name = null,
        string? address = null,
        int? port = null,
        string? description = null,
        ServerFavoriteCategory? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除服务器收藏
    /// </summary>
    Task<bool> RemoveFavoriteAsync(string favoriteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查服务器状态
    /// </summary>
    Task<ServerFavorite> CheckServerStatusAsync(
        string favoriteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量检查服务器状态
    /// </summary>
    Task<IReadOnlyList<ServerFavorite>> CheckAllServerStatusAsync(
        IProgress<(int Current, int Total, string ServerName)>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移动服务器收藏顺序
    /// </summary>
    Task MoveFavoriteAsync(
        string favoriteId,
        int newDisplayOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有可用分类
    /// </summary>
    IReadOnlyList<ServerFavoriteCategoryInfo> GetAvailableCategories();

    event EventHandler<ServerFavoriteEventArgs>? FavoriteAdded;
    event EventHandler<ServerFavoriteEventArgs>? FavoriteRemoved;
    event EventHandler<ServerFavoriteEventArgs>? FavoriteUpdated;
    event EventHandler<ServerFavoriteStatusCheckEventArgs>? StatusCheckCompleted;
}

/// <summary>
/// 分类信息
/// </summary>
public sealed class ServerFavoriteCategoryInfo
{
    public ServerFavoriteCategory Category { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string IconName { get; set; } = string.Empty;
}

/// <summary>
/// 服务器收藏事件参数
/// </summary>
public sealed class ServerFavoriteEventArgs : EventArgs
{
    public ServerFavoriteEventArgs(ServerFavorite favorite)
    {
        Favorite = favorite;
    }

    public ServerFavorite Favorite { get; }
}

/// <summary>
/// 服务器状态检查完成事件参数
/// </summary>
public sealed class ServerFavoriteStatusCheckEventArgs : EventArgs
{
    public ServerFavoriteStatusCheckEventArgs(IReadOnlyList<ServerFavorite> results)
    {
        Results = results;
    }

    public IReadOnlyList<ServerFavorite> Results { get; }
}