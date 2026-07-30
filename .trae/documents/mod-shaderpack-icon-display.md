# 识别 Mod / 光影包图标并在列表中显示

## Context（为什么做这个改动）

用户希望启动器能在 Mod 管理和光影包管理列表里**显示每个 Mod / 光影包自身的图标**，而不是只显示一个通用占位图标。

调研后确认：**UI 层、ViewModel、Domain 模型早就完整支持图标显示**，缺口只在 Infrastructure 数据层：

- `LocalMod` / `LocalShaderPack` / `LocalResourcePack` 三个 Domain 模型都有 `string? IconSource` 字段。
- 三个列表 View 的 XAML（`InstanceModManagementSettingsView.xaml`、`InstanceShaderPackManagementSettingsView.xaml`、`InstanceResourcePackManagementSettingsView.xaml`）都已把 `IconSource` 绑到 `controls:ListPageItemButton.IconSource`。
- 三个 ItemViewModel（`ModManagementModItemViewModel` / `ShaderPackManagementItemViewModel` / `ResourcePackManagementItemViewModel`）都有"有 `IconSource` 显示图片，否则回退默认 `IconKey`"的逻辑，并在 `SyncFrom` 里从 Domain 模型同步 `IconSource`。
- `IconSourceImageConverter` + `IconSourceImageLoader` 已把 `file:///...png` URI 转成可冻结的 `BitmapImage`，带内存缓存（按 `LastWriteTime+Length` 失效）。
- **资源包已完整工作**：`LocalResourcePackService.TryGetCachedIconSource` 从 zip 读根 `pack.png` → 缓存到 `cache/resourcepacks/icons/{hash}.png` → 返回 `file:///` URI。
- **光影包完全没实现**：`LocalShaderPackService.ToLocalShaderPack` 不读 zip，`IconSource` 永远 null。
- **Mod 也没实现本地内嵌图标**：`ModService.TryResolveMetadata` 第 227 行 `ResolvedModMetadata(... null)` 把 `IconSource` 硬编码为 null，只能等远程 `LocalModIconEnrichmentService`（Modrinth/CurseForge）补全。而 `LocalModsViewModel` 注释明确写"内嵌图标优先级更高"——这条优先逻辑当前是空转的。

**目标**：让数据层把 `IconSource` 填上，UI 自动显示。Mod 走"本地 jar 内嵌图标优先，远程增强兜底"，光影包走"本地 zip 内 `pack.png`"。

## 改动范围（全部在 Infrastructure，不动 UI/ViewModel/Domain）

### 1. 新建共享 helper：`Launcher.Infrastructure/FileSystem/EmbeddedArchiveIconCache.cs`

复刻 `LocalResourcePackService` 已验证的 `LoadBitmap` / `SavePng` / `GetCachePath` / `TryGetCachedIconSource` 模式，抽出为可复用静态工具，避免光影包和 Mod 各复制一份。

API（签名草案）：
```csharp
internal static class EmbeddedArchiveIconCache
{
    // 打开 archiveFile，从中读取 iconEntryName 指定的 png 条目，
    // 缓存到 cacheDirectory 下 {sha256}.png，返回 file:/// URI。
    // 找不到条目、损坏归档、IO 失败均返回 null（不抛异常），失败时记录 warning。
    public static string? TryCacheIcon(
        FileInfo archiveFile,
        string iconEntryName,
        string cacheDirectory,
        ILogger logger);
}
```

缓存键：`{archiveFile.FullName}|{Length}|{LastWriteTimeUtc.Ticks}|{iconEntryName}` → SHA256 → `{hash}.png`，与资源包一致，保证包更新后自动生成新缓存。

### 2. 改 `Launcher.Infrastructure/FileSystem/LocalShaderPackService.cs`

- 构造函数增加 `iconCacheDirectory = Path.Combine(pathProvider.DefaultDataDirectory, "cache", "shaderpacks", "icons")`（需注入 `LauncherPathProvider`，与资源包 service 对齐）。
- `ToLocalShaderPack(string path)` 中调用 `EmbeddedArchiveIconCache.TryCacheIcon(file, "pack.png", iconCacheDirectory, logger)`，把返回值赋给 `IconSource`。
  - 光影包（Iris/OptiFine）标准图标位置就是 zip 根目录的 `pack.png`，与资源包一致。
  - 找不到时 `IconSource = null`，UI 自动回退默认 `instance_setting_page/shader` 图标。

### 3. 改 `Launcher.Infrastructure/FileSystem/ModService.cs`

让 `TryResolveMetadata` 在已经打开 jar zip 读元数据的同时，顺便解析内嵌图标入口并缓存：

- `MetadataDeclaration` record 增加 `string? IconEntryName` 字段。
- 各 Loader 解析处补图标入口名：
  - **Fabric**（`TryFindFabricMetadataDeclaration`）：读 `fabric.mod.json` 的 `icon` 字段（字符串，指向 jar 内 png 路径）。
  - **Quilt**（`TryFindQuiltMetadataDeclaration`）：读 `quilt.mod.json` → `quilt_loader.metadata.icon`。
  - **Forge/NeoForge TOML**（`TryFindTomlMetadataDeclaration`）：新增 `TomlLogoFileRegex`，正则读 `logoFile` 字段。
  - **兜底**：以上都没找到时，尝试 jar 根 `pack.png`。
- `TryResolveMetadata` 在拿到 `MetadataDeclaration` 后，若有 `IconEntryName`，调用 `EmbeddedArchiveIconCache.TryCacheIcon(jarFile, iconEntryName, embeddedIconCacheDirectory, logger)`，结果赋给 `ResolvedModMetadata.IconSource`。
- 缓存目录用 `cache/mods/embedded-icons`（**故意避开** `cache/mods/icons`——后者被 `CleanupLegacyIconCacheDirectory` 当作旧实现残留删除，见第 66/420 行，新目录不受影响）。

### 不动的部分

- Domain 模型（已有 `IconSource`）、Application 接口、ViewModel、View XAML —— 均已支持。
- 资源包 service（已工作，不重构，降低风险）。
- 远程 Mod 图标增强 `LocalModIconEnrichmentService` —— 保留作为本地缺失时的兜底。`LocalModsViewModel` 已有"只给 `IconSource` 为空的项目应用远程结果"的逻辑（第 256、545 行），本地内嵌图标天然优先，不会被远程覆盖。

## 关键文件

- 新建：`Launcher.Infrastructure/FileSystem/EmbeddedArchiveIconCache.cs`
- 改：`Launcher.Infrastructure/FileSystem/LocalShaderPackService.cs`
- 改：`Launcher.Infrastructure/FileSystem/ModService.cs`
- 参考（不动，作为模式范本）：`Launcher.Infrastructure/FileSystem/LocalResourcePackService.cs:242-325`
- 参考（不动）：`Launcher.App/Controls/Lists/IconSourceImageLoader.cs`

## 验证

1. 构建：`dotnet build Launcher.sln`
2. 测试：`dotnet test Launcher.sln`
   - 新增 `Launcher.Tests` 下针对 `EmbeddedArchiveIconCache` 与 `ModService`/`LocalShaderPackService` 图标解析的测试（风险驱动，覆盖成功路径 + 无图标 + 损坏归档不抛）。
3. 手动验证：启动器 → 实例设置 → Mod 管理页 / 光影包管理页，确认带图标的包显示真实图标、无图标的回退默认图标、禁用状态的半透明与覆盖图标正常。
4. 主题检查：深色/浅色主题下图标显示正常（图标为内容型多色资产，按 AGENTS.md 可保留原色）。

## 提交

- 提交到用户指定的 `https://github.com/JGZYES/BlockHelm-Launcher.git`。
- 执行阶段检查当前 git remote / 分支状态；若未配置该 remote 则添加后推送，提交信息遵循仓库现有风格（含 GPL-3.0 头、简洁描述 why）。
