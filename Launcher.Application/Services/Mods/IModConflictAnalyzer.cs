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
/// Mod 冲突分析服务接口
/// </summary>
public interface IModConflictAnalyzer
{
    /// <summary>
    /// 分析实例中所有 Mod 的冲突
    /// </summary>
    Task<ModConflictReport> AnalyzeInstanceConflictsAsync(
        GameInstance instance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 分析指定 Mod 列表的冲突
    /// </summary>
    Task<ModConflictReport> AnalyzeModConflictsAsync(
        IReadOnlyList<LocalMod> mods,
        string minecraftVersion,
        string loaderType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查单个 Mod 是否与现有 Mod 兼容
    /// </summary>
    Task<ModCompatibilityCheckResult> CheckModCompatibilityAsync(
        LocalMod newMod,
        IReadOnlyList<LocalMod> existingMods,
        string minecraftVersion,
        string loaderType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 Mod 依赖关系图
    /// </summary>
    Task<IReadOnlyList<ModDependencyGraphNode>> BuildDependencyGraphAsync(
        IReadOnlyList<LocalMod> mods,
        CancellationToken cancellationToken = default);
}
