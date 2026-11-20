using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Core;
using InstantVolumeReducer.Hotkeys;
using InstantVolumeReducer.Settings;
using InstantVolumeReducer.UI;
using Microsoft.Win32;

namespace InstantVolumeReducer;

/// <summary>
/// The windowless tray app: owns the tray icon and its menu and connects the hotkey, the toggle logic
/// and the audio devices. Create, use and dispose it on the UI thread.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "Instant Volume Reducer";
    private const int BalloonTimeoutMs = 4000;

    private readonly CoreAudioDeviceService _audio;
    private readonly SettingsStore _store;
    private AppSettings _settings;
    private readonly VolumeToggleService _toggle;
    private readonly HotkeyManager _hotkeys;
    private readonly TrayIcons _icons;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly Font _boldMenuFont;
    private readonly NotifyIcon _notifyIcon;
    private SettingsForm? _settingsForm;
    private bool _shutDown;

    public TrayApplicationContext()
    {
        // The audio service captures the current synchronization context to raise DevicesChanged on the
        // UI thread. WinForms only installs its context once the first control exists, so install it now.
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }

        _audio = new CoreAudioDeviceService();

        _store = new SettingsStore();
        var loaded = _store.Load();
        _settings = loaded.Settings;
        bool saveSettings;
        if (loaded.Status == SettingsLoadStatus.Loaded)
        {
            saveSettings = DeviceConfigUpdater.RefreshNames(_settings, _audio.GetOutputDevices());
        }
        else
        {
            // First run (or an unreadable file): select every active output device, so the hotkey works
            // right away. Don't overwrite an unreadable file that couldn't be backed up first.
            DeviceConfigUpdater.EnableAllActive(_settings, _audio.GetOutputDevices());
            saveSettings = loaded.Status == SettingsLoadStatus.Missing || loaded.BackupPath is not null;
        }

        _toggle = new VolumeToggleService(_audio, _settings.Devices);
        _toggle.StateChanged += OnToggleStateChanged;
        _audio.DevicesChanged += OnDevicesChanged;

        _icons = new TrayIcons();

        _menu = new ContextMenuStrip();
        _boldMenuFont = new Font(_menu.Font, FontStyle.Bold);
        _toggleItem = new ToolStripMenuItem("Reduce volume", null, (_, _) => ToggleVolume())
        {
            Font = _boldMenuFont, // Bold marks the double-click action.
        };
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        _notifyIcon = new NotifyIcon
        {
            Icon = _icons.Normal,
            Text = AppName,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.MouseDoubleClick += OnNotifyIconDoubleClick;

        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += (_, _) => ToggleVolume();

        UpdateTrayState();
        _notifyIcon.Visible = true;

        RegisterHotkey();

        if (saveSettings)
        {
            SaveSettings();
        }

        if (loaded.Status == SettingsLoadStatus.Corrupt)
        {
            ShowBalloon(
                loaded.BackupPath is null
                    ? "The settings file couldn't be read, so the default settings are used."
                    : $"The settings file couldn't be read, so the default settings are used. The old file was kept as {Path.GetFileName(loaded.BackupPath)}.",
                ToolTipIcon.Warning);
        }

        Application.ApplicationExit += OnApplicationExit;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    protected override void ExitThreadCore()
    {
        Shutdown();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Shutdown();
        }

        base.Dispose(disposing);
    }

    private void RegisterHotkey()
    {
        var binding = HotkeyBinding.TryParse(_settings.Hotkey, out var parsed) ? parsed : HotkeyBinding.Default;
        var result = _hotkeys.Register(binding);
        if (!result.Success)
        {
            ShowBalloon($"The hotkey couldn't be set up. {result.Message}", ToolTipIcon.Warning);
        }

        UpdateTrayState();
    }

    private void ToggleVolume()
    {
        if (_shutDown)
        {
            return;
        }

        if (_toggle.IsReduced)
        {
            _toggle.Restore();
        }
        else if (!_toggle.Reduce())
        {
            ShowBalloon("None of the selected output devices is connected, so nothing was reduced.", ToolTipIcon.Info);
        }
    }

    private void OnNotifyIconDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ToggleVolume();
        }
    }

    private void OnToggleStateChanged(object? sender, EventArgs e) => UpdateTrayState();

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        if (_shutDown)
        {
            return;
        }

        // New devices aren't selected automatically; they show up in the settings dialog. Only keep the
        // saved names current, so disconnected devices are listed under their latest name.
        if (DeviceConfigUpdater.RefreshNames(_settings, _audio.GetOutputDevices()))
        {
            SaveSettings();
        }
    }

    /// <summary>Opens the settings dialog, or brings it to the front if it's already open.</summary>
    private void OpenSettings()
    {
        if (_shutDown)
        {
            return;
        }

        if (_settingsForm is { IsDisposed: false } open)
        {
            if (open.WindowState == FormWindowState.Minimized)
            {
                open.WindowState = FormWindowState.Normal;
            }

            open.Activate();
            return;
        }

        var form = new SettingsForm(_settings, _audio, _hotkeys, _icons.Normal);
        form.FormClosed += OnSettingsFormClosed;
        _settingsForm = form;
        form.Show();
        form.Activate();
    }

    private void OnSettingsFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is not SettingsForm form)
        {
            return;
        }

        form.FormClosed -= OnSettingsFormClosed;
        if (ReferenceEquals(_settingsForm, form))
        {
            _settingsForm = null;
        }

        // A modeless form disposes itself after closing; its Result stays readable.
        var result = form.DialogResult == DialogResult.OK ? form.Result : null;
        if (result is not null && !_shutDown)
        {
            ApplySettings(result);
        }
    }

    /// <summary>
    /// Takes over the settings from the dialog. The dialog already registered the new hotkey; this saves
    /// the file and hands the device settings to the toggle service.
    /// </summary>
    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        DeviceConfigUpdater.RefreshNames(_settings, _audio.GetOutputDevices());
        SaveSettings();
        _toggle.UpdateDeviceConfigs(_settings.Devices);
        UpdateTrayState();
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBalloon($"The settings couldn't be saved: {ex.Message}", ToolTipIcon.Warning);
        }
    }

    private void UpdateTrayState()
    {
        if (_shutDown)
        {
            return;
        }

        var reduced = _toggle.IsReduced;
        _notifyIcon.Icon = reduced ? _icons.Reduced : _icons.Normal;

        var count = _toggle.State.Devices.Count;
        _notifyIcon.Text = reduced
            ? $"{AppName}: volume reduced ({count} {(count == 1 ? "device" : "devices")})"
            : $"{AppName}: volume normal";

        _toggleItem.Text = reduced ? "Restore volume" : "Reduce volume";
        _toggleItem.ShortcutKeyDisplayString = _hotkeys.Current?.ToString();
    }

    private void ShowBalloon(string text, ToolTipIcon icon)
    {
        if (!_shutDown)
        {
            _notifyIcon.ShowBalloonTip(BalloonTimeoutMs, AppName, text, icon);
        }
    }

    private void OnApplicationExit(object? sender, EventArgs e) => Shutdown();

    /// <summary>
    /// Windows is signing out or shutting down: restore the volume while we still can. If the shutdown is
    /// cancelled, the app keeps running with the volume restored.
    /// </summary>
    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => _toggle.Restore();

    /// <summary>
    /// Restores the volume if it's reduced, then releases the hotkey, the tray icon and the audio service.
    /// Safe to call more than once.
    /// </summary>
    private void Shutdown()
    {
        if (_shutDown)
        {
            return;
        }

        _shutDown = true;

        Application.ApplicationExit -= OnApplicationExit;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _audio.DevicesChanged -= OnDevicesChanged;
        _toggle.StateChanged -= OnToggleStateChanged;

        // Close the dialog first: closing it re-registers a hotkey it suspended, which needs the manager.
        if (_settingsForm is { } form)
        {
            _settingsForm = null;
            form.FormClosed -= OnSettingsFormClosed;
            if (!form.IsDisposed)
            {
                form.Close(); // Disposes the modeless form; its changes are discarded.
            }
        }

        _toggle.Restore();

        _hotkeys.Dispose();

        // Hide before disposing so no ghost icon stays in the notification area.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _boldMenuFont.Dispose();
        _icons.Dispose();
        _audio.Dispose();
    }
}
