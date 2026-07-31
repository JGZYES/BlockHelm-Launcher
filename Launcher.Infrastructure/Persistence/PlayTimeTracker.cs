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
using System.IO;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Persistence;

public sealed class PlayTimeTracker : IPlayTimeTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string dataDirectory;
    private readonly string sessionsPath;
    private readonly ILogger<PlayTimeTracker> logger;
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private readonly List<PlayTimeSession> sessions = [];
    private readonly Dictionary<string, PlayTimeSession> activeSessions = new();

    public PlayTimeTracker(LauncherPathProvider pathProvider, ILogger<PlayTimeTracker>? logger = null)
    {
        dataDirectory = Path.Combine(pathProvider.DefaultDataDirectory, "playtime");
        sessionsPath = Path.Combine(dataDirectory, "sessions.json");
        this.logger = logger ?? NullLogger<PlayTimeTracker>.Instance;
        Directory.CreateDirectory(dataDirectory);
        LoadSessions();
    }

    public event EventHandler<PlayTimeSessionEventArgs>? SessionStarted;
    public event EventHandler<PlayTimeSessionEventArgs>? SessionEnded;

    public PlayTimeSession StartSession(GameInstance instance)
    {
        var session = new PlayTimeSession
        {
            InstanceId = instance.Id,
            InstanceName = instance.Name,
            Loader = instance.Loader.ToString(),
            MinecraftVersion = instance.MinecraftVersion,
            StartedAt = DateTimeOffset.UtcNow
        };

        ioLock.Wait();
        try
        {
            sessions.Add(session);
            activeSessions[session.Id] = session;
            SaveSessionsCore();
        }
        finally
        {
            ioLock.Release();
        }

        SessionStarted?.Invoke(this, new PlayTimeSessionEventArgs(session));
        logger.LogInformation(
            "Play time session started. InstanceId={InstanceId} InstanceName={InstanceName} SessionId={SessionId}",
            instance.Id,
            instance.Name,
            session.Id);
        return session;
    }

    public async Task EndSessionAsync(string sessionId, string endReason = "exited")
    {
        PlayTimeSession? session;
        ioLock.Wait();
        try
        {
            session = activeSessions.GetValueOrDefault(sessionId);
            if (session is null)
                return;

            session.EndedAt = DateTimeOffset.UtcNow;
            session.EndReason = endReason;
            activeSessions.Remove(sessionId);
            SaveSessionsCore();
        }
        finally
        {
            ioLock.Release();
        }

        if (session is not null)
        {
            SessionEnded?.Invoke(this, new PlayTimeSessionEventArgs(session));
            logger.LogInformation(
                "Play time session ended. SessionId={SessionId} DurationMinutes={DurationMinutes} Reason={Reason}",
                sessionId,
                session.Duration.TotalMinutes,
                endReason);
        }

        await Task.CompletedTask;
    }

    public PlayTimeSummary GetSummary(string instanceId)
    {
        ioLock.Wait();
        try
        {
            var instanceSessions = sessions
                .Where(s => string.Equals(s.InstanceId, instanceId, StringComparison.Ordinal))
                .ToList();
            var now = DateTimeOffset.UtcNow;
            var todayStart = now.Date;
            var weekStart = todayStart.AddDays(-(int)now.DayOfWeek);

            var completedSessions = instanceSessions.Where(s => s.EndedAt.HasValue).ToList();
            var totalPlayTime = completedSessions.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
            var todayPlayTime = completedSessions
                .Where(s => s.StartedAt >= todayStart)
                .Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
            var weekPlayTime = completedSessions
                .Where(s => s.StartedAt >= weekStart)
                .Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
            var lastPlayedAt = completedSessions.Count > 0
                ? completedSessions.Max(s => s.EndedAt)
                : null;
            var recentSessions = completedSessions
                .OrderByDescending(s => s.StartedAt)
                .Take(20)
                .ToList();

            var instanceName = instanceSessions.FirstOrDefault()?.InstanceName ?? string.Empty;

            return new PlayTimeSummary
            {
                InstanceId = instanceId,
                InstanceName = instanceName,
                TotalPlayTime = totalPlayTime,
                SessionCount = completedSessions.Count,
                LastPlayedAt = lastPlayedAt,
                TodayPlayTime = todayPlayTime,
                ThisWeekPlayTime = weekPlayTime,
                RecentSessions = recentSessions
            };
        }
        finally
        {
            ioLock.Release();
        }
    }

    public IReadOnlyList<PlayTimeSession> GetRecentSessions(string instanceId, int limit = 20)
    {
        ioLock.Wait();
        try
        {
            return sessions
                .Where(s => string.Equals(s.InstanceId, instanceId, StringComparison.Ordinal))
                .OrderByDescending(s => s.StartedAt)
                .Take(limit)
                .ToList();
        }
        finally
        {
            ioLock.Release();
        }
    }

    public IReadOnlyList<PlayTimeSummary> GetAllSummaries()
    {
        ioLock.Wait();
        try
        {
            var instanceIds = sessions
                .Select(s => s.InstanceId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return instanceIds.Select(GetSummary).ToList();
        }
        finally
        {
            ioLock.Release();
        }
    }

    public TimeSpan GetTodayPlayTime(string instanceId)
    {
        var summary = GetSummary(instanceId);
        return summary.TodayPlayTime;
    }

    private void LoadSessions()
    {
        try
        {
            if (!File.Exists(sessionsPath))
                return;

            using var stream = File.OpenRead(sessionsPath);
            var loaded = JsonSerializer.Deserialize<List<PlayTimeSession>>(stream, JsonOptions);
            if (loaded is not null)
            {
                sessions.AddRange(loaded);
                // Recover active sessions (launcher was closed without tracking game exit)
                foreach (var session in sessions.Where(s => !s.EndedAt.HasValue))
                {
                    session.EndedAt = DateTimeOffset.UtcNow;
                    session.EndReason = "launcher_closed";
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Failed to load play time sessions from {SessionsPath}", sessionsPath);
        }
    }

    private void SaveSessionsCore()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            // Trim sessions older than 90 days
            var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
            var trimmed = sessions.Where(s => s.StartedAt >= cutoff).ToList();

            using var stream = new FileStream(sessionsPath, FileMode.Create, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, trimmed, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Failed to save play time sessions to {SessionsPath}", sessionsPath);
        }
    }
}
