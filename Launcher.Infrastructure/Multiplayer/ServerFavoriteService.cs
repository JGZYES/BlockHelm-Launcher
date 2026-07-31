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
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Multiplayer;

/// <summary>
/// 服务器收藏服务实现
/// </summary>
public sealed class ServerFavoriteService : IServerFavoriteService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string dataDirectory;
    private readonly string favoritesPath;
    private readonly ILogger<ServerFavoriteService> logger;
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private readonly List<ServerFavorite> favorites = [];

    public event EventHandler<ServerFavoriteEventArgs>? FavoriteAdded;
    public event EventHandler<ServerFavoriteEventArgs>? FavoriteRemoved;
    public event EventHandler<ServerFavoriteEventArgs>? FavoriteUpdated;
    public event EventHandler<ServerFavoriteStatusCheckEventArgs>? StatusCheckCompleted;

    public ServerFavoriteService(LauncherPathProvider pathProvider, ILogger<ServerFavoriteService>? logger = null)
    {
        dataDirectory = Path.Combine(pathProvider.DefaultDataDirectory, "multiplayer", "favorites");
        favoritesPath = Path.Combine(dataDirectory, "servers.json");
        this.logger = logger ?? NullLogger<ServerFavoriteService>.Instance;
        Directory.CreateDirectory(dataDirectory);
        LoadFavorites();
    }

    public async Task<IReadOnlyList<ServerFavorite>> GetAllFavoritesAsync(CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            return favorites.OrderBy(f => f.Category).ThenBy(f => f.DisplayOrder).ToList();
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task<IReadOnlyList<ServerFavorite>> GetFavoritesByCategoryAsync(
        ServerFavoriteCategory category,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            return favorites
                .Where(f => f.Category == category)
                .OrderBy(f => f.DisplayOrder)
                .ToList();
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task<ServerFavoritesSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            var categories = favorites
                .GroupBy(f => f.Category)
                .Select(g => new ServerFavoriteCategorySummary
                {
                    Category = g.Key,
                    DisplayName = GetCategoryDisplayName(g.Key),
                    Count = g.Count(),
                    Servers = g.OrderBy(f => f.DisplayOrder).ToList()
                })
                .OrderBy(c => c.Category)
                .ToList();

            return new ServerFavoritesSummary
            {
                TotalCount = favorites.Count,
                OnlineCount = favorites.Count(f => f.Status == ServerStatus.Online),
                Categories = categories
            };
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task<ServerFavorite> AddFavoriteAsync(
        string name,
        string address,
        int port = 25565,
        string? description = null,
        ServerFavoriteCategory category = ServerFavoriteCategory.Default,
        CancellationToken cancellationToken = default)
    {
        var favorite = new ServerFavorite
        {
            Name = name,
            Address = address,
            Port = port,
            Description = description,
            Category = category,
            DisplayOrder = favorites.Count(f => f.Category == category),
            AddedAt = DateTimeOffset.UtcNow
        };

        await ioLock.WaitAsync(cancellationToken);
        try
        {
            favorites.Add(favorite);
            SaveFavoritesCore();
        }
        finally
        {
            ioLock.Release();
        }

        FavoriteAdded?.Invoke(this, new ServerFavoriteEventArgs(favorite));
        logger.LogInformation("Server favorite added. Name={Name} Address={Address} Port={Port}", name, address, port);
        return favorite;
    }

    public async Task UpdateFavoriteAsync(
        string favoriteId,
        string? name = null,
        string? address = null,
        int? port = null,
        string? description = null,
        ServerFavoriteCategory? category = null,
        CancellationToken cancellationToken = default)
    {
        ServerFavorite? favorite;
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            favorite = favorites.FirstOrDefault(f => f.Id == favoriteId);
            if (favorite is null)
                return;

            if (name is not null) favorite.Name = name;
            if (address is not null) favorite.Address = address;
            if (port.HasValue) favorite.Port = port.Value;
            if (description is not null) favorite.Description = description;
            if (category.HasValue) favorite.Category = category.Value;

            SaveFavoritesCore();
        }
        finally
        {
            ioLock.Release();
        }

        if (favorite is not null)
        {
            FavoriteUpdated?.Invoke(this, new ServerFavoriteEventArgs(favorite));
            logger.LogInformation("Server favorite updated. Id={Id}", favoriteId);
        }
    }

    public async Task<bool> RemoveFavoriteAsync(string favoriteId, CancellationToken cancellationToken = default)
    {
        ServerFavorite? favorite;
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            favorite = favorites.FirstOrDefault(f => f.Id == favoriteId);
            if (favorite is null)
                return false;

            favorites.Remove(favorite);
            SaveFavoritesCore();
        }
        finally
        {
            ioLock.Release();
        }

        if (favorite is not null)
        {
            FavoriteRemoved?.Invoke(this, new ServerFavoriteEventArgs(favorite));
            logger.LogInformation("Server favorite removed. Id={Id} Name={Name}", favoriteId, favorite.Name);
            return true;
        }

        return false;
    }

    public async Task<ServerFavorite> CheckServerStatusAsync(
        string favoriteId,
        CancellationToken cancellationToken = default)
    {
        ServerFavorite? favorite;
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            favorite = favorites.FirstOrDefault(f => f.Id == favoriteId);
        }
        finally
        {
            ioLock.Release();
        }

        if (favorite is null)
            throw new InvalidOperationException($"Server favorite not found: {favoriteId}");

        var status = await PingServerAsync(favorite.Address, favorite.Port, cancellationToken);
        favorite.Status = status.Status;
        favorite.LastCheckedAt = DateTimeOffset.UtcNow;
        favorite.Players = status.Players;
        favorite.MaxPlayers = status.MaxPlayers;
        favorite.Version = status.Version;
        favorite.Motd = status.Motd;

        await ioLock.WaitAsync(cancellationToken);
        try
        {
            SaveFavoritesCore();
        }
        finally
        {
            ioLock.Release();
        }

        FavoriteUpdated?.Invoke(this, new ServerFavoriteEventArgs(favorite));
        return favorite;
    }

    public async Task<IReadOnlyList<ServerFavorite>> CheckAllServerStatusAsync(
        IProgress<(int Current, int Total, string ServerName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<ServerFavorite> results;
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            results = favorites.ToList();
        }
        finally
        {
            ioLock.Release();
        }

        var total = results.Count;
        for (var i = 0; i < results.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var favorite = results[i];
            progress?.Report((i + 1, total, favorite.Name));

            try
            {
                var status = await PingServerAsync(favorite.Address, favorite.Port, cancellationToken);
                favorite.Status = status.Status;
                favorite.LastCheckedAt = DateTimeOffset.UtcNow;
                favorite.Players = status.Players;
                favorite.MaxPlayers = status.MaxPlayers;
                favorite.Version = status.Version;
                favorite.Motd = status.Motd;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to check server status for {Name}", favorite.Name);
                favorite.Status = ServerStatus.Offline;
                favorite.LastCheckedAt = DateTimeOffset.UtcNow;
            }
        }

        await ioLock.WaitAsync(cancellationToken);
        try
        {
            SaveFavoritesCore();
        }
        finally
        {
            ioLock.Release();
        }

        StatusCheckCompleted?.Invoke(this, new ServerFavoriteStatusCheckEventArgs(results));
        return results;
    }

    public async Task MoveFavoriteAsync(
        string favoriteId,
        int newDisplayOrder,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            var favorite = favorites.FirstOrDefault(f => f.Id == favoriteId);
            if (favorite is null)
                return;

            favorite.DisplayOrder = newDisplayOrder;
            SaveFavoritesCore();
        }
        finally
        {
            ioLock.Release();
        }
    }

    public IReadOnlyList<ServerFavoriteCategoryInfo> GetAvailableCategories()
    {
        return
        [
            new ServerFavoriteCategoryInfo { Category = ServerFavoriteCategory.Default, DisplayName = "默认", IconName = "star" },
            new ServerFavoriteCategoryInfo { Category = ServerFavoriteCategory.Survival, DisplayName = "生存", IconName = "sword" },
            new ServerFavoriteCategoryInfo { Category = ServerFavoriteCategory.Creative, DisplayName = "创造", IconName = "creative" },
            new ServerFavoriteCategoryInfo { Category = ServerFavoriteCategory.Minigame, DisplayName = "小游戏", IconName = "minigame" },
            new ServerFavoriteCategoryInfo { Category = ServerFavoriteCategory.Roleplay, DisplayName = "角色扮演", IconName = "roleplay" },
            new ServerFavoriteCategoryInfo { Category = ServerFavoriteCategory.Technical, DisplayName = "技术", IconName = "technical" },
            new ServerFavoriteCategoryInfo { Category = ServerFavoriteCategory.Other, DisplayName = "其他", IconName = "other" }
        ];
    }

    private static string GetCategoryDisplayName(ServerFavoriteCategory category)
    {
        return category switch
        {
            ServerFavoriteCategory.Default => "默认",
            ServerFavoriteCategory.Survival => "生存",
            ServerFavoriteCategory.Creative => "创造",
            ServerFavoriteCategory.Minigame => "小游戏",
            ServerFavoriteCategory.Roleplay => "角色扮演",
            ServerFavoriteCategory.Technical => "技术",
            ServerFavoriteCategory.Other => "其他",
            _ => category.ToString()
        };
    }

    private async Task<ServerPingResult> PingServerAsync(
        string address,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await client.ConnectAsync(address, port, linkedCts.Token);

            if (!client.Connected)
                return new ServerPingResult { Status = ServerStatus.Offline };

            // Simple connection check - mark as online
            // Full server list ping (SLP) would require implementing the Minecraft protocol
            return new ServerPingResult
            {
                Status = ServerStatus.Online,
                LastCheckedAt = DateTimeOffset.UtcNow
            };
        }
        catch (OperationCanceledException)
        {
            return new ServerPingResult { Status = ServerStatus.Offline };
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            logger.LogDebug(ex, "Server ping failed for {Address}:{Port}", address, port);
            return new ServerPingResult { Status = ServerStatus.Offline };
        }
    }

    private class ServerPingResult
    {
        public ServerStatus Status { get; set; }
        public int? Players { get; set; }
        public int? MaxPlayers { get; set; }
        public string? Version { get; set; }
        public string? Motd { get; set; }
        public DateTimeOffset LastCheckedAt { get; set; }
    }

    private void LoadFavorites()
    {
        try
        {
            if (!File.Exists(favoritesPath))
                return;

            using var stream = File.OpenRead(favoritesPath);
            var loaded = JsonSerializer.Deserialize<List<ServerFavorite>>(stream, JsonOptions);
            if (loaded is not null)
            {
                favorites.AddRange(loaded);
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Failed to load server favorites from {Path}", favoritesPath);
        }
    }

    private void SaveFavoritesCore()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            using var stream = new FileStream(favoritesPath, FileMode.Create, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, favorites, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Failed to save server favorites to {Path}", favoritesPath);
        }
    }
}