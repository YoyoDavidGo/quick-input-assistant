[English](./README.en.md) | **简体中文**

# 快捷输入助手 · QuickInputAssistant

WinUI 3 桌面浮窗工具：低层钩子拦截 14 个 `Alt+` 组合键，把绑定的文本一键输出到当前焦点窗口。日常报销/工单录入提效神器。

<p align="center">
  <img src="icon.png" width="120" />
</p>

## 核心功能

- **14 个全局热键** — `Alt+1~6` / `Q-R` / `A-F`，每个键绑定任意文本，瞬间输出到当前焦点
- **智能日期键 `Alt+Q`** — 单击输出今天日期 (YY/MM/DD)，双击撤回上一次并改为 +1 天
- **4 套预设** — 可命名、可一键切换；每套绑定独立加密保存
- **极简绑定流** — 右键键帽 → 直接内联编辑 → 任意位置左键确认 / 右键取消
- **三套主题** — 深色 / 浅色 / 跟随系统（反向，便于反差使用）
- **轻量浮窗** — 始终置顶、真透明背景、可拖动、三态切换（胶囊 / 键盘 / 列表）
- **开机自启** — 一键开关（HKCU 注册表，无需管理员）
- **数据安全** — 所有绑定本地 DPAPI 加密存储

## 快速开始

1. 下载 [Releases](../../releases) 中的 `QuickInputAssistant_Setup.exe`
2. 双击安装
3. 启动后任意应用中按 `Alt+1` 即可输出绑定内容

## 自行构建

```pwsh
.\build-release.cmd
```

产物：`dist\QuickInputAssistant_Setup.exe`（单文件安装器，约 62 MB，含 .NET runtime）。

## 技术栈

- **WinUI 3** / Windows App SDK 1.5（unpackaged，自含模式）
- **.NET 8**, x64
- **WinUIEx**（透明 backdrop）、**H.NotifyIcon.WinUI**（托盘）、**Serilog**

## 系统要求

Windows 10 19041+ / Windows 11，x64

---

Built with [Claude Code](https://claude.com/claude-code).
