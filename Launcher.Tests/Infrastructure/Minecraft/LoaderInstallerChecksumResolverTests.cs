/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LoaderInstallerChecksumResolverTests
{
    private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";

    [Theory]
    [InlineData(Sha1, Sha1)]
    [InlineData("  0123456789ABCDEF0123456789ABCDEF01234567\r\n", "0123456789ABCDEF0123456789ABCDEF01234567")]
    [InlineData(Sha1 + "  installer.jar", Sha1)]
    [InlineData(Sha1 + "\tinstaller.jar", Sha1)]
    public void NormalizeSha1AcceptsSupportedMavenFormats(string value, string expected)
    {
        Assert.Equal(expected, LoaderInstallerChecksumResolver.NormalizeSha1(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789abcdef")]
    [InlineData("g123456789abcdef0123456789abcdef01234567")]
    public void NormalizeSha1RejectsMissingOrMalformedValues(string? value)
    {
        Assert.Null(LoaderInstallerChecksumResolver.NormalizeSha1(value));
    }

    [Fact]
    public async Task ResolveRequiredSha1RejectsOversizedMetadata()
    {
        using var httpClient = new HttpClient(new DelegateHttpHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(new string('a', LoaderInstallerChecksumResolver.MaximumChecksumMetadataBytes + 1))
            })));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LoaderInstallerChecksumResolver.ResolveRequiredSha1Async(
                httpClient,
                downloadSpeedLimitState: null,
                NullLogger.Instance,
                "https://maven.minecraftforge.net/example-installer.jar",
                DownloadSourcePreference.Official,
                "Forge",
                downloadSpeedLimitMbPerSecond: 0,
                CancellationToken.None));

        Assert.Contains("checksum metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveRequiredSha1RejectsUnavailableMetadata()
    {
        using var httpClient = new HttpClient(new DelegateHttpHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            })));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            LoaderInstallerChecksumResolver.ResolveRequiredSha1Async(
                httpClient,
                downloadSpeedLimitState: null,
                NullLogger.Instance,
                "https://maven.minecraftforge.net/example-installer.jar",
                DownloadSourcePreference.Official,
                "Forge",
                downloadSpeedLimitMbPerSecond: 0,
                CancellationToken.None));
    }

    [Fact]
    public async Task ResolveRequiredSha1PropagatesCancellation()
    {
        var requestStarted = false;
        using var httpClient = new HttpClient(new DelegateHttpHandler((_, _) =>
        {
            requestStarted = true;
            throw new InvalidOperationException("The request should not start after cancellation.");
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LoaderInstallerChecksumResolver.ResolveRequiredSha1Async(
                httpClient,
                downloadSpeedLimitState: null,
                NullLogger.Instance,
                "https://maven.minecraftforge.net/example-installer.jar",
                DownloadSourcePreference.Official,
                "Forge",
                downloadSpeedLimitMbPerSecond: 0,
                cancellation.Token));

        Assert.False(requestStarted);
    }

    private sealed class DelegateHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}
