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

using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsDetailsViewModel
{
public GameSettingsFileDropEvaluation EvaluateImportDrop(IReadOnlyList<string> paths)
    {
        // 由当前分区决定可接受类型，聚合层保证拖放不会路由到隐藏页面。
        if (SelectedInstance is null)
            return GameSettingsFileDropEvaluation.Hidden;

        // 智能识别：如果当前分区不匹配文件类型，尝试自动路由
        var fileTypes = DroppedFileTypeDetector.Classify(paths);
        if (fileTypes.Count > 0 && !IsCurrentSectionCompatible(fileTypes.Keys))
        {
            // 返回可接受评估但提示将自动路由
            return GameSettingsFileDropEvaluation.Accept(
                string.Format(Strings.GameSettings_DropAutoRouteMessageFormat,
                    string.Join(", ", fileTypes.Keys.Select(MapFileTypeToDisplayName))));
        }

        return SelectedSection?.Id?.ToLowerInvariant() switch
        {
            "mod_management" => ModManagement.EvaluateDroppedFiles(paths),
            "saves" => SaveManagement.EvaluateDroppedFiles(paths),
            "resource_packs" => ResourcePackManagement.EvaluateDroppedFiles(paths),
            "shaders" => ShaderPackManagement.EvaluateDroppedFiles(paths),
            _ => GameSettingsFileDropEvaluation.Hidden
        };
    }

    public Task HandleImportDropAsync(IReadOnlyList<string> paths)
    {
        // 实际文件操作仍由对应分区服务完成，此处只选择目标流程。
        if (SelectedInstance is null)
            return Task.CompletedTask;

        // 智能识别并自动路由
        var fileTypes = DroppedFileTypeDetector.Classify(paths);
        if (fileTypes.Count > 0 && !IsCurrentSectionCompatible(fileTypes.Keys))
        {
            return RouteByFileTypeAsync(fileTypes);
        }

        return SelectedSection?.Id?.ToLowerInvariant() switch
        {
            "mod_management" => ModManagement.ImportDroppedModFilesAsync(paths),
            "saves" => SaveManagement.ImportDroppedSaveArchivesAsync(paths),
            "resource_packs" => ResourcePackManagement.ImportDroppedResourcePackArchivesAsync(paths),
            "shaders" => ShaderPackManagement.ImportDroppedShaderPackArchivesAsync(paths),
            _ => Task.CompletedTask
        };
    }

    private async Task RouteByFileTypeAsync(IReadOnlyDictionary<DroppedFileType, List<string>> classified)
    {
        foreach (var (type, files) in classified)
        {
            Task importTask = type switch
            {
                DroppedFileType.Mod => ModManagement.ImportDroppedModFilesAsync(files),
                DroppedFileType.World => SaveManagement.ImportDroppedSaveArchivesAsync(files),
                DroppedFileType.ResourcePack => ResourcePackManagement.ImportDroppedResourcePackArchivesAsync(files),
                DroppedFileType.ShaderPack => ShaderPackManagement.ImportDroppedShaderPackArchivesAsync(files),
                DroppedFileType.Modpack => ModManagement.ImportDroppedModFilesAsync(files),
                _ => Task.CompletedTask
            };
            await importTask.ConfigureAwait(false);
        }
    }

    private bool IsCurrentSectionCompatible(IEnumerable<DroppedFileType> fileTypes)
    {
        var sectionId = SelectedSection?.Id?.ToLowerInvariant();
        if (sectionId is null)
            return false;

        return sectionId switch
        {
            "mod_management" => fileTypes.Contains(DroppedFileType.Mod) || fileTypes.Contains(DroppedFileType.Modpack),
            "saves" => fileTypes.Contains(DroppedFileType.World),
            "resource_packs" => fileTypes.Contains(DroppedFileType.ResourcePack),
            "shaders" => fileTypes.Contains(DroppedFileType.ShaderPack),
            _ => false
        };
    }

    private static string MapFileTypeToDisplayName(DroppedFileType type) => type switch
    {
        DroppedFileType.Mod => Strings.GameSettings_DropTypeMod,
        DroppedFileType.World => Strings.GameSettings_DropTypeWorld,
        DroppedFileType.ResourcePack => Strings.GameSettings_DropTypeResourcePack,
        DroppedFileType.ShaderPack => Strings.GameSettings_DropTypeShaderPack,
        DroppedFileType.Modpack => Strings.GameSettings_DropTypeModpack,
        _ => type.ToString()
    };

    public void ResolvePendingModImportConflict(bool shouldReplace)
    {
        if (shouldReplace)
            ModManagement.ReplaceImportedModAsync(string.Empty);
        else
            ModManagement.SkipPendingImportedModReplacement();
    }

    public Task ReplaceImportedModAsync(string sourcePath)
    {
        return ModManagement.ReplaceImportedModAsync(sourcePath);
    }
}
