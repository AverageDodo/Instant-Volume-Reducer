# Changelog

This file should keep track of all major changes and decisions in this project. For decisions, also include the reasoning behind the decision. The newest changes should appear at the top of this file.

## Format

Each change log entry is represented by a block, formatted as follows:

```plain
### <YYYY-MM-DD HH:MM (UTC Time)>: <title>

#### Summary

<short summary>

(optional) #### <Reasoning>

---
```

### 2026-11-20 13:23: Project setup and technology decisions

#### Summary

Created the solution `src\InstantVolumeReducer.slnx` with the WinForms app project `src\InstantVolumeReducer\` (`net10.0-windows`, WinExe, nullable and implicit usings enabled) and the xUnit test project `src\InstantVolumeReducer.Tests\`, which references the app. Added the `NAudio.Wasapi` package to the app. The app currently starts a windowless `TrayApplicationContext` stub; the tray icon and the services follow in later steps. The initial implementation is developed on the branch `feature/initial-implementation`.

#### Reasoning

- **C# / .NET 10:** good access to native Windows APIs, quick to build a small UI with, and the .NET 10 SDK is already installed. Publishing as a self-contained single-file exe means users don't need .NET installed.
- **WinForms tray app:** the app needs only a notification-area icon, a context menu and a small settings dialog. WinForms is built in, lightweight and enough for that.
- **`NAudio.Wasapi` (MIT) for the Windows Core Audio API:** provides device listing and per-device master volume (`IAudioEndpointVolume`) without hand-writing COM interop. The audio code sits behind an `IAudioDeviceService` interface so it can be replaced or faked in tests.
- **`RegisterHotKey` for the global hotkey:** the standard Windows mechanism. Unlike a low-level keyboard hook, it is unlikely to be flagged by antivirus software, and it reports when another app already uses the combination. The default hotkey is `Ctrl+Alt+V`; users can change it.
- **JSON settings in `%APPDATA%\InstantVolumeReducer\`:** simple, human-readable and doesn't need admin rights.
- **Per-device reduction with relative and absolute modes:** each selected device has its own mode (relative: reduce to X % of the current level; absolute: set to a fixed X %) and amount, because different devices (e.g. speakers vs. headphones) often need different levels. Newly selected devices start with a default of relative, 20 %.
- **Output devices only:** controls the master volume of selected playback devices. Per-app volume and microphones are out of scope for now.

---
