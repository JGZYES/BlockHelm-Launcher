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

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CmlLib.Core;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class NeoForgeLoaderProviderTests : TestTempDirectory
{
    [Fact]
    public async Task NeoForgeLoaderProviderPassesSelectedJavaPathToInstallerRunner()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.4");
        var expectedJavaPath = Path.Combine(TempRoot, "Selected Java", "bin", "java.exe");
        string? receivedJavaPath = null;
        var javaRuntimeResolver = new FixedJavaRuntimeResolver(expectedJavaPath);
        var handler = new NeoForgeHttpHandler();
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((gameDirectory, javaPath, _) =>
            {
                receivedJavaPath = javaPath;
                return CreateSandboxNeoForgeInstallAsync(gameDirectory, "neoforge-20.4.237", "1.20.4", "20.4.237");
            }),
            javaRuntimeResolver: javaRuntimeResolver,
            handler: handler);

        await provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            "1.20.4-neoforge-20.4.237",
            "20.4.237",
            progress: null);

        Assert.Equal(expectedJavaPath, receivedJavaPath);
        Assert.Equal(minecraftDirectory, javaRuntimeResolver.LastRequest?.MinecraftDirectory);
        Assert.Equal(DownloadSourcePreference.Official, javaRuntimeResolver.LastRequest?.DownloadSourcePreference);
        Assert.Equal(LoaderKind.NeoForge, javaRuntimeResolver.LastRequest?.Loader);
        Assert.Equal("20.4.237", javaRuntimeResolver.LastRequest?.LoaderVersion);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri.EndsWith(
                "neoforge-20.4.237-installer.jar.sha1",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task NeoForgeLoaderProviderDoesNotDownloadOrRunInstallerWhenChecksumIsInvalid()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.4");
        var runnerStarted = false;
        var handler = new NeoForgeHttpHandler(installerChecksumOverride: "not-a-sha1");
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((_, _, _) =>
            {
                runnerStarted = true;
                return Task.CompletedTask;
            }),
            handler: handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            "1.20.4-neoforge-20.4.237",
            "20.4.237",
            progress: null));

        Assert.False(runnerStarted);
        Assert.DoesNotContain(
            handler.RequestUris,
            uri => uri.AbsoluteUri.EndsWith("installer.jar", StringComparison.Ordinal));
        AssertNoInstallerSessions("launcher-neoforge");
    }

    [Fact]
    public async Task NeoForgeLoaderProviderDoesNotRunOrPublishInstallerWithMismatchedChecksum()
    {
        const string finalVersionName = "1.20.4-neoforge-20.4.237";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.4");
        var runnerStarted = false;
        var handler = new NeoForgeHttpHandler(
            installerChecksumOverride: "0000000000000000000000000000000000000000");
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((_, _, _) =>
            {
                runnerStarted = true;
                return Task.CompletedTask;
            }),
            handler: handler);

        await Assert.ThrowsAnyAsync<Exception>(() => provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            finalVersionName,
            "20.4.237",
            progress: null));

        Assert.False(runnerStarted);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri.EndsWith("installer.jar", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", finalVersionName)));
        AssertNoInstallerSessions("launcher-neoforge");
    }

    [Fact]
    public async Task NeoForgeLoaderProviderDoesNotRunOrPublishWhenOfficialBaseMetadataFails()
    {
        const string finalVersionName = "1.20.4-neoforge-20.4.237";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var runnerStarted = false;
        var handler = new NeoForgeHttpHandler(
            versionMetadataSha1Override: "0000000000000000000000000000000000000000");
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((_, _, _) =>
            {
                runnerStarted = true;
                return Task.CompletedTask;
            }),
            handler: handler);

        await Assert.ThrowsAnyAsync<Exception>(() => provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            finalVersionName,
            "20.4.237",
            progress: null));

        Assert.False(runnerStarted);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri == "https://piston-meta.mojang.com/v1/packages/test/1.20.4.json");
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", finalVersionName)));
        AssertNoInstallerSessions("launcher-neoforge");
    }

    [Fact]
    public async Task NeoForgeLoaderProviderRejectsSuccessfulInstallerWithMissingProcessorOutput()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.4");
        var provider = CreateProvider(new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
        {
            CreateNeoForgeDerivedVersion(gameDirectory, "neoforge-20.4.237", "1.20.4", "20.4.237");
            return Task.CompletedTask;
        }));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            "1.20.4-neoforge-20.4.237",
            "20.4.237",
            progress: null));

        Assert.Contains("processor output", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(
            minecraftDirectory,
            "versions",
            "1.20.4-neoforge-20.4.237")));
    }

    [Fact]
    public async Task NeoForgeLoaderCanInstallWithOfficialVersionNameWithoutPublishingVanillaJson()
    {
        const string versionName = "1.20.4";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var provider = CreateProvider(new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
            CreateSandboxNeoForgeInstallAsync(
                gameDirectory,
                "neoforge-20.4.237",
                versionName,
                "20.4.237")));

        var installedName = await provider.InstallAsync(
            versionName,
            minecraftDirectory,
            versionName,
            "20.4.237",
            progress: null);

        var jsonPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var versionJson = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))!.AsObject();
        Assert.Equal(versionName, installedName);
        Assert.Equal("cpw.mods.modlauncher.Launcher", versionJson["mainClass"]!.GetValue<string>());
        Assert.Contains(
            versionJson["libraries"]!.AsArray(),
            node => node?["name"]?.GetValue<string>() == "net.neoforged:neoforge:20.4.237");
        Assert.False(versionJson.ContainsKey("inheritsFrom"));
    }

    [Fact]
    public async Task NeoForgeLoaderProviderInstallDoesNotCreateRealVanillaBaseDirectoryWhenMissing()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var handler = new NeoForgeHttpHandler();
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
                CreateSandboxNeoForgeInstallAsync(gameDirectory, "neoforge-20.4.237", "1.20.4", "20.4.237")),
            handler: handler);

        var finalVersionName = await provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            "Imported NeoForge Pack",
            "20.4.237",
            progress: null);

        Assert.Equal("Imported NeoForge Pack", finalVersionName);
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.4")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "neoforge-20.4.237")));
        Assert.True(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "Imported NeoForge Pack")));
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri == "https://piston-meta.mojang.com/v1/packages/test/1.20.4.json");
    }

    [Fact]
    public async Task NeoForgeLoaderProviderInstallCleansCreatedVersionDirectoriesWhenInstallerFails()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.4");

        var provider = CreateProvider(new ScriptedForgeInstallerRunner(async (gameDirectory, _, _) =>
        {
            await CreateSandboxNeoForgeInstallAsync(gameDirectory, "neoforge-20.4.237", "1.20.4", "20.4.237");
            throw new InvalidOperationException("No usable Java runtime was found for NeoForge installation.");
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            "1.20.4-neoforge-20.4.237",
            "20.4.237",
            progress: null));

        Assert.True(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.4")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "neoforge-20.4.237")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.4-neoforge-20.4.237")));
    }

    [Fact]
    public async Task NeoForgeLoaderProviderInstalls1201FromLegacyArtifactCoordinates()
    {
        const string minecraftVersion = "1.20.1";
        const string loaderVersion = "47.1.106";
        const string legacyCoordinate = "1.20.1-47.1.106";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var handler = new LegacyNeoForge1201HttpHandler();
        var runnerStartedAfterVerifiedMetadata = false;
        var provider = new NeoForgeLoaderProvider(
            new HttpClient(handler),
            new ScriptedForgeInstallerRunner(async (gameDirectory, _, _) =>
            {
                var baseJsonPath = Path.Combine(
                    gameDirectory,
                    "versions",
                    minecraftVersion,
                    $"{minecraftVersion}.json");
                Assert.True(File.Exists(baseJsonPath));
                Assert.Equal(
                    minecraftVersion,
                    JsonNode.Parse(await File.ReadAllTextAsync(baseJsonPath))!["id"]!.GetValue<string>());
                runnerStartedAfterVerifiedMetadata = true;
                await CreateLegacy1201SandboxInstallAsync(
                    gameDirectory,
                    minecraftVersion,
                    loaderVersion,
                    legacyCoordinate);
            }),
            new NoOpFinalVersionInstaller(),
            TempRoot,
            javaRuntimeResolver: new FixedJavaRuntimeResolver());

        var installedName = await provider.InstallAsync(
            minecraftVersion,
            minecraftDirectory,
            "Legacy NeoForge Pack",
            loaderVersion,
            progress: null);

        Assert.Equal("Legacy NeoForge Pack", installedName);
        Assert.True(runnerStartedAfterVerifiedMetadata);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri
                == "https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-installer.jar");
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri
                == "https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-installer.jar.sha1");
        Assert.DoesNotContain(
            handler.RequestUris,
            uri => uri.AbsoluteUri.Contains(
                "/net/neoforged/neoforge/47.1.106/",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsoluteUri == "https://piston-meta.mojang.com/v1/packages/test/1.20.1.json");
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", minecraftVersion)));

        var versionJson = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            minecraftDirectory,
            "versions",
            installedName,
            $"{installedName}.json")))!.AsObject();
        Assert.Contains(
            versionJson["libraries"]!.AsArray(),
            node => node?["name"]?.GetValue<string>()
                == "net.neoforged:forge:1.20.1-47.1.106:universal");
    }

    private NeoForgeLoaderProvider CreateProvider(
        IForgeInstallerRunner? runner = null,
        IFinalVersionInstaller? finalVersionInstaller = null,
        ILoaderInstallerJavaRuntimeResolver? javaRuntimeResolver = null,
        NeoForgeHttpHandler? handler = null)
    {
        return new NeoForgeLoaderProvider(
            new HttpClient(handler ?? new NeoForgeHttpHandler()),
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

    private static async Task CreateSandboxNeoForgeInstallAsync(
        string minecraftDirectory,
        string versionName,
        string inheritsFrom,
        string loaderVersion)
    {
        await CreateVanillaVersionAsync(minecraftDirectory, inheritsFrom);
        CreateNeoForgeDerivedVersion(minecraftDirectory, versionName, inheritsFrom, loaderVersion);
        CreateGeneratedNeoForgeLibrary(minecraftDirectory, loaderVersion, includeUniversal: true);
    }

    private static async Task CreateLegacy1201SandboxInstallAsync(
        string minecraftDirectory,
        string minecraftVersion,
        string loaderVersion,
        string legacyCoordinate)
    {
        await CreateVanillaVersionAsync(minecraftDirectory, minecraftVersion);
        var versionName = $"{minecraftVersion}-forge-{loaderVersion}";
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var versionJson = new JsonObject
        {
            ["id"] = versionName,
            ["inheritsFrom"] = minecraftVersion,
            ["mainClass"] = "cpw.mods.modlauncher.Launcher",
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = $"net.neoforged:forge:{legacyCoordinate}" }
            }
        };
        File.WriteAllText(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            versionJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(versionDirectory, "win_args.txt"),
            "--launchTarget forge_client");

        var libraryDirectory = Path.Combine(
            minecraftDirectory,
            "libraries",
            "net",
            "neoforged",
            "forge",
            legacyCoordinate);
        Directory.CreateDirectory(libraryDirectory);
        File.WriteAllText(
            Path.Combine(libraryDirectory, $"forge-{legacyCoordinate}-client.jar"),
            "patched neoforge client");
        File.WriteAllText(
            Path.Combine(libraryDirectory, $"forge-{legacyCoordinate}-universal.jar"),
            "universal neoforge runtime");
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

    private static void CreateNeoForgeDerivedVersion(string minecraftDirectory, string versionName, string inheritsFrom, string loaderVersion)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var versionJson = new JsonObject
        {
            ["id"] = versionName,
            ["inheritsFrom"] = inheritsFrom,
            ["mainClass"] = "cpw.mods.modlauncher.Launcher",
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray(
                    "--fml.neoForgeVersion",
                    loaderVersion,
                    "--fml.mcVersion",
                    inheritsFrom)
            },
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = $"net.neoforged:neoforge:{loaderVersion}" }
            }
        };

        File.WriteAllText(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            versionJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(versionDirectory, "win_args.txt"),
            "--launchTarget neoforge_client");
    }

    private static void CreateGeneratedNeoForgeLibrary(
        string minecraftDirectory,
        string loaderVersion,
        bool includeUniversal)
    {
        var libraryDirectory = Path.Combine(
            minecraftDirectory,
            "libraries",
            "net",
            "neoforged",
            "neoforge",
            loaderVersion);
        Directory.CreateDirectory(libraryDirectory);
        File.WriteAllText(
            Path.Combine(libraryDirectory, $"neoforge-{loaderVersion}-client.jar"),
            "patched neoforge client");
        if (includeUniversal)
        {
            File.WriteAllText(
                Path.Combine(libraryDirectory, $"neoforge-{loaderVersion}-universal.jar"),
                "universal neoforge runtime");
        }
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

    private sealed class NeoForgeHttpHandler : HttpMessageHandler
    {
        private const string OfficialVersionJson = """{"id":"1.20.4","type":"release","mainClass":"net.minecraft.client.main.Main"}""";
        private readonly string? installerChecksumOverride;
        private readonly string? versionMetadataSha1Override;

        public NeoForgeHttpHandler(
            string? installerChecksumOverride = null,
            string? versionMetadataSha1Override = null)
        {
            this.installerChecksumOverride = installerChecksumOverride;
            this.versionMetadataSha1Override = versionMetadataSha1Override;
        }

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var uri = request.RequestUri!.AbsoluteUri
                .Replace("https://bmclapi2.bangbang93.com/maven/", "https://maven.neoforged.net/releases/", StringComparison.OrdinalIgnoreCase);
            if (uri == "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json")
            {
                var sha1 = versionMetadataSha1Override
                    ?? Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(OfficialVersionJson)))
                        .ToLowerInvariant();
                return Task.FromResult(CreateTextResponse(
                    request,
                    $$"""{"versions":[{"id":"1.20.4","url":"https://piston-meta.mojang.com/v1/packages/test/1.20.4.json","sha1":"{{sha1}}"}]}"""));
            }

            if (uri == "https://piston-meta.mojang.com/v1/packages/test/1.20.4.json")
                return Task.FromResult(CreateTextResponse(request, OfficialVersionJson));

            if (uri == "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml")
            {
                return Task.FromResult(CreateTextResponse(request, """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <metadata>
                      <groupId>net.neoforged</groupId>
                      <artifactId>neoforge</artifactId>
                      <versioning>
                        <versions>
                          <version>20.4.235-beta</version>
                          <version>20.4.236</version>
                          <version>20.4.237</version>
                          <version>20.6.115</version>
                          <version>21.1.234</version>
                        </versions>
                      </versioning>
                    </metadata>
                    """));
            }

            if (uri == "https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.237/neoforge-20.4.237-installer.jar.sha1")
            {
                var installerBytes = CreateInstallerBytes();
                var sha1 = installerChecksumOverride
                    ?? Convert.ToHexString(SHA1.HashData(installerBytes)).ToLowerInvariant();
                return Task.FromResult(CreateTextResponse(request, sha1));
            }
            if (uri == "https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.237/neoforge-20.4.237-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request, CreateInstallerBytes()));
            if (uri == "https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.237/neoforge-20.4.237-universal.jar")
                return Task.FromResult(CreateTextResponse(request, "universal neoforge runtime"));

            throw new InvalidOperationException($"Unexpected request: {uri}");
        }

        private static HttpResponseMessage CreateTextResponse(HttpRequestMessage request, string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content)
            };
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request, byte[] content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            };
        }
    }

    private sealed class LegacyNeoForge1201HttpHandler : HttpMessageHandler
    {
        private const string OfficialVersionJson =
            """{"id":"1.20.1","type":"release","mainClass":"net.minecraft.client.main.Main"}""";

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var uri = request.RequestUri!.AbsoluteUri
                .Replace(
                    "https://bmclapi2.bangbang93.com/maven/",
                    "https://maven.neoforged.net/releases/",
                    StringComparison.OrdinalIgnoreCase);
            if (uri == "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json")
            {
                var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(OfficialVersionJson)))
                    .ToLowerInvariant();
                return Task.FromResult(CreateTextResponse(
                    request,
                    $$"""{"versions":[{"id":"1.20.1","url":"https://piston-meta.mojang.com/v1/packages/test/1.20.1.json","sha1":"{{sha1}}"}]}"""));
            }

            if (uri == "https://piston-meta.mojang.com/v1/packages/test/1.20.1.json")
                return Task.FromResult(CreateTextResponse(request, OfficialVersionJson));
            if (uri
                == "https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-installer.jar.sha1")
            {
                var sha1 = Convert.ToHexString(SHA1.HashData(CreateLegacy1201InstallerBytes()))
                    .ToLowerInvariant();
                return Task.FromResult(CreateTextResponse(request, sha1));
            }

            if (uri
                == "https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-installer.jar")
            {
                return Task.FromResult(CreateBinaryResponse(request, CreateLegacy1201InstallerBytes()));
            }

            if (uri
                == "https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-universal.jar")
            {
                return Task.FromResult(CreateTextResponse(request, "universal neoforge runtime"));
            }

            throw new InvalidOperationException($"Unexpected request: {uri}");
        }

        private static HttpResponseMessage CreateTextResponse(
            HttpRequestMessage request,
            string content) =>
            new(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content)
            };

        private static HttpResponseMessage CreateBinaryResponse(
            HttpRequestMessage request,
            byte[] content) =>
            new(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            };
    }

    private static byte[] CreateInstallerBytes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write(
                """
                {
                  "data": {
                    "PATCHED": { "client": "[net.neoforged:neoforge:20.4.237:client]" }
                  },
                  "libraries": [
                    {
                      "name": "net.neoforged:neoforge:20.4.237:universal",
                      "downloads": {
                        "artifact": {
                          "path": "net/neoforged/neoforge/20.4.237/neoforge-20.4.237-universal.jar",
                          "sha1": "bb0166e91991e502fc8d8daf77eedced1b734f6a",
                          "size": 26
                        }
                      }
                    }
                  ],
                  "processors": [
                    { "args": ["--clean", "{MC_SRG}", "--output", "{PATCHED}"] }
                  ]
                }
                """);
            }
            var versionEntry = archive.CreateEntry("version.json");
            using var versionWriter = new StreamWriter(versionEntry.Open());
            versionWriter.Write(
                """
                {
                  "libraries": [
                    {
                      "name": "net.neoforged:neoforge:20.4.237:universal",
                      "downloads": {
                        "artifact": {
                          "path": "net/neoforged/neoforge/20.4.237/neoforge-20.4.237-universal.jar",
                          "sha1": "bb0166e91991e502fc8d8daf77eedced1b734f6a",
                          "size": 26
                        }
                      }
                    }
                  ]
                }
                """);
        }
        return stream.ToArray();
    }

    private static byte[] CreateLegacy1201InstallerBytes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var profileEntry = archive.CreateEntry("install_profile.json");
            using (var profileWriter = new StreamWriter(profileEntry.Open()))
            {
                profileWriter.Write(
                    """
                    {
                      "data": {
                        "PATCHED": {
                          "client": "[net.neoforged:forge:1.20.1-47.1.106:client]"
                        }
                      },
                      "libraries": [
                        {
                          "name": "net.neoforged:forge:1.20.1-47.1.106:universal",
                          "url": "https://maven.neoforged.net/releases/",
                          "downloads": {
                            "artifact": {
                              "path": "net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-universal.jar",
                              "sha1": "bb0166e91991e502fc8d8daf77eedced1b734f6a",
                              "size": 26
                            }
                          }
                        }
                      ],
                      "processors": [
                        { "args": ["--output", "{PATCHED}"] }
                      ]
                    }
                    """);
            }

            var versionEntry = archive.CreateEntry("version.json");
            using var versionWriter = new StreamWriter(versionEntry.Open());
            versionWriter.Write(
                """
                {
                  "libraries": [
                    {
                      "name": "net.neoforged:forge:1.20.1-47.1.106:universal",
                      "url": "https://maven.neoforged.net/releases/",
                      "downloads": {
                        "artifact": {
                          "path": "net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-universal.jar",
                          "sha1": "bb0166e91991e502fc8d8daf77eedced1b734f6a",
                          "size": 26
                        }
                      }
                    }
                  ]
                }
                """);
        }

        return stream.ToArray();
    }
}
