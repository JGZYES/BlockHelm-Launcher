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

namespace Launcher.App.Utilities;

public enum DroppedFileType
{
    Unknown,
    Mod,
    ResourcePack,
    ShaderPack,
    World,
    Modpack,
    URL,
    Directory
}

public static class DroppedFileTypeDetector
{
    public static DroppedFileType Detect(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return DroppedFileType.Unknown;

        // URL detection
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return DroppedFileType.URL;

        // Directory detection
        if (Directory.Exists(pathOrUrl))
            return DroppedFileType.Directory;

        if (!File.Exists(pathOrUrl))
            return DroppedFileType.Unknown;

        var extension = Path.GetExtension(pathOrUrl).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "jar" => DroppedFileType.Mod,
            "zip" => DetectZipContent(pathOrUrl),
            "litematic" => DroppedFileType.World,
            "mcworld" => DroppedFileType.World,
            "rar" => DroppedFileType.Modpack,
            "7z" => DroppedFileType.Modpack,
            _ => DroppedFileType.Unknown
        };
    }

    public static IReadOnlyDictionary<DroppedFileType, List<string>> Classify(IEnumerable<string> paths)
    {
        var classified = new Dictionary<DroppedFileType, List<string>>();
        foreach (var path in paths)
        {
            var type = Detect(path);
            if (type == DroppedFileType.Unknown)
                continue;

            if (!classified.ContainsKey(type))
                classified[type] = [];
            classified[type].Add(path);
        }
        return classified;
    }

    private static DroppedFileType DetectZipContent(string path)
    {
        try
        {
            // Quick heuristic: check file name patterns first
            var fileName = Path.GetFileName(path).ToLowerInvariant();

            // Modpack indicators
            if (fileName.Contains("modpack")
                || fileName.Contains("curseforge")
                || fileName.Contains("modrinth")
                || fileName.Contains("technic"))
                return DroppedFileType.Modpack;

            // Try to peek at the first few entries in the zip
            using var stream = File.OpenRead(path);
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var entries = archive.Entries.Take(20).ToList();
            var entryNames = entries.Select(e => e.FullName.ToLowerInvariant()).ToList();

            // Modpack manifest indicators
            if (entryNames.Any(n => n.Contains("modlist") || n.Contains("manifest") || n.Contains("modpack")))
                return DroppedFileType.Modpack;

            // Shader pack indicators
            if (entryNames.Any(n => n.StartsWith("shaders/") || n.Contains("shader")))
                return DroppedFileType.ShaderPack;

            // Resource pack indicators
            if (entryNames.Any(n => n.StartsWith("assets/") && n.Contains("lang")))
                return DroppedFileType.ResourcePack;

            // If it has a mods/ directory and is not a shader/resource pack, treat as mod
            if (entryNames.Any(n => n.StartsWith("mods/")))
                return DroppedFileType.Modpack;

            // World indicators
            if (entryNames.Any(n => n.StartsWith("region/") || n.EndsWith(".mca")))
                return DroppedFileType.World;

            // Default to resource pack for zips with assets/ but no other indicators
            if (entryNames.Any(n => n.StartsWith("assets/")))
                return DroppedFileType.ResourcePack;

            return DroppedFileType.Unknown;
        }
        catch
        {
            return DroppedFileType.Unknown;
        }
    }
}
