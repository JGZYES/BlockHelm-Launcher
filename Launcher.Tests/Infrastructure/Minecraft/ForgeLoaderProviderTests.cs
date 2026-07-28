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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CmlLib.Core;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ForgeLoaderProviderTests : TestTempDirectory
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ForgeLoaderProviderPassesSelectedJavaPathToInstallerRunner()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var expectedJavaPath = Path.Combine(TempRoot, "Selected Java", "bin", "java.exe");
        string? receivedJavaPath = null;
        var javaRuntimeResolver = new FixedJavaRuntimeResolver(expectedJavaPath);
        var handler = new ForgeHttpHandler();
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((gameDirectory, javaPath, _) =>
            {
                receivedJavaPath = javaPath;
                return CreateSandboxForgeInstallAsync(
                    gameDirectory,
                    "forge-1.20.1-47.4.20",
                    "1.20.1",
                    "1.20.1-47.4.20");
            }),
            javaRuntimeResolver: javaRuntimeResolver,
            handler: handler);

        await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null);

        Assert.Equal(expectedJavaPath, receivedJavaPath);
        Assert.Equal(minecraftDirectory, javaRuntimeResolver.LastRequest?.MinecraftDirectory);
        Assert.Equal(DownloadSourcePreference.Official, javaRuntimeResolver.LastRequest?.DownloadSourcePreference);
        Assert.Equal(LoaderKind.Forge, javaRuntimeResolver.LastRequest?.Loader);
        Assert.Equal("47.4.20", javaRuntimeResolver.LastRequest?.LoaderVersion);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri.EndsWith(
                "forge-1.20.1-47.4.20-installer.jar.sha1",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForgeLoaderProviderDoesNotDownloadOrRunInstallerWhenChecksumIsUnavailable()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var runnerStarted = false;
        var handler = new ForgeHttpHandler(checksumUnavailable: true);
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((_, _, _) =>
            {
                runnerStarted = true;
                return Task.CompletedTask;
            }),
            handler: handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null));

        Assert.False(runnerStarted);
        Assert.DoesNotContain(
            handler.RequestUris,
            uri => uri.AbsoluteUri.EndsWith("installer.jar", StringComparison.Ordinal));
        AssertNoInstallerSessions("launcher-forge");
    }

    [Fact]
    public async Task ForgeLoaderProviderDoesNotRunOrPublishInstallerWithMismatchedChecksum()
    {
        const string finalVersionName = "1.20.1-forge-47.4.20";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var runnerStarted = false;
        var handler = new ForgeHttpHandler(
            installerChecksumOverride: "0000000000000000000000000000000000000000");
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((_, _, _) =>
            {
                runnerStarted = true;
                return Task.CompletedTask;
            }),
            handler: handler);

        await Assert.ThrowsAnyAsync<Exception>(() => provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            finalVersionName,
            "47.4.20",
            progress: null));

        Assert.False(runnerStarted);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri.EndsWith("installer.jar", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", finalVersionName)));
        AssertNoInstallerSessions("launcher-forge");
    }

    [Fact]
    public async Task ForgeLoaderProviderDoesNotRunOrPublishWhenOfficialBaseMetadataFails()
    {
        const string finalVersionName = "1.20.1-forge-47.4.20";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var runnerStarted = false;
        var handler = new ForgeHttpHandler(
            versionMetadataSha1Override: "0000000000000000000000000000000000000000");
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((_, _, _) =>
            {
                runnerStarted = true;
                return Task.CompletedTask;
            }),
            handler: handler);

        await Assert.ThrowsAnyAsync<Exception>(() => provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            finalVersionName,
            "47.4.20",
            progress: null));

        Assert.False(runnerStarted);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri == "https://piston-meta.mojang.com/v1/packages/test/1.20.1.json");
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", finalVersionName)));
        AssertNoInstallerSessions("launcher-forge");
    }

    [Fact]
    public async Task ForgeLoaderProviderDoesNotStartInstallerWhenJavaSelectionFails()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var runnerStarted = false;
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((_, _, _) =>
            {
                runnerStarted = true;
                return Task.CompletedTask;
            }),
            javaRuntimeResolver: new FailingJavaRuntimeResolver());

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() => provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null));

        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing, exception.Reason);
        Assert.False(runnerStarted);
    }

    [Fact]
    public async Task ForgeLoaderCanInstallWithOfficialVersionNameWithoutPublishingVanillaJson()
    {
        const string versionName = "1.20.1";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var handler = new ForgeHttpHandler();
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
                CreateSandboxForgeInstallAsync(
                    gameDirectory,
                    "forge-1.20.1-47.4.20",
                    versionName,
                    "1.20.1-47.4.20")),
            handler: handler);

        var installedName = await provider.InstallAsync(
            versionName,
            minecraftDirectory,
            versionName,
            "47.4.20",
            progress: null);

        var jsonPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var versionJson = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))!.AsObject();
        Assert.Equal(versionName, installedName);
        Assert.Equal("net.minecraftforge.client.loading.ClientModLoader", versionJson["mainClass"]!.GetValue<string>());
        Assert.Contains(
            versionJson["libraries"]!.AsArray(),
            node => node?["name"]?.GetValue<string>() == "net.minecraftforge:forge:1.20.1-47.4.20");
        Assert.False(versionJson.ContainsKey("inheritsFrom"));
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri == "https://piston-meta.mojang.com/v1/packages/test/1.20.1.json");
    }

    [Fact]
    public async Task ForgeLoaderProviderInstallsLegacyForgeAfterVerifyingInstallerChecksum()
    {
        const string minecraftVersion = "1.10.2";
        const string loaderVersion = "12.18.3.2511";
        const string finalVersionName = "Legacy Forge Pack";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var installerBytes = CreateLegacyForgeInstallerBytes();
        var handler = new ForgeHttpHandler(
            include1201Html: false,
            include1102Html: true,
            legacyInstallerBytes: installerBytes);
        var runnerStartedAfterVerifiedMetadata = false;
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner(async (installerMinecraftDirectory, _, _) =>
            {
                var baseJsonPath = Path.Combine(
                    installerMinecraftDirectory,
                    "versions",
                    minecraftVersion,
                    $"{minecraftVersion}.json");
                Assert.True(File.Exists(baseJsonPath));
                Assert.Equal(
                    minecraftVersion,
                    JsonNode.Parse(await File.ReadAllTextAsync(baseJsonPath))!["id"]!.GetValue<string>());
                runnerStartedAfterVerifiedMetadata = true;
                throw new InvalidOperationException("installClient UnrecognizedOptionException");
            }),
            handler: handler);

        var installedName = await provider.InstallAsync(
            minecraftVersion,
            minecraftDirectory,
            finalVersionName,
            loaderVersion,
            progress: null);

        Assert.Equal(finalVersionName, installedName);
        Assert.True(runnerStartedAfterVerifiedMetadata);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri.EndsWith(
                "forge-1.10.2-12.18.3.2511-installer.jar.sha1",
                StringComparison.Ordinal));
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri.EndsWith(
                "forge-1.10.2-12.18.3.2511-installer.jar",
                StringComparison.Ordinal));
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri == "https://piston-meta.mojang.com/v1/packages/test/1.10.2.json");
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", minecraftVersion)));
    }

    [Fact]
    public async Task ForgeInstallerRunnerDoesNotStartWhenAlreadyCanceled()
    {
        var started = false;
        var runner = new ForgeInstallerRunner(_ =>
        {
            started = true;
            return null;
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunInstallerAsync(
            Path.Combine(TempRoot, "ignored-java.exe"),
            Path.Combine(TempRoot, "ignored-installer.jar"),
            TempRoot,
            cancellation.Token));

        Assert.False(started);
    }

    [Fact]
    public async Task ForgeInstallerRunnerRejectsPathJavaFallbackWithoutStartingProcess()
    {
        var started = false;
        var runner = new ForgeInstallerRunner(_ =>
        {
            started = true;
            return null;
        });

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunInstallerAsync(
            "java",
            Path.Combine(TempRoot, "installer.jar"),
            TempRoot,
            CancellationToken.None));

        Assert.False(started);
    }

    [Fact]
    public async Task ForgeIntegrityRepairPreservesVersionJsonBytesAndUserContent()
    {
        const string versionName = "Better MC [FORGE] BMC4";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var httpClient = new HttpClient(new ForgeHttpHandler());
        var provider = new ForgeLoaderProvider(
            httpClient,
            new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
                CreateSandboxForgeInstallAsync(
                    gameDirectory,
                    "forge-1.20.1-47.4.20",
                    "1.20.1",
                    "1.20.1-47.4.20")),
            new NoOpFinalVersionInstaller(),
            TempRoot,
            javaRuntimeResolver: new FixedJavaRuntimeResolver());
        await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            versionName,
            "47.4.20",
            progress: null);
        await EnsureUnverifiedVersionLibrariesExistAsync(minecraftDirectory, versionName);
        var versionJsonPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var versionJson = JsonNode.Parse(await File.ReadAllTextAsync(versionJsonPath))!.AsObject();
        versionJson["launcher"]!.AsObject()["forgeProcessorArtifacts"] = new JsonObject
        {
            ["schemaVersion"] = 2
        };
        await File.WriteAllTextAsync(versionJsonPath, versionJson.ToJsonString());
        var originalVersionJsonBytes = await File.ReadAllBytesAsync(versionJsonPath);

        var missingRelativePaths = new[]
        {
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-srg.jar",
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-extra.jar",
            "net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-client.jar"
        };
        foreach (var relativePath in missingRelativePaths)
            File.Delete(GetGeneratedLibraryPath(minecraftDirectory, relativePath));

        var userFiles = new Dictionary<string, string>
        {
            [Path.Combine(minecraftDirectory, "mods", "keep.jar")] = "mod",
            [Path.Combine(minecraftDirectory, "config", "keep.toml")] = "config",
            [Path.Combine(minecraftDirectory, "saves", "World", "level.dat")] = "save"
        };
        foreach (var userFile in userFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userFile.Key)!);
            await File.WriteAllTextAsync(userFile.Key, userFile.Value);
        }

        var service = new GameFileIntegrityService(
            httpClient,
            downloadSpeedLimitState: null,
            logger: null,
            loaderProviders: [provider],
            gameInstallCoordinator: new GameInstallCoordinator());
        var progressReports = new ConcurrentQueue<LauncherProgress>();
        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(
                minecraftDirectory,
                versionName,
                Path.Combine(minecraftDirectory, "versions", versionName))
            {
                LoaderIdentity = new GameFileLoaderIdentity(
                    LoaderKind.Forge,
                    "1.20.1",
                    "47.4.20")
            },
            new GameFileRepairOptions(AllowRepair: true),
            new InlineProgress(progressReports));

        Assert.True(
            result.LaunchAllowed,
            string.Join(Environment.NewLine, result.Failures.Select(failure =>
                $"{failure.Category}: {failure.Reason} {failure.TargetPath} {failure.Source}")));
        Assert.True(result.RepairedCount >= missingRelativePaths.Length);
        Assert.All(missingRelativePaths, relativePath =>
            Assert.True(File.Exists(GetGeneratedLibraryPath(minecraftDirectory, relativePath))));
        foreach (var userFile in userFiles)
            Assert.Equal(userFile.Value, await File.ReadAllTextAsync(userFile.Key));

        var manifest = await LoaderArtifactManifestStore.ReadAsync(
            Path.Combine(minecraftDirectory, "versions", versionName),
            new GameFileLoaderIdentity(LoaderKind.Forge, "1.20.1", "47.4.20"),
            CancellationToken.None);
        Assert.True(manifest.IsValid);
        Assert.All(missingRelativePaths, relativePath =>
            Assert.Contains(
                manifest.Manifest!.Artifacts,
                artifact => artifact.RelativePath == $"libraries/{relativePath}"));
        Assert.Equal(originalVersionJsonBytes, await File.ReadAllBytesAsync(versionJsonPath));
        using var repairedVersionJson = JsonDocument.Parse(originalVersionJsonBytes);
        Assert.True(
            repairedVersionJson.RootElement.GetProperty("launcher").TryGetProperty(
                "forgeProcessorArtifacts",
                out _));

        var visibleProgress = progressReports
            .Where(report => report.DownloadSpeedTelemetry is null)
            .ToArray();
        Assert.DoesNotContain(
            visibleProgress,
            report => report.Stage.StartsWith("Install.", StringComparison.Ordinal));
        var expectedStages = new[]
        {
            LaunchProgressStages.RepairingLoaderInstaller,
            LaunchProgressStages.CheckingJava,
            LaunchProgressStages.RunningLoaderInstaller,
            LaunchProgressStages.FinalizingLoaderVersion,
            LaunchProgressStages.PublishingLoaderArtifacts,
            LaunchProgressStages.RevalidatingFiles
        };
        var previousIndex = -1;
        foreach (var stage in expectedStages)
        {
            var index = Array.FindIndex(visibleProgress, report => report.Stage == stage);
            Assert.True(index > previousIndex, $"Launch progress stage {stage} was missing or out of order.");
            previousIndex = index;
        }
        var percents = visibleProgress
            .Where(report => report.Percent is not null)
            .Select(report => report.Percent!.Value)
            .ToArray();
        Assert.Equal(percents.Order(), percents);
        Assert.Equal(90, visibleProgress.Last(report => report.Stage == LaunchProgressStages.RevalidatingFiles).Percent);
    }

    private static int? TryReadProcessId(string path)
    {
        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var processId)
                ? processId
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool HasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException(timeoutMessage);

            await Task.Delay(25);
        }
    }

    private ForgeLoaderProvider CreateProvider(
        IForgeInstallerRunner? runner = null,
        IFinalVersionInstaller? finalVersionInstaller = null,
        ILoaderInstallerJavaRuntimeResolver? javaRuntimeResolver = null,
        ForgeHttpHandler? handler = null)
    {
        return new ForgeLoaderProvider(
            new HttpClient(handler ?? new ForgeHttpHandler()),
            runner ?? new NoOpForgeInstallerRunner(),
            finalVersionInstaller ?? new NoOpFinalVersionInstaller(),
            TempRoot,
            javaRuntimeResolver: javaRuntimeResolver ?? new FixedJavaRuntimeResolver());
    }

    private void AssertNoInstallerSessions(string directoryName)
    {
        var root = Path.Combine(TempRoot, directoryName);
        Assert.True(!Directory.Exists(root) || Directory.GetDirectories(root).Length == 0);
    }

    private static async Task CreateSandboxForgeInstallAsync(
        string minecraftDirectory,
        string versionName,
        string inheritsFrom,
        string combinedForgeVersion)
    {
        await CreateVanillaVersionAsync(minecraftDirectory, inheritsFrom);
        CreateForgeDerivedVersion(minecraftDirectory, versionName, inheritsFrom, combinedForgeVersion);
        CreateGeneratedForgeLibrary(minecraftDirectory, combinedForgeVersion);
    }

    private static async Task CreateVanillaVersionAsync(string minecraftDirectory, string versionName)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            $$"""
            {
              "id": "{{versionName}}",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main",
              "libraries": [
                { "name": "com.mojang:patchy:2.2.10" }
              ],
              "arguments": {
                "game": [ "--username", "${auth_player_name}" ],
                "jvm": [ "-Djava.library.path=${natives_directory}" ]
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, $"{versionName}.jar"), "base jar");
    }

    private static void CreateForgeDerivedVersion(string minecraftDirectory, string versionName, string inheritsFrom, string combinedForgeVersion)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var versionJson = new JsonObject
        {
            ["id"] = versionName,
            ["inheritsFrom"] = inheritsFrom,
            ["mainClass"] = "net.minecraftforge.client.loading.ClientModLoader",
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = $"net.minecraftforge:forge:{combinedForgeVersion}" }
            }
        };

        File.WriteAllText(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            versionJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(versionDirectory, "win_args.txt"),
            "--launchTarget forge_client");
    }

    private static void CreateGeneratedForgeLibrary(string minecraftDirectory, string combinedForgeVersion)
    {
        WriteGeneratedLibrary(
            minecraftDirectory,
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-extra.jar",
            "minecraft extra");
        WriteGeneratedLibrary(
            minecraftDirectory,
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-srg.jar",
            "minecraft srg");
        WriteGeneratedLibrary(
            minecraftDirectory,
            $"net/minecraftforge/forge/{combinedForgeVersion}/forge-{combinedForgeVersion}-client.jar",
            "patched forge client");
        WriteGeneratedLibrary(
            minecraftDirectory,
            "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar",
            "forge runtime");
    }

    private static void WriteGeneratedLibrary(string minecraftDirectory, string relativePath, string content)
    {
        var path = GetGeneratedLibraryPath(minecraftDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class InlineProgress(ConcurrentQueue<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Enqueue(value);
    }

    private static async Task EnsureUnverifiedVersionLibrariesExistAsync(
        string minecraftDirectory,
        string versionName)
    {
        var versionPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var version = JsonNode.Parse(await File.ReadAllTextAsync(versionPath))!.AsObject();
        if (version["libraries"] is not JsonArray libraries)
            return;
        foreach (var library in libraries.OfType<JsonObject>())
        {
            foreach (var artifact in ManagedLibraryArtifactResolver.EnumerateDownloads(library))
            {
                if (MinecraftFileIntegrity.IsSha1(artifact.Sha1))
                    continue;
                var path = GetGeneratedLibraryPath(minecraftDirectory, artifact.RelativePath);
                if (File.Exists(path))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "standard library");
            }
        }
    }

    private static string GetGeneratedLibraryPath(string minecraftDirectory, string relativePath) =>
        Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static byte[] CreateModernForgeInstallerBytes()
    {
        static string Sha1(string content) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var profileEntry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(profileEntry.Open()))
            {
                writer.Write(
                    $$"""
                {
                  "spec": 1,
                  "minecraft": "1.20.1",
                  "data": {
                    "MC_EXTRA": { "client": "[net.minecraft:client:1.20.1-20230612.114412:extra]" },
                    "MC_EXTRA_SHA": { "client": "'{{Sha1("minecraft extra")}}'" },
                    "MC_SRG": { "client": "[net.minecraft:client:1.20.1-20230612.114412:srg]" },
                    "PATCHED": { "client": "[net.minecraftforge:forge:1.20.1-47.4.20:client]" },
                    "PATCHED_SHA": { "client": "'{{Sha1("patched forge client")}}'" }
                  },
                  "libraries": [
                    {
                      "name": "net.minecraftforge:fmlcore:1.20.1-47.4.20",
                      "downloads": {
                        "artifact": {
                          "path": "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar",
                          "sha1": "{{Sha1("forge runtime")}}",
                          "size": 13
                        }
                      }
                    }
                  ],
                  "processors": [
                    { "sides": ["client"], "args": ["--extra", "{MC_EXTRA}"], "outputs": { "{MC_EXTRA}": "{MC_EXTRA_SHA}" } },
                    { "args": ["--output", "{MC_SRG}"] },
                    { "args": ["--output", "{PATCHED}"], "outputs": { "{PATCHED}": "{PATCHED_SHA}" } }
                  ]
                }
                """);
            }
            var runtimeEntry = archive.CreateEntry(
                "maven/net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar");
            using (var runtimeWriter = new StreamWriter(runtimeEntry.Open()))
            {
                runtimeWriter.Write("forge runtime");
            }
            var versionEntry = archive.CreateEntry("version.json");
            using var versionWriter = new StreamWriter(versionEntry.Open());
            versionWriter.Write(
                """
                {
                  "libraries": [
                    {
                      "name": "net.minecraftforge:fmlcore:1.20.1-47.4.20",
                      "downloads": {
                        "artifact": {
                          "path": "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar",
                          "sha1": "9de99c8b24ff448def492a91d4aa09e29511b66c",
                          "size": 13
                        }
                      }
                    }
                  ]
                }
                """);
        }
        return stream.ToArray();
    }

    private static byte[] CreateLegacyForgeInstallerBytes()
    {
        const string coordinate = "net.minecraftforge:forge:1.10.2-12.18.3.2511";
        const string payloadPath =
            "maven/net/minecraftforge/forge/1.10.2-12.18.3.2511/forge-1.10.2-12.18.3.2511.jar";
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var profileEntry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(profileEntry.Open()))
            {
                writer.Write(
                    $$"""
                    {
                      "install": {
                        "minecraft": "1.10.2",
                        "path": "{{coordinate}}",
                        "filePath": "{{payloadPath}}"
                      },
                      "versionInfo": {
                        "id": "1.10.2-forge1.10.2-12.18.3.2511",
                        "inheritsFrom": "1.10.2",
                        "type": "release",
                        "mainClass": "net.minecraft.launchwrapper.Launch",
                        "minecraftArguments": "--username ${auth_player_name}",
                        "libraries": [
                          { "name": "{{coordinate}}" }
                        ]
                      }
                    }
                    """);
            }

            var payloadEntry = archive.CreateEntry(payloadPath);
            using var payloadWriter = new StreamWriter(payloadEntry.Open());
            payloadWriter.Write("legacy forge runtime");
        }

        return stream.ToArray();
    }

    private sealed class NoOpForgeInstallerRunner : IForgeInstallerRunner
    {
        public Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpFinalVersionInstaller : IFinalVersionInstaller
    {
        public Task InstallAsync(
            string gameDirectory,
            string versionName,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FixedJavaRuntimeResolver(string? executablePath = null) : ILoaderInstallerJavaRuntimeResolver
    {
        public LoaderInstallerJavaRuntimeRequest? LastRequest { get; private set; }

        public Task<JavaRuntimeInfo> ResolveAsync(
            LoaderInstallerJavaRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var path = executablePath ?? Path.Combine("C:\\Program Files", "Launcher Java", "bin", "java.exe");
            return Task.FromResult(new JavaRuntimeInfo(
                "Launcher Java 21",
                "21.0.2",
                21,
                "x64",
                path,
                Path.GetDirectoryName(Path.GetDirectoryName(path))!,
                "Test"));
        }
    }

    private sealed class FailingJavaRuntimeResolver : ILoaderInstallerJavaRuntimeResolver
    {
        public Task<JavaRuntimeInfo> ResolveAsync(
            LoaderInstallerJavaRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new JavaRuntimeSelectionException(
                "No compatible Java runtime is available.",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing,
                17);
        }
    }

    private sealed class ScriptedForgeInstallerRunner : IForgeInstallerRunner
    {
        private readonly Func<string, string, string, Task> callback;

        public ScriptedForgeInstallerRunner(Func<string, string, string, Task> callback)
        {
            this.callback = callback;
        }

        public Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken)
        {
            return callback(minecraftDirectory, javaCommand, installerJarPath);
        }
    }

    private sealed class ForgeHttpHandler : HttpMessageHandler
    {
        private const string OfficialVersionJson = """{"id":"1.20.1","type":"release","mainClass":"net.minecraft.client.main.Main"}""";
        private const string LegacyVersionJson = """{"id":"1.10.2","type":"release","mainClass":"net.minecraft.client.main.Main"}""";
        private readonly bool include1201Html;
        private readonly bool include1102Html;
        private readonly byte[]? legacyInstallerBytes;
        private readonly string promotionsJson;
        private readonly string? installerChecksumOverride;
        private readonly string? versionMetadataSha1Override;
        private readonly bool checksumUnavailable;

        public ForgeHttpHandler(
            bool include1201Html = true,
            bool include1102Html = false,
            string? promotionsJson = null,
            byte[]? legacyInstallerBytes = null,
            string? installerChecksumOverride = null,
            string? versionMetadataSha1Override = null,
            bool checksumUnavailable = false)
        {
            this.include1201Html = include1201Html;
            this.include1102Html = include1102Html;
            this.legacyInstallerBytes = legacyInstallerBytes;
            this.installerChecksumOverride = installerChecksumOverride;
            this.versionMetadataSha1Override = versionMetadataSha1Override;
            this.checksumUnavailable = checksumUnavailable;
            this.promotionsJson = promotionsJson ?? """
                {
                  "promos": {
                    "1.20.1-latest": "47.4.20",
                    "1.20.1-recommended": "47.4.10"
                  }
                }
                """;
        }

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var uri = request.RequestUri!.AbsoluteUri
                .Replace("https://bmclapi2.bangbang93.com/maven/", "https://maven.minecraftforge.net/", StringComparison.OrdinalIgnoreCase);
            if (uri == "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json")
            {
                var sha1 = versionMetadataSha1Override
                    ?? Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(OfficialVersionJson)))
                        .ToLowerInvariant();
                var legacySha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(LegacyVersionJson)))
                    .ToLowerInvariant();
                return Task.FromResult(CreateJsonResponse(
                    request,
                    $$"""{"versions":[{"id":"1.20.1","url":"https://piston-meta.mojang.com/v1/packages/test/1.20.1.json","sha1":"{{sha1}}"},{"id":"1.10.2","url":"https://piston-meta.mojang.com/v1/packages/test/1.10.2.json","sha1":"{{legacySha1}}"}]}"""));
            }

            if (uri == "https://piston-meta.mojang.com/v1/packages/test/1.20.1.json")
                return Task.FromResult(CreateJsonResponse(request, OfficialVersionJson));
            if (uri == "https://piston-meta.mojang.com/v1/packages/test/1.10.2.json")
                return Task.FromResult(CreateJsonResponse(request, LegacyVersionJson));

            if (uri == "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json")
            {
                return Task.FromResult(CreateJsonResponse(request, promotionsJson));
            }

            if (uri == "https://files.minecraftforge.net/net/minecraftforge/forge/index_1.20.1.html")
            {
                if (!include1201Html)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

                return Task.FromResult(CreateHtmlResponse(request, """
                    <html>
                      <body>
                        <a href="https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-installer.jar">47.4.20</a>
                        <a href="https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.10/forge-1.20.1-47.4.10-installer.jar">47.4.10</a>
                      </body>
                    </html>
                """));
            }

            if (uri.EndsWith("-installer.jar.sha1", StringComparison.Ordinal))
            {
                if (checksumUnavailable)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        RequestMessage = request
                    });
                }

                var installerBytes = uri.Contains("/1.10.2-", StringComparison.Ordinal)
                    ? legacyInstallerBytes ?? "forge installer bytes"u8.ToArray()
                    : CreateModernForgeInstallerBytes();
                var sha1 = installerChecksumOverride
                    ?? Convert.ToHexString(SHA1.HashData(installerBytes)).ToLowerInvariant();
                return Task.FromResult(CreateJsonResponse(request, sha1));
            }

            if (uri == "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request));

            if (uri == "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.10/forge-1.20.1-47.4.10-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request));

            if (uri == "https://files.minecraftforge.net/net/minecraftforge/forge/index_1.10.2.html")
            {
                if (!include1102Html)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

                return Task.FromResult(CreateHtmlResponse(request, """
                    <html>
                      <body>
                        <a href="https://maven.minecraftforge.net/net/minecraftforge/forge/1.10.2-12.18.3.2511/forge-1.10.2-12.18.3.2511-installer.jar">12.18.3.2511</a>
                      </body>
                    </html>
                    """));
            }

            if (uri == "https://maven.minecraftforge.net/net/minecraftforge/forge/1.10.2-12.18.3.2511/forge-1.10.2-12.18.3.2511-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request, legacyInstallerBytes));

            throw new InvalidOperationException($"Unexpected request: {uri}");
        }

        private static HttpResponseMessage CreateJsonResponse(HttpRequestMessage request, string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json)
            };
        }

        private static HttpResponseMessage CreateHtmlResponse(HttpRequestMessage request, string html)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(html)
            };
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request)
        {
            return CreateBinaryResponse(request, CreateModernForgeInstallerBytes());
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request, byte[]? content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content ?? "forge installer bytes"u8.ToArray())
            };
        }
    }
}
