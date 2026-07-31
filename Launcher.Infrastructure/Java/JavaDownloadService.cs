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

using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Java;

/// <summary>
/// 多厂商 JDK 下载服务
/// 支持断点续传、任务持久化、自动恢复
/// </summary>
public sealed class JavaDownloadService : IJavaDownloadService
{
    private readonly HttpClient httpClient;
    private readonly LauncherPathProvider pathProvider;
    private readonly ILogger<JavaDownloadService> logger;

    // 国内加速镜像源（按优先级排序）
    private static readonly string[] MirrorPrefixes =
    [
        "https://mirrors.tuna.tsinghua.edu.cn",
        "https://mirrors.ustc.edu.cn",
    ];

    // Eclipse Temurin 支持的版本
    private static readonly string[] TemurinSupportedVersions = ["8", "11", "17", "21", "24", "25"];

    // Microsoft Build of OpenJDK 支持的版本
    private static readonly string[] MsOpenJdkSupportedVersions = ["8", "11", "13", "16", "17", "21"];

    public event EventHandler<JavaDownloadProgressEventArgs>? DownloadProgressChanged;

    public JavaDownloadService(
        LauncherPathProvider pathProvider,
        ILogger<JavaDownloadService>? logger = null)
    {
        this.pathProvider = pathProvider;
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseProxy = true,
            MaxAutomaticRedirections = 10
        };
        this.httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        this.logger = logger ?? NullLogger<JavaDownloadService>.Instance;
    }

    public async Task<IReadOnlyList<JavaDistributionInfo>> GetAvailableDistributionsAsync(
        string? version = null,
        string? vendor = null,
        string? architecture = null,
        string? platform = null,
        CancellationToken cancellationToken = default)
    {
        var arch = architecture ?? JavaArchitectures.GetCurrentArchitecture();
        var plat = platform ?? JavaPlatforms.GetCurrentPlatform();
        var results = new List<JavaDistributionInfo>();

        // Mojang 版本（使用 Adoptium 源，与 Temurin 同源）
        if (string.IsNullOrEmpty(vendor) || string.Equals(vendor, JavaVendorNames.Mojang, StringComparison.OrdinalIgnoreCase))
        {
            results.AddRange(await GetMojangDistributionsAsync(version, arch, plat, cancellationToken));
        }

        // Eclipse Temurin
        if (string.IsNullOrEmpty(vendor) || string.Equals(vendor, JavaVendorNames.EclipseTemurin, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var temurinDistros = await GetTemurinDistributionsAsync(version, arch, plat, cancellationToken);
                results.AddRange(temurinDistros);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch Temurin distributions");
            }
        }

        // Microsoft Build of OpenJDK
        if (string.IsNullOrEmpty(vendor) || string.Equals(vendor, JavaVendorNames.MicrosoftBuild, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var msDistros = await GetMicrosoftDistributionsAsync(version, arch, plat, cancellationToken);
                results.AddRange(msDistros);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch Microsoft OpenJDK distributions");
            }
        }

        return results.OrderBy(d => d.Version).ThenBy(d => d.Vendor).ToList();
    }

    public async Task<JavaInstallResult> DownloadAndInstallAsync(
        JavaDistributionInfo distribution,
        IProgress<(string Status, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            progress?.Report(("下载中...", 0));
            
            // 先尝试恢复断点续传
            var taskState = LoadDownloadTaskState(distribution);
            var archivePath = await DownloadArchiveWithResumeAsync(distribution, progress, cancellationToken, taskState);

            var installPath = GetInstallPath(distribution);

            progress?.Report(("解压中...", 30));
            await ExtractArchiveAsync(archivePath, installPath, cancellationToken);

            var javaExePath = FindJavaExecutable(installPath);
            if (string.IsNullOrEmpty(javaExePath))
            {
                return new JavaInstallResult
                {
                    IsSuccess = false,
                    ErrorMessage = "安装失败：无法找到 java 可执行文件"
                };
            }

            if (!OperatingSystem.IsWindows())
            {
                SetExecutablePermission(javaExePath);
            }

            // 下载成功后清理任务状态
            RemoveDownloadTaskState(distribution);

            try
            {
                File.Delete(archivePath);
            }
            catch
            {
            }

            progress?.Report(("完成", 100));
            DownloadProgressChanged?.Invoke(this, new JavaDownloadProgressEventArgs(
                "完成", 100, 0, 0));

            return new JavaInstallResult
            {
                IsSuccess = true,
                InstallPath = installPath,
                JavaExecutablePath = javaExePath
            };
        }
        catch (OperationCanceledException)
        {
            return new JavaInstallResult { IsSuccess = false, ErrorMessage = "下载已取消" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install Java: {Version}", distribution.Version);
            return new JavaInstallResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<string?> DownloadAsync(
        JavaDistributionInfo distribution,
        string targetDirectory,
        IProgress<(string Status, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(targetDirectory);
            var archivePath = await DownloadArchiveWithResumeAsync(distribution, progress, cancellationToken, null);
            var targetPath = Path.Combine(targetDirectory, Path.GetFileName(archivePath));
            File.Move(archivePath, targetPath, overwrite: true);
            return targetPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download Java");
            return null;
        }
    }

    public Task<IReadOnlyList<string>> GetManagedInstallsAsync(CancellationToken cancellationToken = default)
    {
        var javaDir = GetManagedJavaDirectory();
        if (!Directory.Exists(javaDir))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var installs = Directory.GetDirectories(javaDir).ToList();
        return Task.FromResult<IReadOnlyList<string>>(installs);
    }

    public Task<bool> UninstallAsync(string installPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Directory.Exists(installPath))
            {
                Directory.Delete(installPath, recursive: true);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to uninstall Java at {Path}", installPath);
            return Task.FromResult(false);
        }
    }

    public Task<IReadOnlyList<JavaDistributionInfo>> CheckForUpdatesAsync(
        string vendor,
        string currentVersion,
        string architecture,
        string platform,
        CancellationToken cancellationToken = default)
    {
        return GetAvailableDistributionsAsync(
            version: null,
            vendor: vendor,
            architecture: architecture,
            platform: platform,
            cancellationToken: cancellationToken);
    }

    // --- 断点续传任务持久化 ---

    private string GetTaskStateDirectory()
    {
        var dir = Path.Combine(pathProvider.DefaultDataDirectory, "temp", "java_downloads");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string GetTaskStatePath(JavaDistributionInfo distribution)
    {
        var safeId = distribution.Id.Replace('/', '_').Replace('\\', '_');
        return Path.Combine(GetTaskStateDirectory(), $"{safeId}.state.json");
    }

    private string GetArchivePath(JavaDistributionInfo distribution)
    {
        var ext = distribution.PackageType?.Equals("tar.gz", StringComparison.OrdinalIgnoreCase) == true ? ".tar.gz" : ".zip";
        var safeId = distribution.Id.Replace('/', '_').Replace('\\', '_');
        return Path.Combine(GetTaskStateDirectory(), $"{safeId}{ext}");
    }

    private DownloadTaskState? LoadDownloadTaskState(JavaDistributionInfo distribution)
    {
        var statePath = GetTaskStatePath(distribution);
        if (!File.Exists(statePath))
            return null;

        try
        {
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<DownloadTaskState>(json);
        }
        catch
        {
            return null;
        }
    }

    private void SaveDownloadTaskState(JavaDistributionInfo distribution, string archivePath, long downloadedBytes)
    {
        var statePath = GetTaskStatePath(distribution);
        var state = new DownloadTaskState
        {
            DistributionId = distribution.Id,
            ArchivePath = archivePath,
            DownloadedBytes = downloadedBytes,
            DownloadUrl = distribution.DownloadUrl,
            LastUpdate = DateTime.UtcNow
        };

        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(statePath, json);
        }
        catch
        {
        }
    }

    private void RemoveDownloadTaskState(JavaDistributionInfo distribution)
    {
        var statePath = GetTaskStatePath(distribution);
        try
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
        catch
        {
        }
    }

    // --- 带断点续传的下载 ---

    private async Task<string> DownloadArchiveWithResumeAsync(
        JavaDistributionInfo distribution,
        IProgress<(string Status, double Progress)>? progress,
        CancellationToken cancellationToken,
        DownloadTaskState? existingState)
    {
        var archivePath = GetArchivePath(distribution);
        var totalBytes = distribution.SizeBytes;
        long existingBytes = 0;
        bool isResume = false;

        // 检查是否有未完成的下载
        if (existingState is not null && existingState.DownloadedBytes > 0 && File.Exists(archivePath))
        {
            existingBytes = new FileInfo(archivePath).Length;
            if (existingBytes == existingState.DownloadedBytes)
            {
                isResume = true;
                logger.LogInformation("Resuming download for {Id} at {Bytes} bytes", distribution.Id, existingBytes);
                progress?.Report(($"恢复下载中...", (double)existingBytes / (totalBytes > 0 ? totalBytes : 1) * 100));
            }
            else
            {
                // 文件大小不匹配，从头开始
                existingBytes = 0;
                File.Delete(archivePath);
            }
        }
        else if (File.Exists(archivePath))
        {
            existingBytes = new FileInfo(archivePath).Length;
            if (totalBytes > 0 && existingBytes >= totalBytes)
            {
                // 已下载完成
                return archivePath;
            }
            else if (existingBytes > 0)
            {
                File.Delete(archivePath);
                existingBytes = 0;
            }
        }

        // 构建候选 URL 列表（国内镜像优先）
        var urlsToTry = BuildDownloadUrls(distribution);

        // 并发测速，选择最快可用的 URL
        var fastestUrl = await SelectFastestUrlAsync(urlsToTry, cancellationToken);
        if (fastestUrl != urlsToTry[0])
        {
            urlsToTry.Remove(fastestUrl);
            urlsToTry.Insert(0, fastestUrl);
        }

        Exception? lastException = null;

        foreach (var url in urlsToTry)
        {
            try
            {
                progress?.Report((isResume ? "恢复下载中..." : "下载中...", (double)existingBytes / (totalBytes > 0 ? totalBytes : 1) * 100));
                
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                
                // 断点续传：添加 Range header
                if (existingBytes > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(existingBytes, null);
                }

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                // 检查 Content-Type，拒绝 HTML 错误页
                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (contentType is not null && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug("Skipping URL {Url} - returned HTML instead of binary", url);
                    lastException = new HttpRequestException("服务器返回了 HTML 页面，可能是无效的下载链接");
                    continue;
                }

                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                {
                    lastException = new HttpRequestException($"HTTP {response.StatusCode}");
                    logger.LogDebug("Download failed for URL {Url}: {StatusCode}", url, response.StatusCode);
                    continue;
                }

                // 获取总大小（用于进度计算）
                var contentLength = response.Content.Headers.ContentLength ?? 0;
                if (response.StatusCode == System.Net.HttpStatusCode.PartialContent && totalBytes == 0)
                {
                    // 206 响应，计算总大小
                    totalBytes = existingBytes + contentLength;
                }
                else if (response.StatusCode != System.Net.HttpStatusCode.PartialContent && totalBytes == 0)
                {
                    totalBytes = contentLength;
                }

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = isResume 
                    ? new FileStream(archivePath, FileMode.Append, FileAccess.Write, FileShare.None)
                    : new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[65536];
                long totalRead = existingBytes;
                int bytesRead;
                var lastSaveTime = DateTime.UtcNow;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    // 每 500ms 保存一次状态
                    var now = DateTime.UtcNow;
                    if ((now - lastSaveTime).TotalMilliseconds > 500)
                    {
                        SaveDownloadTaskState(distribution, archivePath, totalRead);
                        lastSaveTime = now;
                    }

                    if (totalBytes > 0)
                    {
                        var pct = (double)totalRead / totalBytes * 95;
                        progress?.Report(("下载中...", pct));
                        DownloadProgressChanged?.Invoke(this, new JavaDownloadProgressEventArgs(
                            "下载中", pct, totalRead, totalBytes));
                    }
                    else
                    {
                        progress?.Report(($"下载中... {totalRead / 1024 / 1024} MB", 0));
                    }
                }

                // 保存最终状态
                SaveDownloadTaskState(distribution, archivePath, totalRead);

                // 验证下载的文件
                if (totalRead > 0 && File.Exists(archivePath))
                {
                    logger.LogInformation("Downloaded {TotalBytes} bytes from {Url}", totalRead, url);
                    return archivePath;
                }

                lastException = new InvalidOperationException("下载的文件为空");
            }
            catch (OperationCanceledException)
            {
                // 保存当前进度
                var currentSize = File.Exists(archivePath) ? new FileInfo(archivePath).Length : 0;
                if (currentSize > 0)
                {
                    SaveDownloadTaskState(distribution, archivePath, currentSize);
                }
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                logger.LogDebug(ex, "Download failed for URL {Url}", url);
            }
        }

        throw lastException ?? new InvalidOperationException("所有下载源均失败");
    }

    private List<string> BuildDownloadUrls(JavaDistributionInfo distribution)
    {
        var urls = new List<string>();
        var officialUrl = distribution.DownloadUrl;

        if (string.IsNullOrEmpty(officialUrl))
            return urls;

        // 如果是 GitHub URL，添加多个国内加速镜像（按优先级排序）
        if (officialUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var mirrorPrefixes = new[]
            {
                "https://ghfast.top/",
                "https://gh-proxy.com/",
                "https://github.moeyy.xyz/",
                "https://mirror.ghproxy.com/"
            };

            foreach (var prefix in mirrorPrefixes)
            {
                urls.Add(prefix + officialUrl);
            }
        }

        // 添加官方 URL 作为最后 fallback
        urls.Add(officialUrl);

        return urls;
    }

    /// <summary>
    /// 并发测试多个 URL，返回第一个响应成功的 URL（国内镜像优先）
    /// </summary>
    private async Task<string> SelectFastestUrlAsync(
        List<string> urls,
        CancellationToken cancellationToken)
    {
        if (urls.Count <= 1)
            return urls.FirstOrDefault() ?? string.Empty;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var testTasks = urls.Select(url => TestUrlReachabilityAsync(url, cts.Token)).ToList();

        while (testTasks.Count > 0)
        {
            var completed = await Task.WhenAny(testTasks);
            testTasks.Remove(completed);

            var result = await completed;
            if (result != null)
            {
                cts.Cancel();
                logger.LogInformation("Selected fastest URL: {Url}", result);
                return result;
            }
        }

        logger.LogWarning("All URL tests failed, falling back to first URL");
        return urls[0];
    }

    private async Task<string?> TestUrlReachabilityAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // 只请求 1 字节，避免下载整个文件
            request.Headers.Range = new RangeHeaderValue(0, 0);

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            // 接受 200（不支持 Range）或 206（支持 Range）
            if (response.IsSuccessStatusCode)
            {
                return url;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "URL test failed: {Url}", url);
        }
        return null;
    }

    // --- Mojang 版本（使用 Adoptium 源） ---

    private async Task<List<JavaDistributionInfo>> GetMojangDistributionsAsync(
        string? version, string arch, string plat, CancellationToken ct)
    {
        var versions = TemurinSupportedVersions.AsEnumerable();
        if (!string.IsNullOrEmpty(version))
            versions = versions.Where(v => v.StartsWith(version, StringComparison.OrdinalIgnoreCase));

        var results = new List<JavaDistributionInfo>();

        foreach (var ver in versions)
        {
            try
            {
                var dist = await FetchTemurinLatestAsync(ver, arch, plat, ct, isMojang: true);
                if (dist is not null)
                    results.Add(dist);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to fetch Mojang JDK {Version}", ver);
            }
        }

        return results;
    }

    // --- Eclipse Temurin / Adoptium ---

    private async Task<List<JavaDistributionInfo>> GetTemurinDistributionsAsync(
        string? version, string arch, string plat, CancellationToken ct)
    {
        var versions = TemurinSupportedVersions.AsEnumerable();
        if (!string.IsNullOrEmpty(version))
            versions = versions.Where(v => v.StartsWith(version, StringComparison.OrdinalIgnoreCase));

        var results = new List<JavaDistributionInfo>();

        foreach (var ver in versions)
        {
            try
            {
                var dist = await FetchTemurinLatestAsync(ver, arch, plat, ct, isMojang: false);
                if (dist is not null)
                    results.Add(dist);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to fetch Temurin {Version}", ver);
            }
        }

        return results;
    }

    private async Task<JavaDistributionInfo?> FetchTemurinLatestAsync(
        string version, string arch, string plat, CancellationToken ct, bool isMojang)
    {
        var osParam = plat switch
        {
            "windows" => "windows",
            "linux" => "linux",
            "macos" => "mac",
            _ => "windows"
        };

        var archParam = arch switch
        {
            "x64" => "x64",
            "x86" => "x32",
            "arm64" => "aarch64",
            _ => "x64"
        };

        var url = $"https://api.adoptium.net/v3/assets/latest/{version}/hotspot?os={osParam}&arch={archParam}&image_type=jdk&vendor=eclipse";

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;

        var item = root[0];
        var binary = item.GetProperty("binary");
        var package = binary.GetProperty("package");

        var downloadUrl = package.GetProperty("link").GetString() ?? string.Empty;
        var sha256 = package.TryGetProperty("checksum", out var checksumEl) ? checksumEl.GetString() : null;
        var size = package.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
        var fullVer = item.GetProperty("version").GetProperty("openjdk_version").GetString() ?? version;

        var vendor = isMojang ? JavaVendorNames.Mojang : JavaVendorNames.EclipseTemurin;
        var name = isMojang ? $"Mojang JDK {version}" : $"Eclipse Temurin JDK {version}";

        return new JavaDistributionInfo
        {
            Id = $"{(isMojang ? "mojang" : "temurin")}-{version}",
            Name = name,
            Vendor = vendor,
            Version = version,
            Architecture = arch,
            Platform = plat,
            DownloadUrl = downloadUrl,
            ChecksumSha256 = sha256,
            SizeBytes = size,
            PackageType = downloadUrl.EndsWith(".zip") ? "zip" : "tar.gz",
            ReleaseDate = item.TryGetProperty("release_date", out var rdEl)
                ? DateTimeOffset.Parse(rdEl.GetString() ?? string.Empty)
                : null
        };
    }

    // --- Microsoft Build of OpenJDK ---

    private async Task<List<JavaDistributionInfo>> GetMicrosoftDistributionsAsync(
        string? version, string arch, string plat, CancellationToken ct)
    {
        var versions = MsOpenJdkSupportedVersions.AsEnumerable();
        if (!string.IsNullOrEmpty(version))
            versions = versions.Where(v => v.StartsWith(version, StringComparison.OrdinalIgnoreCase));

        var results = new List<JavaDistributionInfo>();

        foreach (var ver in versions)
        {
            try
            {
                var dist = await FetchMicrosoftLatestAsync(ver, arch, plat, ct);
                if (dist is not null)
                    results.Add(dist);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to fetch Microsoft OpenJDK {Version}", ver);
            }
        }

        return results;
    }

    private Task<JavaDistributionInfo?> FetchMicrosoftLatestAsync(
        string version, string arch, string plat, CancellationToken ct)
    {
        var archParam = arch switch
        {
            "x64" => "x64",
            "x86" => "x86",
            "arm64" => "arm64",
            _ => "x64"
        };

        var osParam = plat switch
        {
            "windows" => "windows",
            "linux" => "linux",
            "macos" => "macos",
            _ => "windows"
        };

        // Microsoft OpenJDK 使用 aka.ms 短链接（会自动重定向）
        var fileName = $"microsoft-jdk-{version}-{osParam}-{archParam}.zip";
        var officialUrl = $"https://aka.ms/download-jdk/{fileName}";

        return Task.FromResult<JavaDistributionInfo?>(new JavaDistributionInfo
        {
            Id = $"ms-{version}",
            Name = $"Microsoft OpenJDK {version}",
            Vendor = JavaVendorNames.MicrosoftBuild,
            Version = version,
            Architecture = arch,
            Platform = plat,
            DownloadUrl = officialUrl,
            PackageType = "zip"
        });
    }

    // --- 通用安装逻辑 ---

    private string GetInstallPath(JavaDistributionInfo distribution)
    {
        var vendorDir = distribution.Vendor.Replace(" ", "_", StringComparison.Ordinal);
        return Path.Combine(GetManagedJavaDirectory(), vendorDir, $"jdk-{distribution.Version}");
    }

    private string GetManagedJavaDirectory()
    {
        return Path.Combine(pathProvider.DefaultDataDirectory, "java", "managed");
    }

    private async Task ExtractArchiveAsync(
        string archivePath,
        string installPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(installPath);

        // 检查是否为 tar.gz
        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGzAsync(archivePath, installPath, cancellationToken);
            return;
        }

        // 验证 ZIP 文件有效性
        try
        {
            using var testArchive = ZipFile.OpenRead(archivePath);
            if (testArchive.Entries.Count == 0)
                throw new InvalidOperationException("ZIP 文件为空或已损坏");
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("ZIP 文件损坏，可能是下载不完整，请重试");
        }

        using var archive = ZipFile.OpenRead(archivePath);
        var prefix = GetArchivePrefix(archive);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = entry.FullName;
            if (!string.IsNullOrEmpty(prefix) && relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath[prefix.Length..].TrimStart('/').TrimStart('\\');
            }

            if (string.IsNullOrEmpty(relativePath))
                continue;

            var targetPath = Path.Combine(installPath, relativePath);

            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
            {
                Directory.CreateDirectory(targetPath);
            }
            else
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                await using var entryStream = entry.Open();
                await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                await entryStream.CopyToAsync(fileStream, cancellationToken);
            }
        }
    }

    private static async Task ExtractTarGzAsync(string archivePath, string installPath, CancellationToken ct)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{installPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is not null)
            {
                await process.WaitForExitAsync(ct);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"tar 解压失败，退出码: {process.ExitCode}");
                }
            }
        }
        catch (Exception)
        {
            throw new InvalidOperationException("无法解压 tar.gz 文件，请手动解压");
        }
    }

    private static string GetArchivePrefix(ZipArchive archive)
    {
        var firstEntry = archive.Entries.FirstOrDefault();
        if (firstEntry is null)
            return string.Empty;

        var firstName = firstEntry.FullName;
        var slashIndex = firstName.IndexOf('/');
        var backslashIndex = firstName.IndexOf('\\');

        var separatorIndex = Math.Min(
            slashIndex >= 0 ? slashIndex : int.MaxValue,
            backslashIndex >= 0 ? backslashIndex : int.MaxValue);

        return separatorIndex < int.MaxValue ? firstName[..(separatorIndex + 1)] : string.Empty;
    }

    private string FindJavaExecutable(string installPath)
    {
        var javaName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var javaPath = Path.Combine(installPath, "bin", javaName);

        if (File.Exists(javaPath))
            return javaPath;

        try
        {
            var files = Directory.GetFiles(installPath, javaName, SearchOption.AllDirectories);
            return files.FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void SetExecutablePermission(string path)
    {
        try
        {
            System.Diagnostics.Process.Start("chmod", $"+x \"{path}\"")?.WaitForExit();
        }
        catch
        {
        }
    }
}

/// <summary>
/// 下载任务状态（用于断点续传）
/// </summary>
public sealed class DownloadTaskState
{
    public string DistributionId { get; set; } = string.Empty;
    public string ArchivePath { get; set; } = string.Empty;
    public long DownloadedBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime LastUpdate { get; set; }
}
