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

public interface IPlayTimeTracker
{
    PlayTimeSession StartSession(GameInstance instance);

    Task EndSessionAsync(string sessionId, string endReason = "exited");

    PlayTimeSummary GetSummary(string instanceId);

    IReadOnlyList<PlayTimeSession> GetRecentSessions(string instanceId, int limit = 20);

    IReadOnlyList<PlayTimeSummary> GetAllSummaries();

    TimeSpan GetTodayPlayTime(string instanceId);

    event EventHandler<PlayTimeSessionEventArgs>? SessionStarted;

    event EventHandler<PlayTimeSessionEventArgs>? SessionEnded;
}

public sealed class PlayTimeSessionEventArgs : EventArgs
{
    public PlayTimeSessionEventArgs(PlayTimeSession session)
    {
        Session = session;
    }

    public PlayTimeSession Session { get; }
}
