# BlockHelm Launcher 跨平台兼容指南

## 概述

BlockHelm Launcher 基于 .NET 8 构建，原生支持 Windows、Linux 和 macOS。本文档提供跨平台部署和兼容性说明。

---

## 系统要求

### Windows
- Windows 10 或更高版本
- x64 架构
- 已安装或应用内下载 JDK 8/17/21/25/26

### Linux
- Ubuntu 20.04+、Fedora 34+、Arch Linux 或其他主流发行版
- x64 或 ARM64 架构
- GTK 3 桌面环境（用于 WPF 支持）
- 已安装或应用内下载 JDK 8/17/21/25/26

### macOS
- macOS 11 (Big Sur) 或更高版本
- Intel (x64) 或 Apple Silicon (ARM64) 架构
- 已安装或应用内下载 JDK 8/17/21/25/26

---

## Linux 兼容性

### 安装依赖

```bash
# Ubuntu/Debian
sudo apt update
sudo apt install -y libgtk-3-0 libgbm1 libxkbcommon0 libxdo3 libpango-1.0-0 libcairo2 libasound2 libsdl2-2.0-0 libsdl2-image-2.0-0 libfreetype6 fontconfig

# Fedora
sudo dnf install -y gtk3-libs libgbm libxkbcommon libxdo pango cairo alsa-lib SDL2 SDL2_image freetype fontconfig

# Arch Linux
sudo pacman -S --needed gtk3 libgbm libxkbcommon libxdo pango cairo alsa-lib sdl2 sdl2_image freetype2 fontconfig
```

### 运行应用

```bash
# 方式1: 直接运行
dotnet run --project Launcher.App

# 方式2: 发布后运行
dotnet publish Launcher.App -c Release -r linux-x64
cd Launcher.App/bin/Release/net8.0/linux-x64/publish
chmod +x BlockHelm_Launcher_x64
./BlockHelm_Launcher_x64
```

### 文件路径映射

| Windows | Linux | 说明 |
|---------|-------|------|
| `%USERPROFILE%\BlockHelm` | `~/.blockhelm` | 数据目录 |
| `%APPDATA%\BlockHelm` | `~/.config/blockhelm` | 配置目录 |
| `%TEMP%` | `/tmp` | 临时文件 |

### 已知限制

1. **WPF 在 Linux 上运行**: .NET 8 WPF 对 Linux 的支持仍在预览阶段，可能需要使用 `dotnet8-wpf` 运行时
2. **文件系统区分大小写**: Linux 文件系统区分大小写，路径需精确匹配
3. **权限管理**: 需要通过 `chmod` 设置文件权限，特别是 JDK 可执行文件
4. **桌面通知**: 依赖系统通知守护进程（如 `notification-daemon`）

### 常见问题

#### Q: 启动时提示缺少 libgtk-3 库
```bash
sudo apt install libgtk-3-0
```

#### Q: 游戏无法启动
1. 检查 JDK 是否正确安装
2. 检查 `java` 命令是否在 PATH 中
3. 使用应用内下载的 JDK

#### Q: 文件对话框不显示
- 确保安装了桌面环境的文件管理器
- 或使用直接拖拽文件到应用窗口

---

## macOS 兼容性

### 安装依赖

```bash
# 通过 Homebrew 安装必要的库
brew install pango cairo libffi libpng freetype

# 安装 .NET 8 SDK
brew install --cask dotnet-sdk
# 或从 https://dotnet.microsoft.com/download 下载
```

### 运行应用

```bash
# 方式1: 直接运行
dotnet run --project Launcher.App

# 方式2: 发布后运行
dotnet publish Launcher.App -c Release -r osx-x64
cd Launcher.App/bin/Release/net8.0/osx-x64/publish
./BlockHelm_Launcher_x64
```

### Apple Silicon 支持

```bash
# 发布 ARM64 版本
dotnet publish Launcher.App -c Release -r osx-arm64
```

### 文件路径映射

| Windows | macOS | 说明 |
|---------|-------|------|
| `%USERPROFILE%\BlockHelm` | `~/Library/Application Support/BlockHelm` | 数据目录 |
| `%APPDATA%\BlockHelm` | `~/Library/Preferences/BlockHelm` | 配置目录 |
| `%TEMP%` | `/tmp` | 临时文件 |

### 已知限制

1. **权限对话框**: macOS 可能会在首次访问文件系统时显示权限请求
2. **代码签名**: 分发到其他 Mac 可能需要代码签名
3. **Apple Silicon**: 某些旧版 JDK 可能需要 Rosetta 转换运行

### 常见问题

#### Q: 应用无法打开（安全限制）
```bash
# 移除隔离属性
xattr -d com.apple.quarantine BlockHelm_Launcher_x64
```

#### Q: 提示"无法验证开发者"
1. 打开"系统设置" → "隐私与安全性"
2. 点击"仍要打开"按钮

#### Q: JDK 下载后无法执行
```bash
# 设置执行权限
chmod +x ~/Library/Application\ Support/BlockHelm/java/managed/jdk-*/bin/java
```

---

## JDK 管理

### 应用内下载 JDK

BlockHelm Launcher 支持在应用内直接下载 Mojang 官方 JDK：

1. 打开 **游戏设置** → **Java** 页面
2. 点击 **下载 JDK** 按钮
3. 选择需要的版本（8/17/21/25/26）
4. 等待下载和安装完成

### 手动安装 JDK

如果应用内下载失败，可以手动安装 JDK：

#### Linux
```bash
# 使用系统包管理器
sudo apt install openjdk-17-jdk
# 或
sudo dnf install java-17-openjdk

# 或从 Adoptium 下载
wget https://api.adoptium.net/v3/binary/latest/17/ga/linux/x64/jdk/hotspot/normal/eclipse
sudo tar -xf OpenJDK17U-jdk_x64_linux_hotspot_*.tar.gz -C /usr/local/java/
sudo ln -s /usr/local/java/jdk-17/bin/java /usr/local/bin/java
```

#### macOS
```bash
# 使用 Homebrew
brew install openjdk@17
sudo ln -sfn $(brew --prefix)/opt/openjdk@17/libexec/openjdk.jdk /Library/Java/JavaVirtualMachines/openjdk-17.jdk

# 或从官网下载
# https://www.oracle.com/java/technologies/downloads/
```

### 环境变量配置

```bash
# 设置 JAVA_HOME (Linux/macOS)
export JAVA_HOME=/path/to/your/jdk
export PATH=$JAVA_HOME/bin:$PATH

# 添加到配置文件
echo 'export JAVA_HOME=/path/to/your/jdk' >> ~/.bashrc  # Linux
echo 'export JAVA_HOME=/path/to/your/jdk' >> ~/.zshrc   # macOS
```

---

## 跨平台构建

### 发布多平台版本

```bash
# Windows x64
dotnet publish Launcher.App -c Release -r win-x64

# Linux x64
dotnet publish Launcher.App -c Release -r linux-x64

# Linux ARM64
dotnet publish Launcher.App -c Release -r linux-arm64

# macOS x64
dotnet publish Launcher.App -c Release -r osx-x64

# macOS ARM64 (Apple Silicon)
dotnet publish Launcher.App -c Release -r osx-arm64

# 一次性发布所有平台
./publish-all.sh  # 需要创建此脚本
```

### 独立部署（Self-Contained）

```bash
# 无需目标机器安装 .NET 运行时
dotnet publish Launcher.App -c Release -r linux-x64 --self-contained true
dotnet publish Launcher.App -c Release -r osx-x64 --self-contained true
dotnet publish Launcher.App -c Release -r win-x64 --self-contained true
```

---

## 调试技巧

### 查看日志

```bash
# Linux
tail -f ~/.blockhelm/logs/launcher.log

# macOS
tail -f ~/Library/Logs/BlockHelm/launcher.log
```

### 性能监控

```bash
# Linux
top -p $(pgrep -f BlockHelm)
strace -p $(pgrep -f BlockHelm)

# macOS
ps aux | grep BlockHelm
sample BlockHelm_Launcher 5
```

### 游戏进程调试

```bash
# 查看 Java 进程
ps aux | grep java
# Linux
jstack <pid>
jmap -heap <pid>

# macOS
jcmd <pid> Thread.print
jmap -heap <pid>
```

---

## 版本兼容性矩阵

| Minecraft 版本 | 推荐 JDK | 支持加载器 |
|----------------|----------|-----------|
| 1.8.x | JDK 8 | Vanilla, Forge |
| 1.12.x | JDK 8 | Vanilla, Forge, Fabric |
| 1.16.x - 1.19.x | JDK 17 | Vanilla, Forge, Fabric, Quilt |
| 1.20.x+ | JDK 21 | Vanilla, Forge, Fabric, Quilt, NeoForge |
| 1.21.x+ | JDK 21+ | 全部 |
| 1.25.x+ | JDK 25 | 全部 |

---

## 故障排除

### 通用问题

1. **重置应用数据**: 删除数据目录下的 `config.json`
2. **清除缓存**: 删除数据目录下的 `cache/` 文件夹
3. **检查文件权限**: 确保数据目录有读写权限

### Linux 特有问题

```bash
# 检查依赖
ldd BlockHelm_Launcher_x64 | grep "not found"

# 检查 SELinux
getenforce  # 如果是 Enforcing，可能需要设置策略
```

### macOS 特有问题

```bash
# 检查 Gatekeeper
spctl --assess BlockHelm_Launcher_x64

# 查看系统日志
log show --predicate 'process == "BlockHelm_Launcher_x64"' --last 1h
```

---

## 贡献

欢迎提交跨平台兼容性改进。请遵循以下原则：

1. 使用 `OperatingSystem.IsWindows()` / `IsLinux()` / `IsMacOS()` 进行平台检测
2. 避免硬编码平台特定路径
3. 使用 `Path.Combine()` 构建路径
4. 测试所有支持的平台
5. 在 PR 中说明已测试的平台和版本

---

## 参考链接

- [.NET 8 跨平台支持](https://learn.microsoft.com/en-us/dotnet/core/install/)
- [WPF on Linux 状态](https://github.com/dotnet/wpf)
- [Minecraft Java 版本要求](https://minecraft.wiki/w/Tutorials/Java_Edition_1.21)
- [Adoptium JDK 下载](https://adoptium.net/)
