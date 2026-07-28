/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Resources;
using Launcher.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class RemoteThumbnailDownloadClientTests : TestTempDirectory
{
    [Fact]
    public async Task LocalModEnrichmentStartsThumbnailDownloadsConcurrently()
    {
        Directory.CreateDirectory(TempRoot);
        var mods = Enumerable.Range(0, 2)
            .Select(index =>
            {
                var path = Path.Combine(TempRoot, $"mod-{index}.jar");
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes($"mod-content-{index}"));
                return new LocalMod
                {
                    Name = $"Mod {index}",
                    FileName = Path.GetFileName(path),
                    FullPath = path,
                    IsEnabled = true
                };
            })
            .ToArray();
        var hashes = mods
            .Select(mod => Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(mod.FullPath))).ToLowerInvariant())
            .ToArray();
        var handler = new LocalModIconHandler(hashes);
        using var httpClient = new HttpClient(handler);
        var service = new LocalModIconEnrichmentService(
            new LauncherPathProvider(TempRoot),
            httpClient,
            logger: NullLogger<LocalModIconEnrichmentService>.Instance);

        var enrichment = service.ResolveMissingIconSourcesAsync(mods);
        await handler.AllIconsStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, handler.MaximumActiveIconRequests);

        handler.ReleaseIcons();
        var resolved = await enrichment.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, resolved.Count);
        Assert.All(mods, mod => Assert.True(resolved.ContainsKey(mod.FullPath)));
    }

    private sealed class LocalModIconHandler(IReadOnlyList<string> hashes) : HttpMessageHandler
    {
        private static readonly byte[] IconPayload = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        private readonly TaskCompletionSource allIconsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseIcons = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeIconRequests;
        private int maximumActiveIconRequests;

        public Task AllIconsStarted => allIconsStarted.Task;
        public int MaximumActiveIconRequests => Volatile.Read(ref maximumActiveIconRequests);

        public void ReleaseIcons() => releaseIcons.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.EndsWith("/version_files", StringComparison.OrdinalIgnoreCase))
            {
                var versions = hashes
                    .Select((hash, index) => new KeyValuePair<string, object>(
                        hash,
                        new Dictionary<string, string> { ["project_id"] = $"project-{index}" }))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                return JsonResponse(versions);
            }

            if (uri.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.EndsWith("/projects", StringComparison.OrdinalIgnoreCase))
            {
                var projects = hashes.Select((_, index) => new Dictionary<string, string>
                {
                    ["id"] = $"project-{index}",
                    ["icon_url"] = $"https://cdn.example.com/icons/{index}.png"
                });
                return JsonResponse(projects);
            }

            if (uri.Host.Equals("cdn.example.com", StringComparison.OrdinalIgnoreCase))
            {
                var active = Interlocked.Increment(ref activeIconRequests);
                UpdateMaximum(ref maximumActiveIconRequests, active);
                if (active == hashes.Count)
                    allIconsStarted.TrySetResult();
                try
                {
                    await releaseIcons.Task.WaitAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(IconPayload)
                    };
                }
                finally
                {
                    Interlocked.Decrement(ref activeIconRequests);
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(value),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }
}
