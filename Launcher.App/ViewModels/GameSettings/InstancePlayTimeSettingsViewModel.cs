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
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstancePlayTimeSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    private readonly IPlayTimeTracker playTimeTracker;
    private readonly ILogger<InstancePlayTimeSettingsViewModel> logger;
    private GameInstance? currentInstance;

    [ObservableProperty]
    private string totalPlayTimeText = string.Empty;

    [ObservableProperty]
    private string todayPlayTimeText = string.Empty;

    [ObservableProperty]
    private string weekPlayTimeText = string.Empty;

    [ObservableProperty]
    private int sessionCount;

    [ObservableProperty]
    private string lastPlayedText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PlayTimeSessionItemViewModel> recentSessions = [];

    [ObservableProperty]
    private bool hasPlayTimeData;

    public InstancePlayTimeSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        IPlayTimeTracker playTimeTracker,
        ILogger<InstancePlayTimeSettingsViewModel>? logger = null) : base(parent)
    {
        this.playTimeTracker = playTimeTracker;
        this.logger = logger ?? NullLogger<InstancePlayTimeSettingsViewModel>.Instance;
    }

    public override void OnSelectedInstanceChanged(GameInstance? instance)
    {
        currentInstance = instance;
        RefreshPlayTimeData();
    }

    public override Task OnSectionActivatedAsync()
    {
        RefreshPlayTimeData();
        return Task.CompletedTask;
    }

    private void RefreshPlayTimeData()
    {
        if (currentInstance is null)
        {
            HasPlayTimeData = false;
            TotalPlayTimeText = string.Empty;
            TodayPlayTimeText = string.Empty;
            WeekPlayTimeText = string.Empty;
            SessionCount = 0;
            LastPlayedText = string.Empty;
            RecentSessions.Clear();
            return;
        }

        var summary = playTimeTracker.GetSummary(currentInstance.Id);
        HasPlayTimeData = summary.SessionCount > 0;
        TotalPlayTimeText = FormatDuration(summary.TotalPlayTime);
        TodayPlayTimeText = FormatDuration(summary.TodayPlayTime);
        WeekPlayTimeText = FormatDuration(summary.ThisWeekPlayTime);
        SessionCount = summary.SessionCount;
        LastPlayedText = summary.LastPlayedAt.HasValue
            ? summary.LastPlayedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : Strings.PlayTime_NoHistory;

        RecentSessions.Clear();
        foreach (var session in summary.RecentSessions)
        {
            RecentSessions.Add(new PlayTimeSessionItemViewModel(session));
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
            return Strings.PlayTime_LessThanOneMinute;

        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;

        if (hours == 0)
            return string.Format(Strings.PlayTime_MinutesFormat, minutes);

        return string.Format(Strings.PlayTime_HoursMinutesFormat, hours, minutes);
    }
}

public sealed class PlayTimeSessionItemViewModel
{
    public PlayTimeSessionItemViewModel(PlayTimeSession session)
    {
        Session = session;
        DurationText = FormatDuration(session.Duration);
        StartedAtText = session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        IsActive = session.IsActive;
    }

    public PlayTimeSession Session { get; }
    public string DurationText { get; }
    public string StartedAtText { get; }
    public bool IsActive { get; }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
            return Strings.PlayTime_LessThanOneMinute;

        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;
        return hours == 0
            ? string.Format(Strings.PlayTime_MinutesFormat, minutes)
            : string.Format(Strings.PlayTime_HoursMinutesFormat, hours, minutes);
    }
}
