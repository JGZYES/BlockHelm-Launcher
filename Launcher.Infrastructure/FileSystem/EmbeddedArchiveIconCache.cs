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
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.FileSystem;

/// <summary>
/// 从 zip/jar 归档中提取指定图标条目并缓存为本地 PNG，供 Mod、光影包等本地内容列表复用。
/// </summary>
/// <remarks>
/// 缓存键包含归档路径、大小、最后写入时间和图标条目名，包更新后自动生成新缓存而不会复用旧图。
/// 任何读取或解码失败都返回 null，避免单个损坏包影响列表加载；调用方据此回退默认图标。
/// </remarks>
internal static class EmbeddedArchiveIconCache
{
    /// <summary>
    /// 打开归档读取 <paramref name="iconEntryName"/> 指向的图标条目，缓存到 <paramref name="cacheDirectory"/>，
    /// 返回可用于 WPF 绑定的 <c>file:///</c> 绝对 URI。
    /// </summary>
    public static string? TryCacheIcon(
        FileInfo archiveFile,
        string iconEntryName,
        string cacheDirectory,
        ILogger? logger = null)
    {
        if (archiveFile is null || string.IsNullOrWhiteSpace(iconEntryName))
            return null;

        if (!archiveFile.Exists)
            return null;

        var normalizedEntryName = NormalizeEntryName(iconEntryName);
        if (normalizedEntryName is null)
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(archiveFile.FullName);
            var iconEntry = archive.Entries.FirstOrDefault(entry =>
                string.Equals(
                    NormalizeEntryName(entry.FullName),
                    normalizedEntryName,
                    StringComparison.OrdinalIgnoreCase));
            if (iconEntry is null)
                return null;

            Directory.CreateDirectory(cacheDirectory);
            var cachePath = GetCachePath(archiveFile, iconEntry.FullName, cacheDirectory);
            if (File.Exists(cachePath))
                return new Uri(cachePath).AbsoluteUri;

            using var iconStream = iconEntry.Open();
            var bitmap = LoadBitmap(iconStream);
            try
            {
                SavePng(bitmap, cachePath);
            }
            catch (IOException) when (File.Exists(cachePath))
            {
                // 并发场景下另一个线程可能已写入同名缓存；保留已存在文件即可。
            }

            return new Uri(cachePath).AbsoluteUri;
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException)
        {
            logger?.LogWarning(
                exception,
                "Failed to cache embedded archive icon. ArchivePath={ArchivePath} IconEntry={IconEntry}",
                archiveFile.FullName,
                iconEntryName);
            return null;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                exception,
                "Unexpected failure while caching embedded archive icon. ArchivePath={ArchivePath} IconEntry={IconEntry}",
                archiveFile.FullName,
                iconEntryName);
            return null;
        }
    }

    private static string? NormalizeEntryName(string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
            return null;

        var normalized = entryName.Replace('\\', '/').Trim().TrimStart('/');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static BitmapSource LoadBitmap(Stream source)
    {
        // OnLoad 将像素完全读入内存，关闭 zip 后 UI 仍可安全使用缓存图像。
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;

        var decoder = BitmapDecoder.Create(
            buffer,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault()
            ?? throw new InvalidDataException("Embedded icon contains no frames.");
        frame.Freeze();
        return frame;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string GetCachePath(FileInfo archiveFile, string iconEntryName, string cacheDirectory)
    {
        var hashInput = $"{archiveFile.FullName}|{archiveFile.Length}|{archiveFile.LastWriteTimeUtc.Ticks}|{iconEntryName}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(cacheDirectory, $"{hash}.png");
    }
}
