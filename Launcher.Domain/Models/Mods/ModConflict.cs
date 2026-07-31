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
/// Mod 冲突类型
/// </summary>
public enum ModConflictType
{
    /// <summary>
    /// ID 冲突：两个 Mod 使用相同的 ModId
    /// </summary>
    DuplicateId,
    
    /// <summary>
    /// 文件名冲突：两个 Mod JAR 文件包含相同的类路径
    /// </summary>
    FileNameConflict,
    
    /// <summary>
    /// 版本冲突：Mod 版本要求不满足
    /// </summary>
    VersionMismatch,
    
    /// <summary>
    /// 依赖冲突：缺少必要依赖或依赖版本不兼容
    /// </summary>
    MissingDependency,
    
    /// <summary>
    /// 不兼容的 Mod 组合
    /// </summary>
    IncompatibleCombination,
    
    /// <summary>
    /// 加载顺序冲突
    /// </summary>
    LoadOrderConflict
}

/// <summary>
/// Mod 冲突严重程度
/// </summary>
public enum ModConflictSeverity
{
    /// <summary>
    /// 警告：可能影响功能但不会导致崩溃
    /// </summary>
    Warning,
    
    /// <summary>
    /// 错误：很可能导致崩溃或功能失效
    /// </summary>
    Error,
    
    /// <summary>
    /// 严重：必定导致启动失败
    /// </summary>
    Critical
}

/// <summary>
/// Mod 冲突信息
/// </summary>
public sealed class ModConflict
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ModConflictType ConflictType { get; set; }
    public ModConflictSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ResolutionSuggestion { get; set; }
    
    // 涉及的 Mod
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string? OtherModId { get; set; }
    public string? OtherModName { get; set; }
    
    // 详细信息
    public string? ConflictingVersion { get; set; }
    public string? RequiredVersion { get; set; }
    public IReadOnlyList<string>? AffectedFiles { get; set; }
    
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Mod 冲突分析报告
/// </summary>
public sealed class ModConflictReport
{
    public string InstanceId { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public string Loader { get; set; } = string.Empty;
    
    public int TotalModsCount { get; set; }
    public int ConflictsCount => Conflicts.Count;
    public int CriticalCount => Conflicts.Count(c => c.Severity == ModConflictSeverity.Critical);
    public int ErrorCount => Conflicts.Count(c => c.Severity == ModConflictSeverity.Error);
    public int WarningCount => Conflicts.Count(c => c.Severity == ModConflictSeverity.Warning);
    
    public IReadOnlyList<ModConflict> Conflicts { get; set; } = [];
    public IReadOnlyList<ModDependencyGraphNode> DependencyGraph { get; set; } = [];
    
    public bool HasCriticalIssues => CriticalCount > 0;
    public bool HasErrors => ErrorCount > 0;
    public bool HasWarnings => WarningCount > 0;
    
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Mod 依赖关系图节点
/// </summary>
public sealed class ModDependencyGraphNode
{
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? LoaderType { get; set; }
    
    // 依赖的 ModId 列表
    public IReadOnlyList<string> DependsOn { get; set; } = [];
    
    // 被哪些 Mod 依赖
    public IReadOnlyList<string> DependedBy { get; set; } = [];
    
    // 可选依赖
    public IReadOnlyList<string> Recommends { get; set; } = [];
    
    public bool IsRequired { get; set; }
    public bool IsOptional { get; set; }
}

/// <summary>
/// Mod 兼容性检查结果
/// </summary>
public sealed class ModCompatibilityCheckResult
{
    public bool IsCompatible { get; set; }
    public IReadOnlyList<ModConflict> Conflicts { get; set; } = [];
    public IReadOnlyList<string> Warnings { get; set; } = [];
}
