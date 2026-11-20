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

### 2026-11-20 13:26: Core Audio device service

#### Summary

Added the audio layer in `src\InstantVolumeReducer\Audio\`:

- `IAudioDeviceService` with `GetOutputDevices()`, `IsDeviceAvailable(id)`, `TryGetVolume(id, out volume)`, `TrySetVolume(id, volume)` and a `DevicesChanged` event, plus the `AudioDeviceInfo(Id, Name, IsActive)` record.
- `CoreAudioDeviceService`, the NAudio implementation (`MMDeviceEnumerator`, `AudioEndpointVolume.MasterVolumeLevelScalar`).
- `VolumeLevel.Clamp`, which keeps volume scalars in the 0.0–1.0 range, with unit tests.

#### Reasoning

- **`Try…` methods instead of exceptions:** devices can be unplugged or disabled at any moment, so a missing device is a normal case, not an error. The toggle logic just checks the return value. Only device-related exceptions (COM, invalid operation, argument, access) are caught; anything else is still a bug and propagates.
- **Devices are addressed by their endpoint ID string:** the ID stays stable across renames and reconnects. It's the key the settings and saved reduction state use.
- **Volumes are `float` scalars from 0.0 to 1.0:** this matches Core Audio and the Windows volume slider. Values are clamped on read and write, and NaN becomes 0.
- **The device list includes active and unplugged devices,** with an `IsActive` flag. That way the settings dialog can show known but disconnected devices. Disabled and removed endpoints are left out because they can't be used.
- **`DevicesChanged` is never raised on the Windows audio notification thread:** that thread holds an internal lock and must not call back into the audio stack. The event is posted to the synchronization context captured when the service is created (the UI thread), or to the thread pool if there is none. Handlers can therefore safely re-query devices.
- **Small interface, no `IDisposable` on it:** the toggle service only needs the methods above and can be unit-tested with a simple fake. The owner of `CoreAudioDeviceService` (the tray app) disposes it.

---

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
