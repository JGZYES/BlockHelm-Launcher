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
/// Java 下载与安装服务
/// </summary>
public interface IJavaDownloadService
{
    /// <summary>
    /// 获取可用的 Java 发行版列表
    /// </summary>
    Task<IReadOnlyList<JavaDistributionInfo>> GetAvailableDistributionsAsync(
        string? version = null,
        string? vendor = null,
        string? architecture = null,
        string? platform = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载并安装 Java 发行版
    /// </summary>
    Task<JavaInstallResult> DownloadAndInstallAsync(
        JavaDistributionInfo distribution,
        IProgress<(string Status, double Progress)>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载 Java 发行版到指定目录（仅下载不安装）
    /// </summary>
    Task<string?> DownloadAsync(
        JavaDistributionInfo distribution,
        string targetDirectory,
        IProgress<(string Status, double Progress)>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已安装的 Java 列表（启动器管理的）
    /// </summary>
    Task<IReadOnlyList<string>> GetManagedInstallsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 卸载指定的 Java 安装
    /// </summary>
    Task<bool> UninstallAsync(string installPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查 Java 发行版的最新版本
    /// </summary>
    Task<IReadOnlyList<JavaDistributionInfo>> CheckForUpdatesAsync(
        string vendor,
        string currentVersion,
        string architecture,
        string platform,
        CancellationToken cancellationToken = default);

    event EventHandler<JavaDownloadProgressEventArgs>? DownloadProgressChanged;
}