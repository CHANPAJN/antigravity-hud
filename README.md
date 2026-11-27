# Antigravity HUD

**Native Windows floating HUD & local token ledger for Google Antigravity.**

English | [简体中文](README.zh-CN.md)

[![Platform: Windows 10/11](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4?style=flat-square)](https://github.com/CHANPAJN/antigravity-hud)
[![License: GPL-3.0-only](https://img.shields.io/badge/License-GPL--3.0--only-2563EB?style=flat-square)](LICENSE)
[![Zero Dependencies](https://img.shields.io/badge/Dependencies-0%20(Built--in%20csc.exe)-10B981?style=flat-square)](#quick-start)

> **Windows Only** — Built specifically for **Windows 10 / 11** with pure C# WinForms + Win32 Layered Windows (`UpdateLayeredWindow`). Zero Electron, zero Node.js, zero third-party packages, and zero client file patching.

<p align="center">
  <img src="docs/preview-bar.png" alt="Floating Taskbar Capsule" width="320" />
</p>

<p align="center">
  <img src="docs/preview-hud.png" alt="Window HUD View" width="48%" />
  &nbsp;
  <img src="docs/preview-ledger.png" alt="Global Ledger View" width="48%" />
</p>

---

## Features

- **Floating Capsule Bar (Smart Free-Space Placement + Long-Press to Move)**
  - **Smart Auto-Placement**: detects Windows 11 centered vs. left-aligned taskbars and Widgets state — prefers the open **left taskbar slot** on Win11 centered taskbars, uses the right slot on Win10/left-aligned taskbars, and automatically floats cleanly just above the taskbar on compact laptop screens so it never covers your app icons or system tray.
  - **Click** to toggle the detailed HUD & ledger panel without stealing focus from Antigravity.
  - **Long-press (≥ 260 ms) or drag** to freely move the capsule anywhere across single or multiple monitors.
  - **Smart Edge & Taskbar Snapping**: magnetically snaps to screen edges and the Windows taskbar centerline (hold `Alt` while dragging for pixel-level fine placement).
  - **Drift-Free Anchoring**: remembers left/right screen alignment in `%LOCALAPPDATA%\AntigravityHUD\config.json` so dynamic capsule width changes never shift off-screen.
  - **Smart Popup Placement**: automatically opens above the bar near the taskbar, flips below when placed near the top of a display, and stays clamped inside the active monitor's working area.
  - **Lock & Reset**: right-click the capsule anytime to lock position against accidental drags or reset back to the default taskbar slot.
- **Window HUD (Active Session Telemetry)**
  - Tracks the active Google Antigravity / Antigravity Insiders / Antigravity CLI window automatically via lightweight Win32 foreground hooks.
  - Context window usage ring (`Auto` / `256K` / `1M` scale), prompt cache hit rate, live generation speed (`t/s`), reasoning depth badge, context breakdown (`Tool Outputs`, `Thinking & Replies`, `System Base`, `User Prompts`), turn-by-turn history, and subagent inspection.
- **Global Ledger (Offline Multi-Day Analytics)**
  - Aggregates local transcript logs across `Today`, `Yesterday`, `7 Days`, `30 Days`, and `All` views.
  - Interactive 7-day bar chart, model breakdown with click-to-drill-down diagnostics, conversation rankings (including subagent totals), and top tool call statistics.
- **Bilingual UI (`EN` / `中文`)**
  - Automatically follows Windows UI language on first launch and switches live between English and Simplified Chinese with one click in the top bar or context menu.
- **Optional Memory Helper (Opt-In Only)**
  - Displays read-only Antigravity memory footprint (`WorkingSet`).
  - Manual **Trim / Clean** and **Auto Guard** are **disabled by default** — never runs automatic cleanup unless explicitly toggled on by the user.

---

## Quick Start

### Option 1: Build & Install from Source (Zero External Dependencies)

Every Windows 10 / 11 machine already ships with the built-in .NET Framework 4.x compiler (`csc.exe`). You do **not** need Visual Studio, .NET SDK, or Node.js.

```powershell
git clone https://github.com/CHANPAJN/antigravity-hud.git
cd antigravity-hud

# Compile locally (~1 second) and install to %LOCALAPPDATA%\AntigravityHUD
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1

# Optional: also enable launch at Windows login (opt-in)
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -Autostart
```

Or simply double-click `build.cmd` to compile a standalone portable `dist\AntigravityHUD.exe` (~215 KB) and run it directly.

### Option 2: Prebuilt Release

Download `AntigravityHUD.exe` and `SHA256SUMS.txt` from [GitHub Releases](https://github.com/CHANPAJN/antigravity-hud/releases) (built directly from this repository on GitHub-hosted `windows-latest` runners).

### Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

Cleanly closes the running instance, removes the optional `HKCU\...\Run` entry, Start Menu shortcut, binary, and config file (pass `-KeepConfig` to preserve your saved position and preferences).

---

## Controls & Gestures

| Action | Behavior |
| --- | --- |
| **Left-click capsule** | Expand / collapse the HUD & Global Ledger popup |
| **Long-press (≥ 260 ms) & drag capsule** | Freely move to any position on any monitor (with edge & taskbar snap) |
| **Hold `Alt` while dragging** | Temporarily disable magnetic snapping for exact placement |
| **Right-click capsule** | Lock position, reset to taskbar default, switch `EN/中文`, trim memory, toggle autostart, exit |
| **Click `EN` / `ZH` in header** | Toggle English / Simplified Chinese immediately |
| **`Esc` or click outside popup** | Dismiss the expanded panel |

---

## How It Works & Operational Transparency

Antigravity HUD is designed to be auditable from top to bottom in plain C# source code:

1. **Pure Local Read-Only Log Parsing (Zero Network Calls)**:
   - Reads only local session transcript and annotation files under:
     - `%USERPROFILE%\.gemini\antigravity\brain\`
     - `%USERPROFILE%\.gemini\antigravity-insiders\brain\`
     - `%USERPROFILE%\.gemini\antigravity-cli\brain\`
   - Uses `mtime` + incremental byte-offset tracking so idle disk I/O stays near zero.
   - Makes **zero outbound HTTP/TCP connections**, never inspects browser cookies, OAuth tokens, or API keys, and **never modifies** Antigravity binaries or `app.asar`.
2. **Window Tracking & Fullscreen Auto-Hide**:
   - Uses standard Win32 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` and `GetWindowText` on Antigravity main windows (`Chrome_WidgetWin_1`) to switch the HUD automatically when you switch windows.
   - Automatically hides the floating bar only when an exclusive fullscreen application covers the **same monitor** hosting the capsule.
3. **Opt-In Side Effects Only**:
   - **Config file**: saved solely at `%LOCALAPPDATA%\AntigravityHUD\config.json` (position, monitor anchor, lock state, language, scale, auto-guard preference).
   - **Autostart**: disabled by default; written to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` only if you run `install.ps1 -Autostart` or check `Launch at Windows Login` in the menu.
   - **Memory Guard & Cache Clean**: `Auto Guard` defaults to **`Off`**. When manually clicked (or explicitly enabled), it only deletes files inside Antigravity's `tempmediaStorage` and `crashes` folders and calls standard Win32 `EmptyWorkingSet` on Antigravity processes. It **never terminates any process** and never touches conversation logs.
4. **Platform Boundary**:
   - **Windows 10 / 11 only**. macOS and Linux are intentionally not targeted because the capsule relies directly on Win32 layered windows (`WS_EX_LAYERED | WS_EX_NOACTIVATE`) and the Windows taskbar shell (`Shell_TrayWnd`).

---

## Verification & Self-Test

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\verify.ps1
```

Compiles from clean source via built-in `csc.exe`, verifies `config.json` persistence (`--test-config`), and renders offscreen English & Chinese demo previews (`--render-preview --demo`).

---

## License

Licensed under **GPL-3.0-only**. See [LICENSE](LICENSE) for the full text.
