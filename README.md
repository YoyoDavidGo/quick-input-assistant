<p align="center">
  <img src="./icon.png" alt="QuickInputAssistant" width="120">
</p>

<h1 align="center">QuickInputAssistant · 快捷输入助手</h1>

<p align="center">
  一个轻量级 Windows 桌面快捷输入浮窗工具，适合工单、报销、审批、客服等高频文本录入场景。
</p>

<p align="center">
  <a href="./README.en.md">English</a>
  &nbsp;·&nbsp;
  <strong>简体中文</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 8">
  <img src="https://img.shields.io/badge/UI-WinUI%203-2D7D9A?style=flat-square" alt="WinUI 3">
</p>

## 项目简介

QuickInputAssistant 是一个 WinUI 3 桌面浮窗工具，可以通过全局快捷键快速输出预设文本。它适合需要频繁填写固定内容的场景，例如报销说明、工单回复、审批备注、客服话术、售后记录等。

## 核心功能

- **14 个全局热键** — `Alt+1~6` / `Alt+Q~R` / `Alt+A~F`，每个键可绑定任意文本，一键输出到当前焦点窗口
- **智能日期键 `Alt+Q`** — 单击输出今天日期（YY/MM/DD），双击撤回上一次并改为 +1 天
- **4 套预设** — 可命名、可一键切换；每套绑定独立保存
- **极简绑定流** — 选择文本按相应的快捷键可以一键绑定或者右键键帽 → 直接内联编辑 → 任意位置左键确认 / 右键取消
- **三套主题** — 深色 / 浅色 / 跟随系统，适配不同桌面背景
- **轻量浮窗** — 始终置顶、透明背景、可拖动、三种视图模式（胶囊 / 键盘 / 列表）
- **开机自启** — 一键开关（HKCU 注册表，无需管理员）
- **数据安全** — 所有绑定内容仅保存在本地，并使用 DPAPI 加密存储

## 适合场景

- 工单系统中反复填写固定文本
- 报销、审批、售后记录等高频录入
- 客服、运维、办公人员常用短语输入
- 临时保存多组常用文本，并在不同场景中快速切换
- 需要在任意软件中快速输入模板内容的 Windows 桌面用户

## 界面预览

<div align="center">

<table width="90%">
  <tr>
    <td width="50%" align="center">
      <img src="./docs/assets/QIA-input.png" alt="输入模式" width="95%">
      <br>
      <sub>输入模式</sub>
    </td>
    <td width="50%" align="center">
      <img src="./docs/assets/QIA-copy.png" alt="复制模式" width="95%">
      <br>
      <sub>复制模式</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" align="center">
      <img src="./docs/assets/QIA-light-theme.png" alt="浅色主题" width="95%">
      <br>
      <sub>浅色主题</sub>
    </td>
    <td width="50%" align="center">
      <img src="./docs/assets/QIA-menu.png" alt="托盘菜单" width="95%">
      <br>
      <sub>托盘菜单</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" align="center">
      <img src="./docs/assets/QIA-capsule-mode.png" alt="胶囊模式" width="95%">
      <br>
      <sub>胶囊模式</sub>
    </td>
    <td width="50%" align="center">
      <img src="./docs/assets/QIA-list-mode.png" alt="列表模式" width="95%">
      <br>
      <sub>列表模式</sub>
    </td>
  </tr>
</table>

</div>

## 快速开始

1. 前往 [Releases](https://github.com/YoyoDavidGo/quick-input-assistant/releases) 下载 `QuickInputAssistant_Setup.exe`
2. 双击安装
3. 启动后，在任意应用中按 `Alt+1` 输出绑定内容

## 自行构建

```pwsh
.\build-release.cmd
```

产物：`dist\QuickInputAssistant_Setup.exe`（单文件安装器，约 62 MB，含 .NET Runtime）。

## 技术栈

- **WinUI 3** / Windows App SDK 1.5（unpackaged，自含模式）
- **.NET 8**, x64
- **WinUIEx**（透明 backdrop）
- **H.NotifyIcon.WinUI**（托盘）
- **Serilog**（日志）

## 系统要求

Windows 10 19041+ / Windows 11，x64

---

<p align="center">
  <sub>Built with <a href="https://claude.com/claude-code">Claude Code</a></sub>
</p>
