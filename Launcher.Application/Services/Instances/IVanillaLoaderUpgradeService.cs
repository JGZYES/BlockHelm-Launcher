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
/// 对仅原版（Vanilla）游戏实例执行加载器切换操作：安装 Fabric / Forge / NeoForge / Quilt
/// 并更新实例的持久化 Loader、LoaderVersion 和 VersionName 字段。
/// 非原版实例必须通过新建实例流程，禁止调用本服务。
/// </summary>
public interface IVanillaLoaderUpgradeService
{
    /// <summary>
    /// 查询指定 Minecraft 版本可用的非原版加载器选项（仅返回已实现的加载器）。
    /// </summary>
    Task<IReadOnlyList<VanillaUpgradeLoaderOption>> GetAvailableLoadersAsync(
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0);

    /// <summary>
    /// 对给定原版实例安装目标加载器并持久化更新实例元数据。
    /// 若实例不是原版则抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    Task<GameInstance> UpgradeAsync(
        GameInstance instance,
        VanillaUpgradeLoaderOption loaderOption,
        IProgress<LauncherProgress>? progress = null,
        DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0);
}

public sealed record VanillaUpgradeLoaderOption(
    LoaderKind Loader,
    string? LoaderVersion,
    string DisplayVersion);
