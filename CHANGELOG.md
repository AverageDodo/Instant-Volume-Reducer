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

### 2026-11-27 12:30: Experimental "keep the indicator ready" option against HDR black flashes

#### Summary

- New setting `OverlaySettings.KeepWindowReady`, off by default; missing in older settings files, which means off. In the settings dialog it's "Keep the indicator ready to avoid black flashes in HDR games (experimental)", with a tooltip explaining the trade-off.
- When on, the overlay window stays shown whenever the indicator is enabled.
  - It's drawn with alpha 1/255 (`OverlayLayout.IdleAlpha`) while the volume isn't reduced, and with the opacity setting while it is.
  - Toggling only changes the alpha (`UpdateLayeredWindow`), never shows or hides the window.
  - The topmost and active-monitor timer keeps running, so the window is already on the right monitor when it becomes visible.

#### Reasoning

- **The problem:** showing a new window over a fullscreen game makes Windows switch from sending the game's frames straight to the monitor to composing the desktop. On some HDR monitors, that switch causes a short black screen. Alt-Tab and system pop-ups cause the same switch.
- **A window that's always present** means the switch doesn't happen on toggle.
- **Alpha 1 instead of 0:** a fully transparent window may be treated as absent, which would bring the switch back when it becomes visible. 1/255 is invisible to the eye.
- **Experimental and off by default:** whether it removes the flash depends on the GPU, driver and monitor, and it has to be confirmed on real HDR setups. The trade-off is that a game under the indicator is always composed: possibly about one frame of extra delay, and on some setups no variable refresh rate in windowed mode.
- **Fallback if it doesn't help:** an option not to show the indicator over fullscreen apps.

---

### 2026-11-27 12:13: On-screen indicator while the volume is reduced

#### Summary

- New optional **on-screen indicator** (`UI\ReductionOverlay.cs`): an orange speaker badge on a dark rounded square, shown in a screen corner while the volume is reduced. It follows every state change, including reductions that move to a new default device.
- Settings (`AppSettings.Overlay`, off by default; missing in older settings files, which means off): corner, size (32/48/64 px at 100 % scaling), opacity (20–100 %) and display (main monitor or the monitor with the active window). Invalid values in the file fall back to defaults; opacity is clamped.
- The settings dialog has a new "On-screen indicator" group.
- `UI\OverlayLayout.cs`: the placement maths (corner inside the working area, margin and size scaled by the monitor's DPI, opacity to alpha), unit-tested.
- The speaker drawing moved into `TrayIcons.DrawGlyph`, shared by the tray icons and the badge.
- The app now runs with per-monitor DPI awareness (`ApplicationHighDpiMode` `PerMonitorV2`).

#### Reasoning

- **Per-pixel-alpha layered window (`UpdateLayeredWindow`) on a bare `NativeWindow`:**
  - It gives smooth, semi-transparent edges, which a `TransparencyKey` form can't.
  - A `NativeWindow` avoids WinForms resizing or moving the window on DPI changes.
  - It's click-through (`WS_EX_TRANSPARENT`, `HTTRANSPARENT`) and never takes the focus (`WS_EX_NOACTIVATE`).
  - `WS_EX_TOOLWINDOW` keeps it out of the taskbar and Alt+Tab.
- **Re-asserting topmost every 2 seconds while shown:** other topmost windows can cover it. The same timer moves the badge to the active window's monitor in that mode, and display changes re-place it right away. Hidden, it costs nothing.
- **Working area instead of full monitor bounds:** the badge never covers the taskbar.
- **Per-monitor DPI awareness:** the badge is sized and placed in physical pixels per monitor. It also keeps the settings dialog sharp on mixed-scaling setups (WinForms rescales it with `AutoScaleMode.Dpi`).
- **Off by default:** consistent with notifications being off by default; the tray icon already shows the state.
- **Known limitation, deferred:** exclusive-fullscreen apps draw over all normal windows, so the badge isn't visible there. Working around that would need hooking into the app's rendering.

---

### 2026-11-20 14:28: CI, automatic releases and versioning

#### Summary

- **Branching model:** `feature branch → develop → main`, through pull requests only.
- **Versioning:** SemVer. `<Version>` in `src\InstantVolumeReducer\InstantVolumeReducer.csproj` is the only place the version is set. A version with a pre-release part (`0.3.0-beta.1`) becomes a GitHub pre-release.
- `.github/workflows/ci.yml`: builds and tests every push to `develop` and every pull request into `develop` or `main`. Pull requests into `main` also get a release check: the source branch must be `develop`, the version must not be lower than the latest release, and an already-released version gets a warning.
- `.github/workflows/release.yml`: on every push to `main`, reads the version.
  - If tag `v<Version>` doesn't exist and the version is higher than every release, it tests, publishes, creates the tag on that commit and a GitHub release. The release has `InstantVolumeReducer-v<Version>-win-x64.exe`, a `.sha256` file and generated notes.
  - An existing tag means nothing to release; a lower version fails the run.
- `.github/scripts/Get-ReleaseInfo.ps1`: the version and tag logic used by both workflows, with SemVer precedence including pre-releases. Works on PowerShell 7 and Windows PowerShell 5.1.
- The app shows its version in the tray menu header and the settings window title (`AppInfo.Version`, without the `+commit` build metadata).
- README: Download section and a "Branches, CI and releases" section.

#### Reasoning

- **Version in the project file instead of a hand-made tag:** releases must only come from merges into `main`, so nobody creates tags by hand. The version therefore has to be in the code, raised on `develop` as part of preparing a release. The workflow turns it into the tag, so the tag, the exe's version and the version shown in the app always match.
- **Releasing on push to `main`, skipping already-released versions:** every merge into `main` is a release candidate, but a merge that doesn't raise the version (docs, CI tweaks) shouldn't fail or release anything.
- **Tag created by the release itself, after the tests:** `gh release create --target <commit>` creates the tag and the release together, only after the build and tests have passed. A failed run leaves no tag that would block a retry.
- **Release check on pull requests into `main`:** it catches a forgotten or lowered version before merging. Branch protection isn't available for private repositories on the Free plan, so the checks show red or green but can't block a merge.
- **Merge commits from `develop` into `main`:** squash or rebase merges rewrite the commits, so `develop` and `main` would diverge and conflict later.
- **Windows runner for building, Linux for the release check:** WinForms needs Windows. The release check only reads files and tags, and Linux minutes cost half as much on private repositories.
- **Raw exe plus checksum, no zip:** users can run the download directly; the checksum lets them verify it. Code signing was left out because it needs a paid certificate.
- **Notes generated by GitHub:** `CHANGELOG.md` is a dated decision log, not organised by version, so the release notes come from the merged pull requests instead.

---

### 2026-11-20 14:12: Follow the Windows default output device

#### Summary

- New optional **Windows default device** row, pinned at the top of the settings dialog's device table, with its own tick, mode and amount (`AppSettings.WindowsDefaultDevice`; missing in older settings files, which means off).
- When ticked, the hotkey also reduces whichever device is the Windows default output device at that moment.
- If the default changes while the volume is reduced, `VolumeToggleService.HandleDefaultDeviceChanged` moves the reduction: devices reduced only because they were the default are restored, and the new default is reduced. The tray tooltip updates, and a notification names the new device if notifications are on.
- `IAudioDeviceService` gained `GetDefaultOutputDeviceId()` and a `DefaultDeviceChanged` event. `CoreAudioDeviceService` listens to NAudio's `DefaultDeviceChanged` for render devices, Console role, raises it on the UI thread and only when the device actually differs.
- `ReducedDevice` gained `ViaWindowsDefault` (defaults to `false`, so older state files still load). Crash recovery is unchanged because the state always lists concrete devices.
- The settings dialog keeps the default row's "Currently: …" status live while it's open; Refresh updates it too.

#### Reasoning

- **A separate setting, not a magic device ID in `Devices`:** keeps the per-device list purely about real endpoints and makes the JSON self-explanatory.
- **A device selected by name wins over the default row:** naming a device is the more specific choice, so it uses its own settings, is reduced once, and stays reduced when the default moves away. If it's the new default but wasn't reduced yet (e.g. it was unplugged at the time), it's reduced with its own settings.
- **Console role only:** Windows' Sound settings and the taskbar volume menu set the Console and Multimedia defaults together. The Communications default (for calls) is a separate choice, so it's left out for now.
- **Previous default that can't be restored stays in the state:** unplugging the default device is the most common reason the default changes, and at that moment its volume can't be set. Keeping it means it's restored on the final restore, provided it's connected again by then, instead of losing its original volume.
- **The default row being newly ticked while reduced takes effect on the next reduction:** the same rule as newly ticked named devices.
- **Dedupe in the audio service:** Windows can report the same default more than once, which would otherwise trigger needless state writes.

---

### 2026-11-20 14:04: Select all devices, notifications off by default

#### Summary

- Settings dialog: a **Select all** checkbox above the device table. It ticks every device (connected or not), or unticks them all when every device is already ticked, and shows a partial tick when only some are. Devices ticked for the first time get the defaults, as with ticking a single row. The logic lives in `DeviceTableModel.GetSelectAllState` / `ToggleAll` and is unit-tested.
- `AppSettings.ShowNotifications` now defaults to `false`. Warnings (a device that couldn't be restored, nothing to reduce, crash recovery) are still always shown.

#### Reasoning

- **Checkbox above the table instead of in the column header:** a DataGridView header checkbox needs custom painting and hit-testing, and isn't reachable by keyboard or screen readers. A normal three-state checkbox gets all of that for free.
- **Click from the partial state selects all:** that's what "Select all" promises, and it matches common file-manager and mail-client behaviour. WinForms' default (`AutoCheck`) would clear the box instead, so the click is handled manually.
- **Notifications off by default:** requested by the user. The tray icon's colour already shows the state, so a balloon on every toggle is noise for most people.

---

### 2026-11-20 13:55: Publish profile, app icon and README

#### Summary

- `src\InstantVolumeReducer\Properties\PublishProfiles\win-x64.pubxml`: self-contained, single-file Release build for win-x64 (native libraries bundled, no trimming, no compression, no PDB). Publish with `dotnet publish src\InstantVolumeReducer\InstantVolumeReducer.csproj -p:PublishProfile=win-x64`. The output is `src\InstantVolumeReducer\bin\publish\win-x64\InstantVolumeReducer.exe` (~112 MB), which is git-ignored.
- Assembly metadata: Product "Instant Volume Reducer", a description, and Version 0.1.0.
- App icon: `src\InstantVolumeReducer\Resources\AppIcon.ico` (16, 24, 32, 48, 256 px), generated by `src\tools\New-AppIcon.ps1`, which draws the same blue speaker as the tray icon (`UI\TrayIcons.cs`). It's set as `ApplicationIcon`.
- `.gitignore`: the publish-profile exception pointed at another project's path (`src/LandtagTool.App/...`) and now points at `src/InstantVolumeReducer/Properties/PublishProfiles/*.pubxml`, so the profile is tracked.
- `README.md`: what the app does, requirements, usage, the settings dialog, files and crash recovery, build/test/publish commands, known limitations.

#### Reasoning

- **Self-contained single file:** users get one exe that runs without installing .NET, which suits a small tool that's copied somewhere and started at sign-in.
- **No trimming:** WinForms doesn't support trimming, and trimming could break NAudio's COM interop.
- **No single-file compression:** measured on the published app, compression shrinks the exe from ~112 MB to ~50 MB but raises the running process's private memory from ~12 MB to ~50 MB, because the bundled assemblies are unpacked into memory. A tray app runs all day, so memory matters more than download size.
- **Generated icon:** the icon file is produced from the same shapes as the tray icon, so nothing is downloaded and the look stays consistent. The generated `.ico` is committed, so builds don't need to run the script. The script has to be kept in sync by hand if the tray icon's design changes.
- **No CI workflow yet:** it was optional in the plan and not requested.

---

### 2026-11-20 13:52: Start with Windows, toggle notifications and crash recovery

#### Summary

- **Start with Windows:** `Settings\AutostartManager.cs` writes or removes the value `InstantVolumeReducer` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, set to the quoted path of the running exe. It's applied whenever settings are saved and once at startup, which also repoints the entry if the exe was moved. Registry access goes through `IAutostartRegistry` (`RunKeyRegistry` for the real key, an in-memory fake in tests), and registry errors produce a tray warning instead of an exception.
- **Toggle notifications:** with "Show a notification…" on, each toggle shows a short tray balloon ("Volume reduced on 2 devices.", "Volume restored."), including devices that were skipped because they aren't connected. The texts live in `UI\ToggleMessages.cs`. `VolumeToggleService.Restore()` now returns a `RestoreResult` (restored/skipped counts), and `SelectedDeviceCount` was added for the "skipped" count when reducing.
- **Crash recovery:** `Core\ReductionStateStore.cs` keeps the current reduction in `%APPDATA%\InstantVolumeReducer\state.json` while the volume is reduced and deletes the file once nothing is reduced (after a restore, a normal exit and Windows sign-out). At startup, `Core\CrashRecovery.cs` restores the saved original volumes automatically if the file still exists, shows a notification, and deletes the file. A corrupt state file is moved to `state.json.bak` and reported; it never crashes the app.
- The atomic temp-file-then-replace write from `SettingsStore` moved to `Settings\AtomicFile.cs`, so the settings and the state file share it.

#### Reasoning

- **The setting is the source of truth for autostart.** Applying it at startup in both directions keeps the Run key correct after the exe was moved, and removes a stale entry if the setting is off. One side effect: running a different copy of the exe (e.g. a debug build) with the setting on repoints the entry to that copy. A per-user Run key value needs no admin rights and is the standard way for a tray app to start at sign-in.
- **Recovery restores automatically instead of asking.** After a crash the volume is still reduced, so the user almost certainly wants it back. Asking would add a dialog at sign-in for a decision that has only one sensible answer. Recovery uses the same rules as a normal restore: devices that aren't connected are skipped and forgotten, so a later restore can't unexpectedly change a device the user has used since.
- **Notifications are shown even when turned off in two cases:** the startup recovery (the user should know why their volume changed) and a restore where devices were skipped (those devices lost their original volume). A reduce that skips unplugged devices only notifies when notifications are on, since that is a common, harmless situation (e.g. headphones selected but not plugged in).
- **The state file only exists while something is reduced,** so its presence at startup is exactly the "didn't exit normally while reduced" signal, and a normal session leaves nothing behind.

---

### 2026-11-20 13:45: Settings persistence and settings dialog

#### Summary

Settings are now saved to disk and can be changed in a settings dialog:

- `Settings\SettingsStore.cs` loads and saves `AppSettings` as indented JSON at `%APPDATA%\InstantVolumeReducer\settings.json` (the path can be injected for tests). A missing file gives defaults. A corrupt or unreadable file also gives defaults: a copy is kept as `settings.json.bak` and the tray shows a warning. Saving writes a temp file and then replaces the settings file. Loaded values are normalized: amounts clamped to 0–100, unknown modes set to the default, an invalid hotkey replaced by `Ctrl+Alt+V` (a valid one is written in its standard form), and device entries without an ID or with a repeated ID dropped.
- First run (no settings file): every active output device is selected with the defaults and the file is saved, the same behaviour step 5 had in memory. Devices connected later are **no longer selected automatically**. They appear in the dialog; only the saved names of known devices are kept up to date (`Settings\DeviceConfigUpdater.cs`).
- `UI\SettingsForm.cs`: a device table (Reduce · Device · Mode · Amount % · Status) listing connected devices plus saved devices that aren't connected (in grey), with a Refresh button. It also has defaults for newly selected devices, the hotkey box, "Start with Windows" and "Show a notification…" checkboxes (saved only; their behaviour comes in the next step), and OK/Cancel.
- `UI\DeviceTableModel.cs`: the dialog's non-UI logic (merging the device list with saved settings, giving a newly ticked device the defaults, turning rows back into settings), covered by unit tests.
- `UI\HotkeyCaptureBox.cs`: records a key combination, shows held modifiers (`Ctrl+Alt+…`), and rejects invalid combinations with a hint. Tab, Enter and Escape keep their dialog behaviour. Backspace/Delete returns to the hotkey the box started with.
- The tray menu's "Settings…" opens the dialog (only one at a time; a second click brings it to the front). OK saves the file, hands the device settings to the toggle service and updates the menu.
- 39 new tests (349 in total).

#### Reasoning

- **The dialog registers the new hotkey on OK:** if the combination is taken, the dialog stays open with the error, and `HotkeyManager` keeps the old hotkey registered. A failed change never leaves the app without a hotkey.
- **The hotkey is suspended while the hotkey box has focus:** a registered hotkey is delivered as `WM_HOTKEY` instead of as key presses, so without this the current hotkey couldn't be typed into the box (and pressing it would toggle the volume). It's registered again when the box loses focus or the dialog closes.
- **Keys are captured in `ProcessCmdKey`:** a read-only `TextBox` with shortcuts disabled swallows combinations like Ctrl+Shift+V or Ctrl+A before `KeyDown` fires.
- **Modeless dialog:** the tray menu and the hotkey keep working while settings are open, and the dialog shows in the taskbar because the app has no other window.
- **New devices aren't selected automatically:** with saved settings, which devices are reduced is the user's choice. Silently adding every new device (e.g. a Bluetooth headset) would change that behind their back.
- **Never-configured devices aren't saved:** only devices that were ticked at some point, or already saved, are written to the file, so it doesn't fill up with every device Windows has ever listed.
- **Relaxed JSON escaping:** the file stays readable and hand-editable (`"Ctrl+Alt+V"` instead of `"Ctrl+Alt+V"`, non-ASCII device names as they are).
- **An unreadable file is only overwritten after it was backed up:** if the backup copy fails, the app runs with defaults but doesn't save over the original.

---

### 2026-11-20 13:36: Tray icon and app wiring

#### Summary

The app now runs as a working tray app:

- `Program.cs`: one instance per Windows session (a named `Local\` mutex; a second launch exits quietly), then runs `TrayApplicationContext`.
- `TrayApplicationContext.cs` creates and connects `CoreAudioDeviceService`, `VolumeToggleService` and `HotkeyManager`, and registers the hotkey from the settings (`Ctrl+Alt+V` by default). If registration fails, a balloon message says why.
- Tray icon: the icon and tooltip show the state ("volume normal" / "volume reduced (2 devices)"). Double-click toggles. The menu has "Reduce volume"/"Restore volume" (showing the hotkey), a disabled "Settings…" (added in a later step) and "Exit".
- Until settings are saved to disk (next step), settings are kept in memory: every active output device is selected with the default reduction (relative, 20 %). Devices connected while the app runs are added the same way.
- On Exit, on `Application.ApplicationExit` and when Windows signs out or shuts down (`SystemEvents.SessionEnding`), a reduced volume is restored. The hotkey, tray icon and audio objects are then released, and the icon is hidden first so no ghost icon is left behind.
- `UI\TrayIcons.cs`: the two icons, drawn at runtime.

#### Reasoning

- **Icons drawn with GDI+ at runtime:** a blue speaker with two sound waves (normal) and an orange speaker with a downward arrow (reduced). Drawn at the system's small-icon size, they stay sharp at any DPI scaling, and no binary `.ico` files are needed in the repository. The colour and the shape both differ, so the state is visible without relying on colour alone. The unmanaged handle from `Bitmap.GetHicon` is copied into an owned `Icon` and freed with `DestroyIcon`, so nothing leaks.
- **Synchronization context installed up front:** the audio service raises `DevicesChanged` through the synchronization context it captures when created. WinForms only installs its context when the first control is created, so the tray context installs it explicitly before creating the audio service. Device events then always arrive on the UI thread.
- **Session-scoped mutex:** `Local\` allows one instance per signed-in user (e.g. with fast user switching), not one per machine.
- **Restore on session end:** the reduced volume is a temporary state. Windows remembers device volumes across sign-ins, so without this a reduced level would stick.

---

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
