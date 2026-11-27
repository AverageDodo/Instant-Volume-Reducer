# Instant-Volume-Reducer

A small Windows tray app that turns down selected audio output devices when you press a global hotkey, and puts them back to their previous volume when you press it again.

## Features

- **Toggle with one hotkey:** the first press reduces, the second restores the exact previous levels. The default hotkey is `Ctrl+Alt+V`, and you can change it.
- **You choose the devices:** for example, reduce the speakers but leave the headset alone.
- **Follow the Windows default device (optional):** reduce whichever device is the default output, and move the reduction along when you switch the default while the volume is reduced.
- **Per-device reduction:** each device has its own mode and amount.
  - *Relative:* reduce to *Amount %* of the current volume (e.g. 20 % of 50 % = 10 %).
  - *Absolute:* set the volume to *Amount %*. If the device is already quieter than that, it's left as it is, because the hotkey never makes anything louder.
- **Tray icon:** blue when the volume is normal and orange when it's reduced, with a tooltip saying how many devices are reduced.
- **Safe restore:** volumes are restored on exit and Windows sign-out, and after a crash (see [Files and crash recovery](#files-and-crash-recovery)).
- Optional **Start with Windows** and **notifications** on each toggle.
- Optional **on-screen indicator:** a small orange speaker badge in a corner of the screen while the volume is reduced.
- **Installer with automatic updates:** installs per user without admin rights and keeps itself up to date (see [Download](#download)).

## Download

### Installer (recommended)

Download **`InstantVolumeReducer-win-Setup.exe`** from the latest release on the repository's **Releases** page and run it.

- **No admin rights needed:** it installs for your Windows user only, to `%LocalAppData%\InstantVolumeReducer`, and starts the app.
- **Start menu shortcut** and an entry in **Settings → Apps**, where you can uninstall it like any other app.
- **Automatic updates:** the installed app checks for new versions shortly after it starts and then twice a day, and downloads them in the background. When one is ready, a notification says so. Click it, or use **Restart to install version …** in the tray menu, to install it now; otherwise it's installed the next time the app starts. You can turn the checks off in Settings, and check by hand with **Check for updates…** in the tray menu.

### Portable exe (only if you know what that means for you)

`InstantVolumeReducer-v<version>-win-x64.exe` is the app as a single file that runs without installing. It's meant for people who deliberately want no installation, for example to run it from a tools folder. With it:

- **No updates:** it never updates itself; you have to download new versions yourself.
- **You're responsible for where it lives:** it runs from wherever you put it. If it stays in your Downloads folder, it's easily deleted by accident. "Start with Windows" points to the file's current location, so moving or deleting it breaks that.
- **No uninstaller:** to remove it, turn off "Start with Windows" in its settings, exit it, and delete the file.

### Checking the download

Each file has a `.sha256` file next to it with its SHA-256 checksum. Compare it with:

```powershell
Get-FileHash .\InstantVolumeReducer-win-Setup.exe -Algorithm SHA256
```

The files aren't code-signed, so Windows SmartScreen may warn the first time you run them ("More info" → "Run anyway"). The tray menu and the settings window show the running version.

### Uninstalling

Uninstall from **Settings → Apps**. It removes the app, its shortcuts and its "Start with Windows" entry. If the volume is still reduced, it's restored first. Your settings in `%APPDATA%\InstantVolumeReducer` are kept, so a reinstall picks them up again. Delete that folder by hand if you want them gone.

## Requirements

- Windows 10 or 11, x64.
- The published exe is self-contained, so you don't need to install .NET to run it.
- To build from source: .NET 10 SDK.

## Usage

1. Start `InstantVolumeReducer.exe`. It runs in the notification area (system tray) and has no main window. Only one copy runs at a time.
2. Press the hotkey (`Ctrl+Alt+V` by default) or double-click the tray icon to reduce the volume. Do it again to restore it.
3. Right-click the tray icon for **Reduce/Restore volume**, **Settings…** and **Exit**.

On first start, every connected output device is selected with the defaults: Relative, 20 %.

### Settings

- **Output devices:** a table with one row per device: *Reduce* (tick to include it), *Device*, *Mode*, *Amount %* and *Status*.
  - Devices that aren't connected but have saved settings are shown in grey and keep their settings for when you plug them back in.
  - Newly connected devices are not selected automatically. Use **Refresh** to update the list.
  - **Select all** above the table ticks every device, or unticks them all if they're all ticked already. It shows a partial tick when only some devices are selected.
  - **Windows default device**, the pinned first row, stands for whichever device is the default output when you press the hotkey (the one picked in Sound settings or the taskbar volume menu). Its Status column shows the current one.
    - If the default changes while the volume is reduced (also when Windows switches on its own, e.g. after you plug in headphones), the old default goes back to its original volume and the new one is reduced.
    - A device that's also ticked by name uses its own row's settings, is reduced only once, and stays reduced when the default moves away from it.
    - If the old default can't be restored because it was unplugged, it's restored when you restore the volume, provided it's connected again by then.
    - Only the normal default is followed, not the separate default for calls ("communications device").
- **Defaults:** the mode and amount a device gets when you tick it for the first time.
- **Hotkey:** click the box and press the new combination.
  - It needs at least one of Ctrl, Alt, Shift or Win. F13–F24 and media/volume keys can also be used on their own.
  - Backspace or Delete returns to the hotkey the box started with.
  - If another app already uses the combination, the dialog says so and the old hotkey stays active.
- **Start with Windows** and **Show a notification when the volume is reduced or restored** (both off by default). Warnings, such as a device that couldn't be restored, are always shown.
- **On-screen indicator** (off by default): **Show an icon on screen while the volume is reduced**.
  - Choose the corner, the size (small, medium, large), the opacity (20–100 %) and the display: the main monitor, or the monitor with the active window. With the active window, the badge follows it within about 2 seconds.
  - Clicks go through the badge, it never takes the focus, and it isn't listed in the taskbar or Alt+Tab.
  - It stays inside the monitor's working area, so it doesn't cover the taskbar, and it scales with each monitor's display scaling.
  - **Keep the indicator ready to avoid black flashes in HDR games (experimental):** a window appearing over a fullscreen game makes Windows switch from sending the game's frames straight to the monitor to composing the desktop. On some HDR monitors, that switch causes a short black screen.
    - With this option, the indicator window stays on screen, practically invisible, whenever the indicator is on, and only becomes visible while the volume is reduced, so toggling doesn't trigger the switch.
    - Trade-off: a game under the indicator's corner is always composed by Windows. That can add about one frame of delay and, on some setups, turn off variable refresh rate in windowed mode.

Changes take effect when you click **OK**. Changing a device's mode or amount while the volume is reduced applies from the next reduction. Unticking a device while it's reduced restores it right away.

## Files and crash recovery

Everything is stored per user in `%APPDATA%\InstantVolumeReducer\`:

| File | Contents |
|---|---|
| `settings.json` | Settings (hotkey, devices, defaults, options). If the file is corrupt, the app starts with defaults and keeps a copy as `settings.json.bak`. |
| `state.json` | Only exists while the volume is reduced: each device's original volume. |

If the app ends while the volume is reduced (crash, power loss, killed process), `state.json` is still there at the next start. The app then restores those volumes automatically and shows a notification. Start with Windows uses the value `InstantVolumeReducer` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Building

All commands run from the repository root.

```powershell
dotnet build src
```

```powershell
dotnet test src
```

Publish a self-contained single-file release exe to `src\InstantVolumeReducer\bin\publish\win-x64\InstantVolumeReducer.exe`:

```powershell
dotnet publish src\InstantVolumeReducer\InstantVolumeReducer.csproj -p:PublishProfile=win-x64
```

The exe is about 112 MB because it contains the .NET runtime. It isn't compressed, which keeps the app's memory use low while it runs in the tray.

The app icon (`src\InstantVolumeReducer\Resources\AppIcon.ico`) is generated by `src\tools\New-AppIcon.ps1`. Run that script again only if the icon design changes.

### Installer and updates (Velopack)

The installer and the automatic updates use [Velopack](https://velopack.io). Its packaging tool `vpk` is a local .NET tool pinned in `dotnet-tools.json`, at the same version as the `Velopack` NuGet package; keep the two in step when updating either. To build the installer locally after publishing:

```powershell
dotnet tool restore
```

```powershell
dotnet vpk pack --packId InstantVolumeReducer --packVersion 0.1.0 --packTitle "Instant Volume Reducer" --runtime win-x64 --packDir src\InstantVolumeReducer\bin\publish\win-x64 --mainExe InstantVolumeReducer.exe --icon src\InstantVolumeReducer\Resources\AppIcon.ico --shortcuts StartMenuRoot --noPortable --outputDir velopack
```

This writes `InstantVolumeReducer-win-Setup.exe`, the update package and the `releases.win.json` feed to `velopack\`. Use the same version as `<Version>` in the project file.

The installed app looks for updates in this repository's GitHub releases, which only works while the repository is public. To test updates without GitHub:

1. Pack two versions into the same folder.
2. Set the environment variable `INSTANTVOLUMEREDUCER_UPDATE_SOURCE` to that folder.
3. Start the installed app from that environment.

## Branches, CI and releases

Work flows `feature branch → develop → main`, always through pull requests:

1. Branch off `develop`, then open a pull request into `develop`. **CI** (`.github/workflows/ci.yml`) builds and runs the tests.
2. To release, raise `<Version>` in `src\InstantVolumeReducer\InstantVolumeReducer.csproj` on `develop` (SemVer: `0.2.0`, or `0.3.0-beta.1` for a pre-release).
3. Open a pull request from `develop` into `main`. Besides the build and tests, a **release check** makes sure it comes from `develop`, and says which version merging will release. It fails if the version is lower than the latest release. If the version is already released, it warns that merging won't create a release.
4. Merge it with **Create a merge commit**. Squash or rebase merges make `main` and `develop` drift apart, which causes conflicts in later pull requests.
5. The **Release** workflow (`.github/workflows/release.yml`) runs on the push to `main`:
   - It builds and tests the app, publishes the exe, and builds the Velopack installer and update packages. If the previous release has update packages, it also builds a small delta package, so installed copies only download what changed.
   - Then it assembles a **draft** release and publishes it, with notes generated from the merged pull requests. Publishing creates the tag `v<Version>` on the merge commit. The release contains:
     - the installer, the portable exe, and a `.sha256` file for each
     - the update packages and the `releases.win.json` feed that installed copies read
   - The repository has GitHub's **immutable releases** turned on, so a published release can't be changed. That's why nothing is published until every file is attached.
   - If the run fails, at most a draft release exists and no tag. Re-running the workflow removes the draft and starts over.
   - If `v<Version>` already exists, nothing is released.
   - Versions with a pre-release part are published as pre-releases.

Never create release tags by hand: the release workflow creates them. `.github/scripts/Get-ReleaseInfo.ps1` holds the version logic both workflows use. Run it locally to see what would happen:

```powershell
.\.github\scripts\Get-ReleaseInfo.ps1
```

On a private repository on GitHub's Free plan, branch protection isn't available. So GitHub shows the checks on each pull request but doesn't block merging when they fail.

## Known limitations

- **Output devices only:** the app changes each device's master volume. It doesn't control per-app volumes or microphones.
- **Overlay and exclusive fullscreen:** apps that run in exclusive fullscreen (some games) draw over every normal window, so the on-screen indicator isn't visible there. Windowed and borderless-fullscreen apps are fine.
- **Hotkey conflicts:** a global hotkey only works if no other app has registered the same combination. Pick another one in Settings if it's taken.
- **Manual changes while reduced are overwritten:** restoring always returns to the volume saved when reducing, even if you changed the volume by hand in between.
- **Unplugged devices:** a device that is unplugged while reduced isn't restored, and its saved original volume is discarded.
- **Unsigned exe:** Windows SmartScreen may warn the first time you run the published exe.
