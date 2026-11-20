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

### 2026-11-20 13:33: Global hotkey manager

#### Summary

Added the hotkey layer in `src\InstantVolumeReducer\Hotkeys\`:

- `HotkeyBinding`: an immutable value of `HotkeyModifiers` (Ctrl, Alt, Shift, Win) plus a WinForms `Keys` key. `TryParse` and `ToString` round-trip the settings text (`Ctrl+Alt+V`, `Ctrl+Shift+F12`, `F13`). It also has `IsValid`, `NativeModifiers`/`VirtualKey` for `RegisterHotKey`, `Default` (`Ctrl+Alt+V`) and `FromKeyData`/`FromKeyEventArgs` for the hotkey capture box.
- `HotkeyManager`: registers one global hotkey through `RegisterHotKey` on a hidden message-only window and raises `Pressed` on `WM_HOTKEY`. `Register(binding)` returns a `HotkeyRegistrationResult` (`Registered`, `InvalidBinding`, `AlreadyInUse`, `Failed`, plus a message for the user). `Unregister()` and `Dispose()` release the hotkey.
- 254 new tests (310 in total): parsing, formatting, a round-trip over every usable key, the validity rule, `MOD_*` mapping, building from key events, and real registration with unused combinations (Ctrl+Alt+Shift+F23/F24).
- The test project now sets `UseWindowsForms` so tests can use `Keys` and `KeyEventArgs`.

#### Reasoning

- **`RegisterHotKey` instead of a low-level keyboard hook:** this is the documented way to get a system-wide hotkey, it costs nothing between presses, and antivirus tools don't treat it as a keylogger. Its limitation, that a combination another app already registered can't be used, is reported to the user as `AlreadyInUse`.
- **`MOD_NOREPEAT` is always set,** so holding the keys down toggles only once.
- **Message-only window (`HWND_MESSAGE`):** it receives `WM_HOTKEY` without any visible or enumerable window, and doesn't need the tray icon to exist.
- **Re-registering keeps the old hotkey on failure:** if the new combination can't be registered, the previous one is registered again. A failed change in the settings dialog then never leaves the app without a working hotkey.
- **Validity rule:** at least one modifier is required, so a normal key like `V` can't be taken away from every app by accident. F13–F24 and media/volume keys may be used alone because nobody types them by accident. Modifier keys alone and mouse buttons are never valid. `TryParse` only accepts valid bindings, so a hand-edited settings file can't register something unusable.
- **Text format:** modifiers are always written in the order Ctrl, Alt, Shift, Win, and the key last. A few keys get friendlier names (`1` instead of `D1`, `Enter`, `PageUp`/`PageDown`, `Backspace`, `CapsLock`, `ScrollLock`, `PrintScreen`). Parsing is case-insensitive, ignores spaces and also accepts `Control`, `Windows` and every `Keys` enum name.
- **Win key in the capture box:** WinForms doesn't report the Windows key as a modifier in `KeyEventArgs`, so `FromKeyData` takes it as a separate flag that the capture box (step 6) sets from the keyboard state.
- **`DllImport` instead of `LibraryImport`:** `LibraryImport` would require enabling unsafe code in the project for two simple calls.

---

### 2026-11-20 13:29: Volume toggle service with per-device reduction

#### Summary

Added the toggle logic and the models it uses:

- `Settings\AppSettings.cs`: `AppSettings` (hotkey text, per-device list, default mode and amount, start with Windows, notifications), `DeviceReductionConfig` (`DeviceId`, `LastKnownName`, `Enabled`, `Mode`, `Amount` 0–100) and the `ReductionMode` enum (Relative, Absolute). The settings are JSON-serializable; loading and saving follow in a later step.
- `Core\ReductionState.cs`: an immutable, JSON-serializable record of the reduced devices with their original and applied volume. `IsReduced` is derived from the device list.
- `Core\ReductionCalculator.cs`: computes the reduced volume for a mode and amount.
- `Core\VolumeToggleService.cs`: `Toggle()`, `Reduce()`, `Restore()`, `UpdateDeviceConfigs(...)`, `State`, `IsReduced` and a `StateChanged` event. It can be constructed from a saved `ReductionState` for crash recovery.
- 48 new unit tests using an in-memory fake device service (56 tests in total).

#### Reasoning

- **Absolute mode never raises the volume:** if a device is already quieter than the absolute target, it stays at its current level. A "reduce" hotkey that makes something louder would be surprising.
- **Restore always returns to the saved original volume,** even if the volume was changed by hand while reduced. This is the simple, predictable behaviour from the plan; respecting manual changes can be added later as an option.
- **Unavailable devices are skipped:** devices that are unplugged, or whose volume can't be read or set, are not reduced. If no device could be reduced, the app stays not reduced, so the next press tries to reduce again instead of doing a no-op "restore". When restoring, devices that are gone are skipped and forgotten. Downside: if such a device is plugged back in, it keeps the reduced level that Windows remembers for it.
- **Changing the selection while reduced:** devices that are deselected or removed are restored right away, so they can't get stuck at a reduced level. Mode and amount changes, and newly enabled devices, take effect on the next reduction. Re-applying a different level mid-reduction would require deciding how it relates to manual changes, and isn't needed for the toggle use case.
- **The service keeps its own copy of the configs** (enabled entries only, first entry wins for duplicate IDs, compared case-insensitively). The settings dialog can then edit its objects freely without affecting a reduction in progress.
- **Persistence through the `StateChanged` event:** instead of a separate storage interface, the event fires after every change, and the subscriber can read `State` and save it. One mechanism serves both crash recovery and the tray icon update.
- **Single-threaded by design:** the hotkey, tray menu and device-change events all arrive on the UI thread, so no locking is needed.

---

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
