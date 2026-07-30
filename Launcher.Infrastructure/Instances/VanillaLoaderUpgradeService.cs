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

using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Instances;

internal sealed class VanillaLoaderUpgradeService : IVanillaLoaderUpgradeService
{
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> loaderProviders;
    private readonly IGameInstanceRepository repository;
    private readonly ISettingsService settingsService;
    private readonly ILogger<VanillaLoaderUpgradeService> logger;

    public VanillaLoaderUpgradeService(
        IEnumerable<ILoaderProvider> loaderProviders,
        IGameInstanceRepository repository,
        ISettingsService settingsService,
        ILogger<VanillaLoaderUpgradeService>? logger = null)
    {
        this.loaderProviders = loaderProviders
            .Where(provider => provider.IsImplemented)
            .ToDictionary(provider => provider.Kind);
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.logger = logger ?? NullLogger<VanillaLoaderUpgradeService>.Instance;
    }

    public async Task<IReadOnlyList<VanillaUpgradeLoaderOption>> GetAvailableLoadersAsync(
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return Array.Empty<VanillaUpgradeLoaderOption>();

        var loaders = new List<VanillaUpgradeLoaderOption>();
        var candidates = new[]
        {
            LoaderKind.Fabric,
            LoaderKind.NeoForge,
            LoaderKind.Forge,
            LoaderKind.Quilt
        };

        foreach (var loader in candidates)
        {
            if (!loaderProviders.TryGetValue(loader, out var provider))
                continue;

            IReadOnlyList<LoaderVersionInfo> versions;
            try
            {
                versions = await provider.GetLoaderVersionsAsync(
                    minecraftVersion,
                    downloadSourcePreference,
                    cancellationToken,
                    downloadSpeedLimitMbPerSecond).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "Vanilla upgrade loader versions fetch skipped. Loader={Loader} MinecraftVersion={MinecraftVersion}",
                    loader,
                    minecraftVersion);
                continue;
            }

            if (versions.Count == 0)
                continue;

            var selectedVersion = versions.FirstOrDefault(v => v.IsStable) ?? versions[0];
            loaders.Add(new VanillaUpgradeLoaderOption(
                loader,
                selectedVersion.Version,
                selectedVersion.Version));
        }

        return loaders;
    }

    public async Task<GameInstance> UpgradeAsync(
        GameInstance instance,
        VanillaUpgradeLoaderOption loaderOption,
        IProgress<LauncherProgress>? progress = null,
        DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(loaderOption);
        if (instance.Loader != LoaderKind.Vanilla
            && instance.Loader != LoaderKind.Fabric
            && instance.Loader != LoaderKind.Forge
            && instance.Loader != LoaderKind.NeoForge
            && instance.Loader != LoaderKind.Quilt)
        {
            throw new InvalidOperationException(
                $"Loader upgrade is not supported for loader '{instance.Loader}'.");
        }
        if (!loaderProviders.TryGetValue(loaderOption.Loader, out var provider))
        {
            throw new NotSupportedException($"Loader {loaderOption.Loader} is not implemented.");
        }

        logger.LogInformation(
            "Starting vanilla loader upgrade. Instance={Instance} MinecraftVersion={MinecraftVersion} TargetLoader={Loader} TargetLoaderVersion={LoaderVersion}",
            instance.Id,
            instance.MinecraftVersion,
            loaderOption.Loader,
            loaderOption.LoaderVersion);

        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var gameDirectory = settings.MinecraftDirectory;
        var newVersionName = instance.MinecraftVersion + "-" + loaderOption.Loader
            + (string.IsNullOrWhiteSpace(loaderOption.LoaderVersion)
                ? string.Empty
                : "-" + loaderOption.LoaderVersion);

        var installedVersionName = await provider.InstallAsync(
            minecraftVersion: instance.MinecraftVersion,
            gameDirectory: gameDirectory,
            isolatedVersionName: newVersionName,
            loaderVersion: loaderOption.LoaderVersion,
            progress: progress,
            downloadSourcePreference: downloadSourcePreference,
            cancellationToken: cancellationToken,
            downloadSpeedLimitMbPerSecond: downloadSpeedLimitMbPerSecond).ConfigureAwait(false);

        var updated = new GameInstance
        {
            Id = instance.Id,
            Name = instance.Name,
            MinecraftVersion = instance.MinecraftVersion,
            Loader = loaderOption.Loader,
            LoaderVersion = loaderOption.LoaderVersion,
            VersionName = installedVersionName,
            VersionType = loaderOption.Loader.ToString(),
            Description = instance.Description,
            IconSource = instance.IconSource,
            InstanceDirectory = instance.InstanceDirectory,
            BackupDirectory = instance.BackupDirectory,
            MemorySettingsMode = instance.MemorySettingsMode,
            MemoryMb = instance.MemoryMb,
            WindowWidth = instance.WindowWidth,
            WindowHeight = instance.WindowHeight,
            PreLaunchCommand = instance.PreLaunchCommand,
            WaitForPreLaunchCommand = instance.WaitForPreLaunchCommand,
            PostExitCommand = instance.PostExitCommand,
            JvmArguments = instance.JvmArguments,
            GameArguments = instance.GameArguments,
            LaunchSettingsMode = instance.LaunchSettingsMode,
            JavaSettingsMode = instance.JavaSettingsMode,
            JavaSelectionMode = instance.JavaSelectionMode,
            SelectedJavaExecutablePath = instance.SelectedJavaExecutablePath,
            CheckFilesBeforeLaunch = instance.CheckFilesBeforeLaunch,
            AutoRepairMissingFiles = instance.AutoRepairMissingFiles,
            MinimizeLauncherAfterLaunch = instance.MinimizeLauncherAfterLaunch,
            LaunchFullScreen = instance.LaunchFullScreen,
            AutoJoinServerAddress = instance.AutoJoinServerAddress,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await repository.UpdateInstanceAsync(gameDirectory, updated, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Vanilla loader upgrade completed. Instance={Instance} NewLoader={Loader} NewVersionName={VersionName}",
            instance.Id,
            updated.Loader,
            updated.VersionName);
        return updated;
    }
}
