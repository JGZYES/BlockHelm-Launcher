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
/// Mod 更新检查服务
/// </summary>
public interface IModUpdateService
{
    /// <summary>
    /// 检查单个 Mod 是否有更新
    /// </summary>
    Task<ModUpdateCheckResult> CheckForUpdateAsync(
        LocalMod mod,
        string minecraftVersion,
        string? loaderType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量检查多个 Mod 的更新
    /// </summary>
    Task<IReadOnlyList<ModUpdateCheckResult>> CheckForUpdatesAsync(
        IEnumerable<LocalMod> mods,
        string minecraftVersion,
        string? loaderType = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载 Mod 更新
    /// </summary>
    Task<string?> DownloadUpdateAsync(
        ModUpdateInfo updateInfo,
        string targetDirectory,
        IProgress<(string Status, double Progress)>? progress = null,
        CancellationToken cancellationToken = default);
}