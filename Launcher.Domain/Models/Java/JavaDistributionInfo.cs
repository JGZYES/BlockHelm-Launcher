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

/// <summary>
/// Java 发行版信息
/// </summary>
public sealed class JavaDistributionInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Architecture { get; set; } = "x64";
    public string Platform { get; set; } = "windows";
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string? MirrorDownloadUrl { get; set; }
    public string? ChecksumSha256 { get; set; }
    public string? ChecksumType { get; set; } // sha256, sha1, md5
    public string? PackageType { get; set; } // zip, tar.gz
    public string? JrePathInArchive { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }
    public string? DownloadPageUrl { get; set; }
}

/// <summary>
/// Java 安装结果
/// </summary>
public sealed class JavaInstallResult
{
    public bool IsSuccess { get; set; }
    public string? InstallPath { get; set; }
    public string? JavaExecutablePath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DownloadedArchivePath { get; set; }
}

/// <summary>
/// Java 下载进度事件参数
/// </summary>
public sealed class JavaDownloadProgressEventArgs : EventArgs
{
    public JavaDownloadProgressEventArgs(string status, double progress, long bytesDownloaded, long totalBytes)
    {
        Status = status;
        Progress = progress;
        BytesDownloaded = bytesDownloaded;
        TotalBytes = totalBytes;
    }

    public string Status { get; }
    public double Progress { get; }
    public long BytesDownloaded { get; }
    public long TotalBytes { get; }
}

/// <summary>
/// Java 发行版支持的厂商
/// </summary>
public static class JavaVendorNames
{
    public const string Mojang = "Mojang";
    public const string EclipseTemurin = "Eclipse Temurin";
    public const string MicrosoftBuild = "Microsoft Build of OpenJDK";
    public const string AzulZulu = "Azul Zulu";
    public const string AmazonCorretto = "Amazon Corretto";
    public const string BellSoftLiberica = "BellSoft Liberica";
    public const string OracleGraalVM = "Oracle GraalVM";
    public const string AlibabaDragonwell = "Alibaba Dragonwell";
    public const string SAPMachine = "SAP SapMachine";
    public const string RedHatBuild = "Red Hat Build of OpenJDK";
    public const string IBMSemeru = "IBM Semeru";
    public const string JetBrainsRuntime = "JetBrains Runtime";
}

/// <summary>
/// Java 架构类型
/// </summary>
public static class JavaArchitectures
{
    public const string X64 = "x64";
    public const string X86 = "x86";
    public const string Arm64 = "arm64";
    public const string Arm32 = "arm32";

    public static string GetCurrentArchitecture()
    {
        var arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "AMD64";
        return arch.ToLowerInvariant() switch
        {
            "amd64" or "x64" => X64,
            "x86" or "i386" or "i686" => X86,
            "arm64" or "aarch64" => Arm64,
            _ => X64
        };
    }
}

/// <summary>
/// Java 平台类型
/// </summary>
public static class JavaPlatforms
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string MacOS = "macos";

    public static string GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return Windows;
        if (OperatingSystem.IsLinux()) return Linux;
        if (OperatingSystem.IsMacOS()) return MacOS;
        return Windows;
    }
}