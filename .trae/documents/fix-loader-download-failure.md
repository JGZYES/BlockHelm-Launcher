# 修复 Fabric 等加载器下载到 86% 失败

## Context（背景与根因）

用户反馈：在游戏设置里切换/安装加载器（Fabric 等）时，下载进度到 **86% 就失败**。

根因是上一轮"优化游戏下载速度"时我改了两个下载常量，这两个改动降低了下载稳定性：

1. `ManagedVersionRepairDownloadBatch.MaxConcurrency`：**8 → 16**
   - 16 路并发对镜像服务器压力过大，触发连接重置/限流，某个文件下载中途被断开。
   - 批处理层（`Launcher.Infrastructure\Minecraft\Launching\ManagedVersionRepairDownloadBatch.cs:81-88`）用 `Parallel.ForEachAsync` 并发下载，单个文件失败即整个批次抛异常——没有批处理级单文件重试。

2. `MinecraftDownloadRequestExecutor.MinimumSegmentedDownloadSize`：**8MB → 4MB**
   - 阈值降低让更多文件走多段 Range 下载，HTTP Range 请求数量翻倍，单段失败概率上升。
   - 执行器虽有重试（`DownloadRetryOptions` + `attemptCount`），但在 16 并发压满服务器时重试本身也会连续失败。

结论：这两个"优化"在没有配套退避/重试增强的情况下属于冒险改动，破坏了原有稳定下载行为。

## 修复方案（仅回退两个常量到原始稳定值）

### 文件 1：`Launcher.Infrastructure\Minecraft\Launching\ManagedVersionRepairDownloadBatch.cs:32`
```csharp
private const int MaxConcurrency = 16;   // 回退为 8
```
改为：
```csharp
private const int MaxConcurrency = 8;
```

### 文件 2：`Launcher.Infrastructure\Minecraft\MinecraftDownloadRequestExecutor.cs:40`
```csharp
internal const long MinimumSegmentedDownloadSize = 4L * 1024 * 1024;   // 回退为 8MB
```
改为：
```csharp
internal const long MinimumSegmentedDownloadSize = 8L * 1024 * 1024;
```

## 关于"下载速度优化"需求

回退后下载速度恢复到原始水平（稳定但非最快）。后续若要真正提速，需要先增强重试/退避机制（如批处理级单文件重试、指数退避），再逐步提升并发，而非直接拉高常量。本次不做该增强，保持改动聚焦。

## 验证

```powershell
dotnet build Launcher.sln
dotnet test Launcher.sln --filter "FullyQualifiedName~StringResourceTests|FullyQualifiedName~LayerDependencyContractTests"
```

然后运行 `Launcher.App\bin\Debug\net8.0-windows\BlockHelm_Launcher_x64.exe`，在游戏设置里对一个实例切换到 Fabric（或其他加载器），确认下载能跑到 100% 完成。
