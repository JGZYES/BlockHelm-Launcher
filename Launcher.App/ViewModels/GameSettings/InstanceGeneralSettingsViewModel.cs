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
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceGeneralSettingsViewModel : GameSettingsDetailsSectionViewModelBase, IDisposable
{
    private static readonly TimeSpan DescriptionSaveDelay = TimeSpan.FromMilliseconds(450);
    private readonly GameSettingsEditDialogViewModel editDialog;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IStatusService statusService;
    private readonly InstanceSettingsPersistenceCoordinator persistence;
    private readonly DownloadTasksPageViewModel downloadTasksPage;
    private readonly IVanillaLoaderUpgradeService? vanillaLoaderUpgradeService;
    private readonly ISettingsService? settingsService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<InstanceGeneralSettingsViewModel> logger;
    private INotifyPropertyChanged? selectedInstanceNotifier;
    private GameSettingsInstanceItem? selectedInstance;
    private bool suppressAutoSave;
    private VanillaUpgradeLoaderOption? selectedLoaderOption;

    [ObservableProperty]
    private string descriptionText = string.Empty;

    [ObservableProperty]
    private bool isVanillaLoaderUpgradeInProgress;

    internal InstanceGeneralSettingsViewModel(
        GameSettingsEditDialogViewModel editDialog,
        IInstanceFolderService instanceFolderService,
        IStatusService statusService,
        InstanceSettingsPersistenceCoordinator persistence,
        DownloadTasksPageViewModel downloadTasksPage,
        IVanillaLoaderUpgradeService? vanillaLoaderUpgradeService,
        ISettingsService? settingsService,
        IFloatingMessageService floatingMessageService,
        IUiDispatcher uiDispatcher,
        ILogger<InstanceGeneralSettingsViewModel>? logger = null)
    {
        this.editDialog = editDialog;
        this.instanceFolderService = instanceFolderService;
        this.statusService = statusService;
        this.persistence = persistence;
        this.downloadTasksPage = downloadTasksPage;
        this.vanillaLoaderUpgradeService = vanillaLoaderUpgradeService;
        this.settingsService = settingsService;
        this.floatingMessageService = floatingMessageService;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger ?? NullLogger<InstanceGeneralSettingsViewModel>.Instance;
    }

    public event Action<GameSettingsInstanceItem>? DeleteInstanceRequested;

    public string InstanceName => selectedInstance?.Name ?? string.Empty;

    public string InstanceIconSource => selectedInstance?.IconSource ?? string.Empty;

    public string InstanceSubtitle => selectedInstance?.Subtitle ?? string.Empty;

    public string InstanceCreatedAtText => selectedInstance?.Instance.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

    public bool IsVanillaInstance => selectedInstance?.Instance.Loader == LoaderKind.Vanilla;

    public bool CanUseVanillaLoaderUpgrade =>
        IsVanillaInstance && vanillaLoaderUpgradeService is not null && !IsVanillaLoaderUpgradeInProgress;

    public string VanillaLoaderUpgradeStatusText
    {
        get
        {
            if (!IsVanillaInstance)
                return Strings.GameSettings_GeneralLoaderUpgradeAlreadyModded;
            if (AvailableLoaderOptions.Count == 0)
                return Strings.GameSettings_GeneralLoaderUpgradeNoVersions;
            if (IsVanillaLoaderUpgradeInProgress)
                return Strings.GameSettings_GeneralLoaderUpgradeInProgress;
            return Strings.GameSettings_GeneralLoaderUpgradeHint;
        }
    }

    public ObservableCollection<VanillaUpgradeLoaderOption> AvailableLoaderOptions { get; } = [];

    public VanillaUpgradeLoaderOption? SelectedLoaderOption
    {
        get => selectedLoaderOption;
        set
        {
            if (SetProperty(ref selectedLoaderOption, value))
                OnPropertyChanged(nameof(CanStartVanillaLoaderUpgrade));
        }
    }

    public bool CanStartVanillaLoaderUpgrade =>
        CanUseVanillaLoaderUpgrade && SelectedLoaderOption is not null;

    public void SetSelectedInstance(GameSettingsInstanceItem? value)
    {
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged -= SelectedInstance_PropertyChanged;

        selectedInstance = value;
        selectedInstanceNotifier = value;
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged += SelectedInstance_PropertyChanged;

        NotifyInstanceDisplayChanged();
        LoadDescriptionFromInstance();
        SelectedLoaderOption = null;
        AvailableLoaderOptions.Clear();
        OnPropertyChanged(nameof(IsVanillaInstance));
        OnPropertyChanged(nameof(CanUseVanillaLoaderUpgrade));
        OnPropertyChanged(nameof(VanillaLoaderUpgradeStatusText));
        OnPropertyChanged(nameof(CanStartVanillaLoaderUpgrade));

        if (vanillaLoaderUpgradeService is not null && value?.Instance.Loader == LoaderKind.Vanilla)
        {
            _ = RefreshAvailableLoaderOptionsAsync(value.Instance);
        }
    }

    public void Dispose()
    {
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged -= SelectedInstance_PropertyChanged;
        selectedInstanceNotifier = null;
    }

    partial void OnDescriptionTextChanged(string value)
    {
        if (suppressAutoSave || selectedInstance is null)
            return;

        var instance = selectedInstance.Instance;
        var normalizedDescription = NormalizeDescription(value);
        persistence.Schedule(
            "description",
            instance,
            target =>
            {
                var originalDescription = target.Description;
                if (string.Equals(originalDescription, normalizedDescription, StringComparison.Ordinal))
                    return null;

                target.Description = normalizedDescription;
                return () => target.Description = originalDescription;
            },
            LoadDescriptionFromInstance,
            DescriptionSaveDelay);
    }

    partial void OnIsVanillaLoaderUpgradeInProgressChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseVanillaLoaderUpgrade));
        OnPropertyChanged(nameof(CanStartVanillaLoaderUpgrade));
        OnPropertyChanged(nameof(VanillaLoaderUpgradeStatusText));
    }

    [RelayCommand]
    private void RequestEditInstance()
    {
        if (selectedInstance is not null)
            editDialog.Open(selectedInstance);
    }

    [RelayCommand]
    private void OpenInstanceDirectory()
    {
        if (selectedInstance is null)
            return;

        var folderPath = selectedInstance.Instance.InstanceDirectory;
        if (!instanceFolderService.DirectoryExists(folderPath))
        {
            statusService.Report(Strings.Status_InstanceFolderNotFound);
            return;
        }

        if (!instanceFolderService.TryOpen(folderPath))
            statusService.Report(Strings.Status_OpenInstanceFolderFailed);
    }

    [RelayCommand]
    private void RequestDeleteInstance()
    {
        if (selectedInstance is not null)
            DeleteInstanceRequested?.Invoke(selectedInstance);
    }

    [RelayCommand(CanExecute = nameof(CanStartVanillaLoaderUpgrade))]
    private async Task StartVanillaLoaderUpgradeAsync(CancellationToken cancellationToken)
    {
        if (!CanStartVanillaLoaderUpgrade
            || selectedInstance is null
            || SelectedLoaderOption is null
            || vanillaLoaderUpgradeService is null)
        {
            return;
        }

        var option = SelectedLoaderOption;
        var instanceItem = selectedInstance;
        var title = string.Format(
            Strings.Status_VanillaLoaderUpgradeRunningFormat,
            instanceItem.Name,
            option.Loader);
        var subtitle = option.DisplayVersion;

        IsVanillaLoaderUpgradeInProgress = true;
        OnPropertyChanged(nameof(VanillaLoaderUpgradeStatusText));
        var task = downloadTasksPage.BeginTask(title, subtitle);
        statusService.Report(title);

        try
        {
            var settings = settingsService is null ? new LauncherSettings() : await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var progress = task.CreateProgress(p => { });
            var updatedInstance = await vanillaLoaderUpgradeService.UpgradeAsync(
                instanceItem.Instance,
                option,
                progress,
                settings.DownloadSourcePreference,
                cancellationToken,
                settings.DownloadSpeedLimitMbPerSecond).ConfigureAwait(false);

            task.State = DownloadTaskState.Completed;
            instanceItem.Update(updatedInstance, updatedInstance.VersionType);
            logger.LogInformation(
                "Vanilla loader upgrade succeeded. Instance={Instance} Loader={Loader} VersionName={VersionName}",
                updatedInstance.Id,
                updatedInstance.Loader,
                updatedInstance.VersionName);
            floatingMessageService.Show(string.Format(Strings.GameSettings_GeneralLoaderUpgradeSucceededFormat, option.Loader));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || task.IsCancellationRequested)
        {
            task.State = DownloadTaskState.Failed;
            floatingMessageService.Show(Strings.DownloadTask_Failed);
        }
        catch (Exception exception)
        {
            task.State = DownloadTaskState.Failed;
            logger.LogError(exception, "Vanilla loader upgrade failed. Instance={Instance}", instanceItem.Instance.Id);
            floatingMessageService.Show(
                string.Format(Strings.GameSettings_GeneralLoaderUpgradeFailedFormat, exception.Message));
        }
        finally
        {
            IsVanillaLoaderUpgradeInProgress = false;
        }
    }

    private async Task RefreshAvailableLoaderOptionsAsync(GameInstance instance)
    {
        if (vanillaLoaderUpgradeService is null)
            return;

        try
        {
            var settings = settingsService is null ? new LauncherSettings() : await settingsService.LoadAsync().ConfigureAwait(false);
            var options = await vanillaLoaderUpgradeService.GetAvailableLoadersAsync(
                instance.MinecraftVersion,
                settings.DownloadSourcePreference,
                default,
                settings.DownloadSpeedLimitMbPerSecond).ConfigureAwait(false);

            uiDispatcher.Invoke(() =>
            {
                AvailableLoaderOptions.Clear();
                foreach (var option in options)
                    AvailableLoaderOptions.Add(option);

                SelectedLoaderOption = options.Count == 0 ? null : options[0];
                OnPropertyChanged(nameof(VanillaLoaderUpgradeStatusText));
                OnPropertyChanged(nameof(CanStartVanillaLoaderUpgrade));
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to resolve vanilla upgrade loader options. MinecraftVersion={MinecraftVersion}",
                instance.MinecraftVersion);
            uiDispatcher.Invoke(() =>
            {
                AvailableLoaderOptions.Clear();
                OnPropertyChanged(nameof(VanillaLoaderUpgradeStatusText));
            });
        }
    }

    private void SelectedInstance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyInstanceDisplayChanged();
    }

    private void NotifyInstanceDisplayChanged()
    {
        OnPropertyChanged(nameof(InstanceName));
        OnPropertyChanged(nameof(InstanceIconSource));
        OnPropertyChanged(nameof(InstanceSubtitle));
        OnPropertyChanged(nameof(InstanceCreatedAtText));
    }

    private void LoadDescriptionFromInstance()
    {
        suppressAutoSave = true;
        try
        {
            DescriptionText = selectedInstance?.Instance.Description ?? string.Empty;
        }
        finally
        {
            suppressAutoSave = false;
        }
    }

    private static string NormalizeDescription(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
