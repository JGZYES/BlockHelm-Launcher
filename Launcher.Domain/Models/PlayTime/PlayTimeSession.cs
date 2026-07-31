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

namespace Launcher.Domain.Models;

public sealed class PlayTimeSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string InstanceId { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string Loader { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public TimeSpan Duration => EndedAt.HasValue
        ? EndedAt.Value - StartedAt
        : DateTimeOffset.UtcNow - StartedAt;
    public bool IsActive => !EndedAt.HasValue;
    public string EndReason { get; set; } = string.Empty;
}

public sealed class PlayTimeSummary
{
    public string InstanceId { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public TimeSpan TotalPlayTime { get; set; }
    public int SessionCount { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public TimeSpan TodayPlayTime { get; set; }
    public TimeSpan ThisWeekPlayTime { get; set; }
    public IReadOnlyList<PlayTimeSession> RecentSessions { get; set; } = [];
}
