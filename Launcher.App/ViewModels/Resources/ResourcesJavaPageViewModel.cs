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

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

/// <summary>
/// 资源中心 JDK 下载分区 ViewModel
/// </summary>
public sealed class ResourcesJavaPageViewModel : ResourcesSectionViewModelBase
{
    public ResourcesJavaPageViewModel(
        ResourcesPageViewModel parent,
        IJavaDownloadService? javaDownloadService = null,
        IFloatingMessageService? floatingMessageService = null,
        DownloadTasksPageViewModel? downloadTasksPage = null)
        : base(parent, Strings.Resources_SectionJava)
    {
        JavaDownload = new JavaDownloadPanelViewModel(
            javaDownloadService ?? NullJavaDownloadService.Instance,
            floatingMessageService ?? NullFloatingMessageService.Instance,
            downloadTasksPage);
    }

    public JavaDownloadPanelViewModel JavaDownload { get; }

    private sealed class NullJavaDownloadService : IJavaDownloadService
    {
        public static NullJavaDownloadService Instance { get; } = new();
        public event EventHandler<JavaDownloadProgressEventArgs>? DownloadProgressChanged { add { } remove { } }

        public Task<IReadOnlyList<JavaDistributionInfo>> GetAvailableDistributionsAsync(
            string? version = null, string? vendor = null, string? architecture = null, string? platform = null,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<JavaDistributionInfo>>([]);

        public Task<JavaInstallResult> DownloadAndInstallAsync(
            JavaDistributionInfo distribution, IProgress<(string Status, double Progress)>? progress = null,
            CancellationToken cancellationToken = default) => Task.FromResult(new JavaInstallResult { IsSuccess = false, ErrorMessage = "服务未启用" });

        public Task<string?> DownloadAsync(
            JavaDistributionInfo distribution, string targetDirectory,
            IProgress<(string Status, double Progress)>? progress = null,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> GetManagedInstallsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> UninstallAsync(string installPath, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<IReadOnlyList<JavaDistributionInfo>> CheckForUpdatesAsync(
            string vendor, string currentVersion, string architecture, string platform,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<JavaDistributionInfo>>([]);
    }

    private sealed class NullFloatingMessageService : IFloatingMessageService
    {
        public static NullFloatingMessageService Instance { get; } = new();
        public event Action<string>? MessageRequested { add { } remove { } }
        public void Show(string message) { }
    }
}
