# 快捷输入助手 — 开发启动 & 打包指南

## 环境要求

| 工具 | 最低版本 | 下载 |
|------|---------|------|
| Windows | 10 Build 19041 / Windows 11 | — |
| .NET SDK | 8.0 | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Windows App SDK | 1.5 | 随 NuGet 自动恢复 |
| Visual Studio (可选) | 2022 17.8+ | https://visualstudio.microsoft.com |

> 只用命令行不需要装 Visual Studio，装 .NET SDK 即可。

---

## 一、临时拉起（开发调试）

### 1. 克隆 / 拿到源码

```powershell
# 如果是 git 仓库
git clone <repo-url>
cd quick_input_claude_desktop

# 或者直接进项目目录
cd E:\AI\software\quick_input_claude\quick_input_claude_desktop
```

### 2. 还原依赖

```powershell
dotnet restore QuickInputAssistant\QuickInputAssistant.csproj
```

### 3. 编译 & 运行（一步到位）

```powershell
# Debug 模式（带日志、方便调试）
dotnet run --project QuickInputAssistant\QuickInputAssistant.csproj `
           -r win-x64 --no-self-contained

# Release 模式（性能更好）
dotnet run --project QuickInputAssistant\QuickInputAssistant.csproj `
           -c Release -r win-x64 --no-self-contained
```

### 4. 只编译，手动运行

```powershell
# 编译
dotnet build QuickInputAssistant\QuickInputAssistant.csproj `
             -c Release -r win-x64 --no-self-contained

# 编译产物路径
# QuickInputAssistant\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\QuickInputAssistant.exe

# 运行
.\QuickInputAssistant\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\QuickInputAssistant.exe
```

### 5. 杀掉旧进程再启动（改完代码后常用）

```powershell
Stop-Process -Name QuickInputAssistant -Force -ErrorAction SilentlyContinue
Start-Sleep 1
dotnet build QuickInputAssistant\QuickInputAssistant.csproj -c Release -r win-x64 --no-self-contained
.\QuickInputAssistant\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\QuickInputAssistant.exe
```

> 提示：把上面四行保存成 `restart.ps1` 放项目根目录，改代码后双击一键重启。

---

## 二、打包发布

打包目标是生成一个**自包含文件夹**（不需要用户额外安装 .NET），
直接把文件夹拷给别人就能用。

### 方案 A — 自包含文件夹（推荐）

```powershell
dotnet publish QuickInputAssistant\QuickInputAssistant.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o publish\win-x64
```

产物在 `publish\win-x64\`，把整个文件夹压缩发给用户即可。

### 方案 B — 单文件（体积更大，但更简洁）

```powershell
dotnet publish QuickInputAssistant\QuickInputAssistant.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o publish\single-file
```

> ⚠ WinUI3 有部分 C++ 运行库无法合并进单文件，最终产物仍有少量 dll。

### 方案 C — 依赖系统 .NET（体积最小，用户需自行安装 .NET 8）

```powershell
dotnet publish QuickInputAssistant\QuickInputAssistant.csproj `
    -c Release `
    -r win-x64 `
    --no-self-contained `
    -o publish\framework-dependent
```

---

## 三、打包成压缩包（可选）

编译完成后，用 PowerShell 打包成 zip：

```powershell
$version = "1.0.0"
$src     = "publish\win-x64"
$out     = "快捷输入助手_v$version.zip"

Compress-Archive -Path "$src\*" -DestinationPath $out -Force
Write-Host "打包完成: $out"
```

---

## 四、目录结构说明

```
quick_input_claude_desktop/
├── QuickInputAssistant/            ← 主项目
│   ├── App.xaml / App.xaml.cs      ← 应用入口、单实例、服务初始化
│   ├── MainWindow.xaml / .cs       ← 三态 UI (Capsule/Keyboard/List)
│   ├── Services/
│   │   ├── HotkeyService.cs        ← 低层键盘钩子，捕获 Alt+* 热键
│   │   ├── CoreService.cs          ← 业务核心：绑定 or 输出 分派
│   │   ├── ClipboardService.cs     ← 剪贴板备份 / 还原 / 轮询
│   │   ├── InputService.cs         ← SendInput 模拟键盘输出
│   │   ├── BindingStore.cs         ← DPAPI 加密持久化绑定数据
│   │   ├── DateKeyService.cs       ← Alt+Q 日期状态机（双击递增）
│   │   ├── StatusService.cs        ← 状态消息，3s 防抖复位
│   │   └── BlacklistService.cs     ← 读 blacklist.json，黑名单判断
│   ├── PInvoke/
│   │   ├── User32.cs               ← Win32 API 声明
│   │   ├── Structs.cs              ← INPUT / GUITHREADINFO 等结构体
│   │   └── Constants.cs            ← 虚拟键码、窗口样式常量
│   └── Models/
│       ├── StatusMessage.cs        ← 状态消息模型
│       └── BindingsData.cs         ← 绑定数据模型
├── BUILD.md                        ← 本文件
├── CLAUDE.md                       ← AI 编码规范
└── 快捷输入助手_部署包/             ← 已编译的部署包（含脚本和文档）
```

---

## 五、常见问题

### Q: `dotnet build` 报"文件被占用"
```powershell
Stop-Process -Name QuickInputAssistant -Force -ErrorAction SilentlyContinue
Start-Sleep 1
dotnet build ...  # 再试一次
```

### Q: 启动崩溃，没有任何错误提示
查看日志：
```powershell
Get-Content "$env:LOCALAPPDATA\QuickInputAssistant\logs\app-$(Get-Date -Format 'yyyyMMdd').log" | Select-Object -Last 30
```

### Q: 修改代码后热键不响应
低层键盘钩子需要重启生效，直接结束进程再重新启动：
```powershell
Stop-Process -Name QuickInputAssistant -Force -ErrorAction SilentlyContinue
.\QuickInputAssistant\bin\x64\Release\...\QuickInputAssistant.exe
```

### Q: 打包后在其他电脑运行闪退
- 确认用了 `--self-contained true`（含运行库）
- 目标机器需要 Windows 10 Build 19041 或更新版本
- 查看对方机器上的日志文件确认错误原因

---

## 六、一键脚本（复制即用）

> **Windows 用户注意**：双击 `.ps1` 会用记事本打开，不会运行。
> 请直接双击 **`restart.bat`** 或 **`pack.bat`**（已在根目录创建好）。

将以下内容保存为项目根目录的 `restart.ps1`：

```powershell
# restart.ps1 — 改完代码后一键重新编译并启动

$proj = "QuickInputAssistant\QuickInputAssistant.csproj"
$exe  = "QuickInputAssistant\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\QuickInputAssistant.exe"

Stop-Process -Name QuickInputAssistant -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
dotnet build $proj -c Release -r win-x64 --no-self-contained 2>&1 | Select-Object -Last 5
if ($LASTEXITCODE -eq 0) { Start-Process $exe } else { Write-Host "编译失败" -ForegroundColor Red }
```

将以下内容保存为 `pack.ps1`，一键打包：

```powershell
# pack.ps1 — 一键发布打包

$proj    = "QuickInputAssistant\QuickInputAssistant.csproj"
$out     = "publish\win-x64"
$zipName = "快捷输入助手_v1.0.0.zip"

dotnet publish $proj -c Release -r win-x64 --self-contained true -o $out
Compress-Archive -Path "$out\*" -DestinationPath $zipName -Force
Write-Host "完成: $zipName" -ForegroundColor Green
```
