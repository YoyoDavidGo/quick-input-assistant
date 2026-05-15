**English** | [简体中文](./README.md)

# QuickInputAssistant

A WinUI 3 floating-panel utility that intercepts 14 `Alt+` global hotkeys via low-level keyboard hook and instantly outputs your bound text into the focused window. Perfect for expense reports, ticketing systems, or any repetitive form filling.

<p align="center">
  <img src="icon.png" width="120" />
</p>

## Features

- **14 global hotkeys** — `Alt+1~6` / `Q-R` / `A-F`, bind any text per key and emit it instantly to the focused window
- **Smart date key `Alt+Q`** — single-click outputs today's date (YY/MM/DD); double-click undoes and emits +1 day
- **4 named presets** — switch sets of bindings with one click; each set stored independently and encrypted
- **Inline edit** — right-click a keycap → edit in place → any left-click confirms / any right-click cancels
- **3 themes** — Dark / Light / Follow system (inverted, for contrast against your wallpaper)
- **Lightweight overlay** — always-on-top, true transparent background, draggable, 3 view modes (capsule / keyboard / list)
- **Auto-start toggle** — one click in the settings menu (uses HKCU registry, no admin needed)
- **Local-only & encrypted** — all bindings stored locally with DPAPI

## Quick start

1. Download `QuickInputAssistant_Setup.exe` from [Releases](../../releases)
2. Double-click to install
3. In any application, press `Alt+1` to emit the bound text

## Build from source

```pwsh
.\build-release.cmd
```

Output: `dist\QuickInputAssistant_Setup.exe` (single-file installer, ~62 MB, includes .NET runtime).

## Tech stack

- **WinUI 3** / Windows App SDK 1.5 (unpackaged, self-contained)
- **.NET 8**, x64
- **WinUIEx** (transparent backdrop), **H.NotifyIcon.WinUI** (tray), **Serilog**

## Requirements

Windows 10 19041+ / Windows 11, x64

---

Built with [Claude Code](https://claude.com/claude-code).
