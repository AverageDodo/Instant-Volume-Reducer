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
    private readonly AppSettings _settings;
    private readonly VolumeToggleService _toggle;
    private readonly HotkeyManager _hotkeys;
    private readonly TrayIcons _icons;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly Font _boldMenuFont;
    private readonly NotifyIcon _notifyIcon;
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

        // Until settings are persisted, every active output device is selected with the default reduction.
        _settings = new AppSettings();
        AddNewActiveDevices();

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
        _menu.Items.Add(new ToolStripMenuItem("Settings…") { Enabled = false });
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

        if (AddNewActiveDevices())
        {
            _toggle.UpdateDeviceConfigs(_settings.Devices);
        }
    }

    /// <summary>
    /// Adds active output devices that aren't in the settings yet (enabled, with the default reduction)
    /// and refreshes the names of known ones. Returns <c>true</c> if a device was added.
    /// </summary>
    private bool AddNewActiveDevices()
    {
        var added = false;
        foreach (var device in _audio.GetOutputDevices())
        {
            var existing = _settings.Devices.Find(d => string.Equals(d.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.LastKnownName = device.Name;
            }
            else if (device.IsActive)
            {
                _settings.Devices.Add(new DeviceReductionConfig
                {
                    DeviceId = device.Id,
                    LastKnownName = device.Name,
                    Enabled = true,
                    Mode = _settings.DefaultMode,
                    Amount = _settings.DefaultAmount,
                });
                added = true;
            }
        }

        return added;
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
