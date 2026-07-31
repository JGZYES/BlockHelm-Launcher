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

using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

/// <summary>
/// JDK 下载面板 ViewModel - 两步式导航（厂商列表 → 版本列表）
/// 下载任务统一纳入 DownloadTasksPageViewModel 管理
/// </summary>
public sealed partial class JavaDownloadPanelViewModel : ObservableObject
{
    private readonly IJavaDownloadService? javaDownloadService;
    private readonly IFloatingMessageService? floatingMessageService;
    private readonly DownloadTasksPageViewModel? downloadTasksPage;

    public ObservableCollection<JavaVendorOption> VendorOptions { get; } = [];

    public ObservableCollection<JavaVersionListItem> AvailableVersions { get; } = [];

    [ObservableProperty]
    private JavaVendorOption? selectedVendor;

    [ObservableProperty]
    private JavaVersionListItem? selectedVersion;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasLoadedDistributions;

    [ObservableProperty]
    private bool isVendorStep = true;

    [ObservableProperty]
    private bool isVersionStep;

    [ObservableProperty]
    private string statusMessage = "选择厂商后进入版本选择";

    public JavaDownloadPanelViewModel()
    {
        VendorOptions.Add(new JavaVendorOption(
            JavaVendorNames.Mojang,
            "Mojang 官方 JDK",
            "Minecraft 官方随附的 Java 运行时",
            icon: "\uE950",
            iconSource: null));

        VendorOptions.Add(new JavaVendorOption(
            JavaVendorNames.EclipseTemurin,
            "Eclipse Temurin",
            "Adoptium 社区维护的 OpenJDK 发行版",
            icon: "\uE950",
            iconSource: null));

        VendorOptions.Add(new JavaVendorOption(
            JavaVendorNames.MicrosoftBuild,
            "Microsoft Build of OpenJDK",
            "微软构建的 OpenJDK 发行版",
            icon: "\uE950",
            iconSource: null));
    }

    public JavaDownloadPanelViewModel(
        IJavaDownloadService javaDownloadService,
        IFloatingMessageService floatingMessageService,
        DownloadTasksPageViewModel? downloadTasksPage = null) : this()
    {
        this.javaDownloadService = javaDownloadService;
        this.floatingMessageService = floatingMessageService;
        this.downloadTasksPage = downloadTasksPage;
    }

    partial void OnSelectedVendorChanged(JavaVendorOption? value)
    {
        foreach (var vendor in VendorOptions)
            vendor.IsSelected = ReferenceEquals(vendor, value);

        if (value is not null)
        {
            IsVendorStep = false;
            IsVersionStep = true;
            _ = LoadVersionsAsync();
        }
    }

    partial void OnSelectedVersionChanged(JavaVersionListItem? value)
    {
        foreach (var v in AvailableVersions)
            v.IsSelected = ReferenceEquals(v, value);
    }

    [RelayCommand]
    private void BackToVendor()
    {
        IsVendorStep = true;
        IsVersionStep = false;
        SelectedVendor = null;
        SelectedVersion = null;
        AvailableVersions.Clear();
        HasLoadedDistributions = false;
        StatusMessage = "选择厂商后进入版本选择";
    }

    private async Task LoadVersionsAsync()
    {
        if (SelectedVendor is null || javaDownloadService is null)
        {
            AvailableVersions.Clear();
            HasLoadedDistributions = false;
            return;
        }

        IsLoading = true;
        AvailableVersions.Clear();
        StatusMessage = $"正在获取 {SelectedVendor.Title} 可用版本...";

        try
        {
            var distributions = await javaDownloadService.GetAvailableDistributionsAsync(
                vendor: SelectedVendor.Id);

            foreach (var dist in distributions.OrderByDescending(d => d.Version, StringComparer.Ordinal))
            {
                AvailableVersions.Add(new JavaVersionListItem(
                    dist.Version,
                    dist.Name,
                    GetArchitectureLabel(dist.Architecture, dist.Platform),
                    dist.DownloadUrl,
                    dist));
            }

            HasLoadedDistributions = AvailableVersions.Count > 0;
            SelectedVersion = AvailableVersions.FirstOrDefault();
            StatusMessage = AvailableVersions.Count > 0
                ? $"可用版本：{AvailableVersions.Count} 个"
                : "未找到可用版本";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载版本列表失败: {ex.Message}";
            HasLoadedDistributions = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string GetArchitectureLabel(string arch, string platform)
    {
        var archLabel = arch switch
        {
            JavaArchitectures.X64 => "x64",
            JavaArchitectures.X86 => "x86",
            JavaArchitectures.Arm64 => "ARM64",
            _ => arch
        };
        var platLabel = platform switch
        {
            JavaPlatforms.Windows => "Windows",
            JavaPlatforms.Linux => "Linux",
            JavaPlatforms.MacOS => "macOS",
            _ => platform
        };
        return $"{platLabel} · {archLabel}";
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (SelectedVersion is null || javaDownloadService is null)
            return;

        var version = SelectedVersion;
        var taskItem = downloadTasksPage?.BeginTask(
            version.Title,
            version.Subtitle);

        floatingMessageService?.Show($"开始下载 {version.Title}");

        // 下载速度跟踪状态
        long lastBytes = 0;
        var lastTime = DateTime.MinValue;
        var speedLock = new object();

        // 订阅下载进度事件，计算并报告下载速度
        void OnDownloadProgressChanged(object? sender, JavaDownloadProgressEventArgs e)
        {
            if (taskItem is null) return;

            long bytesPerSecond = 0;
            lock (speedLock)
            {
                var now = DateTime.UtcNow;
                if (lastTime != DateTime.MinValue)
                {
                    var diff = now - lastTime;
                    if (diff.TotalSeconds > 0.1)
                    {
                        var bytesDiff = e.BytesDownloaded - lastBytes;
                        bytesPerSecond = (long)(bytesDiff / diff.TotalSeconds);
                    }
                }
                lastBytes = e.BytesDownloaded;
                lastTime = now;
            }

            if (bytesPerSecond > 0)
            {
                taskItem.Report(new LauncherProgress(
                    "JavaDownload",
                    e.Status,
                    e.Progress,
                    new DownloadSpeedTelemetry(bytesPerSecond)));
            }
        }

        javaDownloadService.DownloadProgressChanged += OnDownloadProgressChanged;

        IProgress<(string Status, double Progress)>? progress = null;
        if (taskItem is not null)
        {
            taskItem.Report(new LauncherProgress(
                "JavaDownload",
                "准备下载...",
                0));
            progress = new Progress<(string Status, double Progress)>(p =>
            {
                taskItem.Report(new LauncherProgress(
                    "JavaDownload",
                    p.Status,
                    p.Progress));
            });
        }

        var operation = DownloadAndInstallCoreAsync(
            version,
            javaDownloadService,
            floatingMessageService,
            taskItem,
            progress);

        // 下载完成后取消订阅
        _ = operation.ContinueWith(_ =>
        {
            javaDownloadService.DownloadProgressChanged -= OnDownloadProgressChanged;
        }, TaskScheduler.Default);

        downloadTasksPage?.TrackBackgroundTask(operation);
    }

    private static async Task DownloadAndInstallCoreAsync(
        JavaVersionListItem version,
        IJavaDownloadService javaDownloadService,
        IFloatingMessageService? floatingMessageService,
        DownloadTaskItem? taskItem,
        IProgress<(string Status, double Progress)>? progress)
    {
        try
        {
            var result = await javaDownloadService.DownloadAndInstallAsync(
                version.Distribution,
                progress);

            if (result.IsSuccess)
            {
                taskItem?.Complete($"{version.Title} 安装完成");
                floatingMessageService?.Show($"{version.Title} 安装成功");
            }
            else
            {
                taskItem?.Fail(result.ErrorMessage ?? "安装失败");
                floatingMessageService?.Show($"安装失败: {result.ErrorMessage}");
            }
        }
        catch (OperationCanceledException) when (taskItem?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            taskItem?.Fail(ex.Message);
            floatingMessageService?.Show($"下载出错: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshVersionsAsync()
    {
        await LoadVersionsAsync();
    }
}

public sealed partial class JavaVendorOption : ObservableObject
{
    public JavaVendorOption(string id, string title, string subtitle, string icon, string? iconSource = null)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
        IconSource = iconSource;
    }

    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Icon { get; }
    public string? IconSource { get; }

    [ObservableProperty]
    private bool isSelected;
}

public sealed partial class JavaVersionListItem : ObservableObject
{
    public JavaVersionListItem(string version, string title, string subtitle, string downloadUrl, JavaDistributionInfo distribution)
    {
        Version = version;
        Title = title;
        Subtitle = subtitle;
        DownloadUrl = downloadUrl;
        Distribution = distribution;
    }

    public string Version { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string DownloadUrl { get; }
    public JavaDistributionInfo Distribution { get; }

    [ObservableProperty]
    private bool isSelected;
}
