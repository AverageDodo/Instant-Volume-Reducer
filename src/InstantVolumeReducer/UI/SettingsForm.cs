using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Hotkeys;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.UI;

/// <summary>
/// The settings dialog. It works on a copy of the settings: nothing changes until OK, and Cancel
/// discards everything. On OK it registers the new hotkey itself (so a taken combination can be
/// reported while the dialog is still open) and exposes the edited settings as <see cref="Result"/>;
/// saving and applying them is up to the caller.
/// </summary>
/// <remarks>
/// Shown modeless, so the tray menu and the hotkey keep working while it's open. While the hotkey box
/// has the focus, the app's current hotkey is unregistered so it can be pressed in the box (otherwise
/// Windows would deliver it as a hotkey instead of as key presses); it's registered again when the box
/// loses the focus or the dialog closes.
/// </remarks>
internal sealed class SettingsForm : Form
{
    private static readonly string Title = $"{AppInfo.Name} {AppInfo.Version} – Settings";

    private static readonly ModeItem[] ModeItems =
    [
        new(ReductionMode.Relative, "Relative"),
        new(ReductionMode.Absolute, "Absolute"),
    ];

    private readonly IAudioDeviceService _audio;
    private readonly HotkeyManager _hotkeys;
    private readonly AppSettings _original;
    private List<DeviceRow> _rows;
    private HotkeyBinding? _suspendedHotkey;
    private bool _populating;

    private readonly CheckBox _selectAllBox;
    private readonly DataGridView _grid;
    private readonly DataGridViewCheckBoxColumn _enabledColumn;
    private readonly DataGridViewTextBoxColumn _nameColumn;
    private readonly DataGridViewComboBoxColumn _modeColumn;
    private readonly DataGridViewTextBoxColumn _amountColumn;
    private readonly DataGridViewTextBoxColumn _statusColumn;
    private readonly ComboBox _defaultModeBox;
    private readonly NumericUpDown _defaultAmountBox;
    private readonly HotkeyCaptureBox _hotkeyBox;
    private readonly Label _hotkeyMessage;
    private readonly CheckBox _startWithWindowsBox;
    private readonly CheckBox _showNotificationsBox;
    private readonly CheckBox _checkForUpdatesBox;
    private readonly CheckBox _overlayEnabledBox;
    private readonly ComboBox _overlayCornerBox;
    private readonly ComboBox _overlaySizeBox;
    private readonly NumericUpDown _overlayOpacityBox;
    private readonly ComboBox _overlayMonitorBox;
    private readonly CheckBox _overlayKeepReadyBox;

    /// <param name="settings">The current settings; the dialog doesn't modify this object.</param>
    /// <param name="audio">Used to list the output devices (initially and on Refresh).</param>
    /// <param name="hotkeys">The app's hotkey manager; OK registers the new hotkey through it.</param>
    /// <param name="icon">The window icon (not owned by the dialog).</param>
    /// <param name="updatesAvailable">Whether this copy is installed and can update itself.</param>
    public SettingsForm(AppSettings settings, IAudioDeviceService audio, HotkeyManager hotkeys, Icon? icon, bool updatesAvailable)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _original = settings;
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        _rows = DeviceTableModel.BuildRows(
            _audio.GetOutputDevices(),
            settings.Devices,
            settings.DefaultMode,
            settings.DefaultAmount,
            settings.WindowsDefaultDevice ?? new WindowsDefaultDeviceConfig(),
            _audio.GetDefaultOutputDeviceId());

        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Title;
        Icon = icon;
        ShowIcon = icon is not null;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true; // The app has no other window, so let the dialog be found in the taskbar.
        ClientSize = new Size(680, 700);
        MinimumSize = new Size(560, 600);
        Padding = new Padding(10);

        // ---- Devices ----
        _enabledColumn = new DataGridViewCheckBoxColumn
        {
            HeaderText = "Reduce",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            ToolTipText = "Reduce this device when the hotkey is pressed.",
        };
        _nameColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Device",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 50,
        };
        _modeColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "Mode",
            DataSource = ModeItems,
            DisplayMember = nameof(ModeItem.Name),
            ValueMember = nameof(ModeItem.Mode),
            ValueType = typeof(ReductionMode),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            FlatStyle = FlatStyle.Flat,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 18,
            ToolTipText = "Relative: reduce to Amount % of the current volume. Absolute: set the volume to Amount %.",
        };
        _amountColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Amount %",
            ValueType = typeof(int),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 14,
            ToolTipText = "A whole number from 0 to 100.",
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight },
        };
        _statusColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Status",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 18,
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            EditMode = DataGridViewEditMode.EditOnEnter,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            StandardTab = true, // Tab leaves the grid instead of walking through every cell.
            AccessibleName = "Output devices",
        };
        _grid.Columns.AddRange(_enabledColumn, _nameColumn, _modeColumn, _amountColumn, _statusColumn);
        _grid.CurrentCellDirtyStateChanged += OnGridCurrentCellDirtyStateChanged;
        _grid.CellValueChanged += OnGridCellValueChanged;
        _grid.CellParsing += OnGridCellParsing;
        _grid.CellValidating += OnGridCellValidating;
        _grid.CellEndEdit += (_, e) => _grid.Rows[e.RowIndex].ErrorText = string.Empty;
        _grid.DataError += OnGridDataError;

        // AutoCheck is off so a click from the indeterminate state selects all (see ToggleAllDevices)
        // instead of WinForms' default of clearing the box.
        _selectAllBox = new CheckBox
        {
            Text = "Select &all",
            AutoSize = true,
            AutoCheck = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(4, 0, 3, 4),
            AccessibleName = "Select all devices",
        };
        _selectAllBox.Click += (_, _) => ToggleAllDevices();

        var refreshButton = new Button { Text = "&Refresh", AutoSize = true, Anchor = AnchorStyles.Left };
        refreshButton.Click += (_, _) => RefreshDevices();

        var modeHelp = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "Windows default device: whichever device is the default when you press the hotkey. If the " +
                   "default changes while the volume is reduced, the reduction moves to the new device. " +
                   "Relative: reduce to Amount % of the current volume. Absolute: set the volume to Amount % " +
                   "(a quieter device is left as it is). Devices that aren't connected are shown in grey.",
            Margin = new Padding(3, 6, 3, 3),
        };

        // A 100 % column (rather than the default auto-size one) keeps the grid inside the group and lets
        // the help text wrap instead of widening the whole layout.
        var devicesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        devicesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        devicesLayout.Controls.Add(_selectAllBox, 0, 0);
        devicesLayout.Controls.Add(_grid, 0, 1);
        devicesLayout.Controls.Add(modeHelp, 0, 2);
        devicesLayout.Controls.Add(refreshButton, 0, 3);

        var devicesGroup = new GroupBox { Text = "Output devices", Dock = DockStyle.Fill, Padding = new Padding(8) };
        devicesGroup.Controls.Add(devicesLayout);

        // ---- Defaults ----
        _defaultModeBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DataSource = ModeItems.ToList(),
            DisplayMember = nameof(ModeItem.Name),
            ValueMember = nameof(ModeItem.Mode),
            Anchor = AnchorStyles.Left,
            Width = 120,
            AccessibleName = "Default mode",
        };
        _defaultAmountBox = new NumericUpDown
        {
            Minimum = DeviceReductionConfig.MinAmount,
            Maximum = DeviceReductionConfig.MaxAmount,
            Anchor = AnchorStyles.Left,
            Width = 70,
            TextAlign = HorizontalAlignment.Right,
            AccessibleName = "Default amount in percent",
        };

        var defaultsLayout = NewFormLayout(columns: 5);
        defaultsLayout.Controls.Add(NewLabel("&Mode:"), 0, 0);
        defaultsLayout.Controls.Add(_defaultModeBox, 1, 0);
        defaultsLayout.Controls.Add(NewLabel("A&mount:"), 2, 0);
        defaultsLayout.Controls.Add(_defaultAmountBox, 3, 0);
        defaultsLayout.Controls.Add(NewLabel("%"), 4, 0);

        var defaultsGroup = NewAutoSizeGroup("Defaults for newly selected devices", defaultsLayout);

        // ---- Hotkey ----
        _hotkeyBox = new HotkeyCaptureBox { Width = 220, Anchor = AnchorStyles.Left, AccessibleName = "Hotkey" };
        _hotkeyBox.GotFocus += (_, _) => SuspendHotkey();
        _hotkeyBox.LostFocus += (_, _) => ResumeHotkey();
        _hotkeyBox.BindingChanged += (_, _) => ShowHotkeyMessage(null);
        _hotkeyBox.HintChanged += (_, _) => ShowHotkeyMessage(_hotkeyBox.Hint);

        _hotkeyMessage = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 4, 3, 3),
        };

        var hotkeyLayout = NewFormLayout(columns: 2);
        hotkeyLayout.Controls.Add(NewLabel("&Hotkey:"), 0, 0);
        hotkeyLayout.Controls.Add(_hotkeyBox, 1, 0);
        hotkeyLayout.Controls.Add(_hotkeyMessage, 1, 1);

        var hotkeyGroup = NewAutoSizeGroup("Hotkey", hotkeyLayout);

        // ---- Options ----
        _startWithWindowsBox = new CheckBox { Text = "&Start with Windows", AutoSize = true };
        _showNotificationsBox = new CheckBox { Text = "Show a &notification when the volume is reduced or restored", AutoSize = true };

        var optionsLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        optionsLayout.Controls.Add(_startWithWindowsBox);
        optionsLayout.Controls.Add(_showNotificationsBox);

        _checkForUpdatesBox = new CheckBox
        {
            Text = updatesAvailable
                ? "Check for &updates automatically"
                : "Check for &updates automatically (only in the installed app)",
            AutoSize = true,
            Enabled = updatesAvailable,
        };
        optionsLayout.Controls.Add(_checkForUpdatesBox);

        var optionsGroup = NewAutoSizeGroup("Options", optionsLayout);

        // ---- On-screen indicator ----
        _overlayEnabledBox = new CheckBox { Text = "Show an &icon on screen while the volume is reduced", AutoSize = true };
        _overlayCornerBox = NewChoiceBox(
            "Overlay corner",
            new Choice<OverlayCorner>(OverlayCorner.TopLeft, "Top left"),
            new Choice<OverlayCorner>(OverlayCorner.TopRight, "Top right"),
            new Choice<OverlayCorner>(OverlayCorner.BottomLeft, "Bottom left"),
            new Choice<OverlayCorner>(OverlayCorner.BottomRight, "Bottom right"));
        _overlaySizeBox = NewChoiceBox(
            "Overlay size",
            new Choice<OverlaySize>(OverlaySize.Small, "Small"),
            new Choice<OverlaySize>(OverlaySize.Medium, "Medium"),
            new Choice<OverlaySize>(OverlaySize.Large, "Large"));
        _overlayMonitorBox = NewChoiceBox(
            "Overlay monitor",
            new Choice<OverlayMonitor>(OverlayMonitor.Primary, "Main monitor"),
            new Choice<OverlayMonitor>(OverlayMonitor.ActiveWindow, "Monitor with the active window"));
        _overlayMonitorBox.Width = 220;
        _overlayOpacityBox = new NumericUpDown
        {
            Minimum = OverlaySettings.MinOpacity,
            Maximum = OverlaySettings.MaxOpacity,
            Increment = 5,
            Anchor = AnchorStyles.Left,
            Width = 70,
            TextAlign = HorizontalAlignment.Right,
            AccessibleName = "Overlay opacity in percent",
        };
        _overlayKeepReadyBox = new CheckBox
        {
            Text = "&Keep the indicator ready to avoid black flashes in HDR games (experimental)",
            AutoSize = true,
            Margin = new Padding(3, 6, 3, 3),
        };
        _overlayEnabledBox.CheckedChanged += (_, _) => UpdateOverlayControls();

        var overlayLayout = NewFormLayout(columns: 5);
        overlayLayout.Controls.Add(_overlayEnabledBox, 0, 0);
        overlayLayout.SetColumnSpan(_overlayEnabledBox, 5);
        overlayLayout.Controls.Add(NewLabel("&Corner:"), 0, 1);
        overlayLayout.Controls.Add(_overlayCornerBox, 1, 1);
        overlayLayout.Controls.Add(NewLabel("Si&ze:"), 2, 1);
        overlayLayout.Controls.Add(_overlaySizeBox, 3, 1);
        overlayLayout.Controls.Add(NewLabel("&Opacity:"), 0, 2);
        overlayLayout.Controls.Add(_overlayOpacityBox, 1, 2);
        overlayLayout.Controls.Add(NewLabel("Displa&y:"), 2, 2);
        overlayLayout.Controls.Add(_overlayMonitorBox, 3, 2);
        overlayLayout.SetColumnSpan(_overlayMonitorBox, 2);
        overlayLayout.Controls.Add(_overlayKeepReadyBox, 0, 3);
        overlayLayout.SetColumnSpan(_overlayKeepReadyBox, 5);

        var overlayGroup = NewAutoSizeGroup("On-screen indicator", overlayLayout);

        var overlayToolTip = new ToolTip { AutoPopDelay = 20000 };
        FormClosed += (_, _) => overlayToolTip.Dispose();
        overlayToolTip.SetToolTip(
            _overlayKeepReadyBox,
            "Keeps the indicator window on screen, practically invisible, whenever the indicator is on, and only\n" +
            "makes it visible while the volume is reduced. Showing a new window over a fullscreen game can cause\n" +
            "a short black screen on some HDR monitors; a window that's always there avoids that on toggle.\n" +
            "Trade-off: games under the indicator's corner are always composed by Windows, which can add about\n" +
            "one frame of delay and, on some setups, turn off variable refresh rate in windowed mode.");

        // ---- Buttons ----
        var okButton = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(80, 0) };
        okButton.Click += (_, _) => Accept();
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 0) };
        cancelButton.Click += (_, _) => Cancel();
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 6, 0, 0),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        // ---- Root ----
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        root.Controls.Add(devicesGroup, 0, 0);
        root.Controls.Add(defaultsGroup, 0, 1);
        root.Controls.Add(hotkeyGroup, 0, 2);
        root.Controls.Add(optionsGroup, 0, 3);
        root.Controls.Add(overlayGroup, 0, 4);
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);

        ResumeLayout(performLayout: true);

        // ---- Values ----
        _defaultModeBox.SelectedValue = settings.DefaultMode;
        _defaultAmountBox.Value = Math.Clamp(settings.DefaultAmount, DeviceReductionConfig.MinAmount, DeviceReductionConfig.MaxAmount);
        _defaultModeBox.SelectedValueChanged += (_, _) => UpdateUnconfiguredRows();
        _defaultAmountBox.ValueChanged += (_, _) => UpdateUnconfiguredRows();

        _hotkeyBox.Binding = HotkeyBinding.TryParse(settings.Hotkey, out var hotkey) ? hotkey : HotkeyBinding.Default;
        _startWithWindowsBox.Checked = settings.StartWithWindows;
        _showNotificationsBox.Checked = settings.ShowNotifications;
        _checkForUpdatesBox.Checked = settings.CheckForUpdates;

        var overlay = settings.Overlay ?? new OverlaySettings();
        _overlayEnabledBox.Checked = overlay.Enabled;
        _overlayCornerBox.SelectedValue = overlay.Corner;
        _overlaySizeBox.SelectedValue = overlay.Size;
        _overlayMonitorBox.SelectedValue = overlay.Monitor;
        _overlayOpacityBox.Value = Math.Clamp(overlay.Opacity, OverlaySettings.MinOpacity, OverlaySettings.MaxOpacity);
        _overlayKeepReadyBox.Checked = overlay.KeepWindowReady;
        UpdateOverlayControls();

        PopulateGrid();

        _audio.DefaultDeviceChanged += OnDefaultDeviceChanged;
    }

    /// <summary>The edited settings after OK; <c>null</c> if the dialog was cancelled.</summary>
    public AppSettings? Result { get; private set; }

    private ReductionMode DefaultMode =>
        _defaultModeBox.SelectedValue is ReductionMode mode ? mode : AppSettings.DefaultReductionMode;

    private int DefaultAmount => (int)_defaultAmountBox.Value;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _grid.ClearSelection();
        _grid.CurrentCell = null;
        _hotkeyBox.SelectionLength = 0;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _audio.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        ResumeHotkey();
        base.OnFormClosed(e);
    }

    // ---- OK / Cancel ----

    private void Accept()
    {
        if (!_grid.EndEdit())
        {
            return; // An invalid amount is being edited; the grid shows the error.
        }

        ResumeHotkey(); // Enter in the hotkey box presses OK while the box still has the focus.

        var binding = _hotkeyBox.Binding ?? HotkeyBinding.Default;
        if (_hotkeys.Current != binding)
        {
            var result = _hotkeys.Register(binding);
            if (!result.Success)
            {
                // The manager re-registered the previous hotkey, so the old one keeps working.
                ShowHotkeyMessage(result.Message);
                _hotkeyBox.Focus();
                return;
            }
        }

        Result = new AppSettings
        {
            Hotkey = binding.ToString(),
            Devices = DeviceTableModel.ToConfigs(_rows),
            WindowsDefaultDevice = DeviceTableModel.ToWindowsDefaultConfig(_rows) ?? new WindowsDefaultDeviceConfig(),
            DefaultMode = DefaultMode,
            DefaultAmount = DefaultAmount,
            StartWithWindows = _startWithWindowsBox.Checked,
            ShowNotifications = _showNotificationsBox.Checked,
            CheckForUpdates = _checkForUpdatesBox.Checked,
            Overlay = new OverlaySettings
            {
                Enabled = _overlayEnabledBox.Checked,
                Corner = SelectedOr(_overlayCornerBox, _original.Overlay?.Corner ?? OverlayCorner.TopRight),
                Size = SelectedOr(_overlaySizeBox, _original.Overlay?.Size ?? OverlaySize.Medium),
                Monitor = SelectedOr(_overlayMonitorBox, _original.Overlay?.Monitor ?? OverlayMonitor.Primary),
                Opacity = (int)_overlayOpacityBox.Value,
                KeepWindowReady = _overlayKeepReadyBox.Checked,
            },
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Cancel()
    {
        _grid.CancelEdit();
        Result = null;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // ---- Hotkey ----

    private void SuspendHotkey()
    {
        if (_suspendedHotkey is null && _hotkeys.Current is { } current)
        {
            _suspendedHotkey = current;
            _hotkeys.Unregister();
        }
    }

    private void ResumeHotkey()
    {
        if (_suspendedHotkey is { } binding)
        {
            _suspendedHotkey = null;
            _hotkeys.Register(binding);
        }
    }

    private void ShowHotkeyMessage(string? message)
    {
        _hotkeyMessage.ForeColor = message is null ? SystemColors.GrayText : Color.Firebrick;
        _hotkeyMessage.Text = message ?? "Click the box and press the new key combination. Backspace undoes the change.";
    }

    // ---- Device table ----

    private void RefreshDevices()
    {
        if (!_grid.EndEdit())
        {
            return;
        }

        _rows = DeviceTableModel.BuildRows(
            _audio.GetOutputDevices(),
            DeviceTableModel.ToConfigs(_rows),
            DefaultMode,
            DefaultAmount,
            DeviceTableModel.ToWindowsDefaultConfig(_rows),
            _audio.GetDefaultOutputDeviceId());
        PopulateGrid();
    }

    /// <summary>
    /// Keeps the Windows default device row's status current without rebuilding the table, so edits in
    /// progress aren't disturbed.
    /// </summary>
    private void OnDefaultDeviceChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        foreach (DataGridViewRow gridRow in _grid.Rows)
        {
            if (gridRow.Tag is DeviceRow { IsWindowsDefault: true } row)
            {
                DeviceTableModel.SetCurrentDefault(row, _audio.GetDefaultOutputDeviceId(), _audio.GetOutputDevices());
                gridRow.Cells[_statusColumn.Index].Value = DeviceTableModel.StatusText(row);
                ApplyRowStyle(gridRow, row);
            }
        }
    }

    private void PopulateGrid()
    {
        _populating = true;
        try
        {
            _grid.Rows.Clear();
            foreach (var row in _rows)
            {
                var index = _grid.Rows.Add(row.Enabled, row.Name, row.Mode, row.Amount, DeviceTableModel.StatusText(row));
                var gridRow = _grid.Rows[index];
                gridRow.Tag = row;
                if (row.IsWindowsDefault)
                {
                    gridRow.Cells[_nameColumn.Index].ToolTipText =
                        "Whichever device is the Windows default output device when you press the hotkey. " +
                        "A device that's also ticked below uses its own settings.";
                }

                ApplyRowStyle(gridRow, row);
            }
        }
        finally
        {
            _populating = false;
        }

        UpdateSelectAllBox();
        ShowHotkeyMessage(_hotkeyBox.Hint);
    }

    private void ToggleAllDevices()
    {
        if (!_grid.EndEdit())
        {
            return; // An invalid amount is being edited; the grid shows the error.
        }

        // The rows are the source of truth (cell edits are written back to them), so rebuilding the
        // grid from them is the simplest way to show the new ticks, modes, amounts and styles.
        DeviceTableModel.ToggleAll(_rows, DefaultMode, DefaultAmount);
        PopulateGrid();
    }

    private void UpdateSelectAllBox()
    {
        _selectAllBox.CheckState = DeviceTableModel.GetSelectAllState(_rows);
        _selectAllBox.Enabled = _rows.Count > 0;
    }

    /// <summary>Keeps rows that were never configured showing the current defaults.</summary>
    private void UpdateUnconfiguredRows()
    {
        _populating = true;
        try
        {
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (gridRow.Tag is DeviceRow { HasConfig: false } row)
                {
                    row.Mode = DefaultMode;
                    row.Amount = DefaultAmount;
                    gridRow.Cells[_modeColumn.Index].Value = row.Mode;
                    gridRow.Cells[_amountColumn.Index].Value = row.Amount;
                }
            }
        }
        finally
        {
            _populating = false;
        }
    }

    private void ApplyRowStyle(DataGridViewRow gridRow, DeviceRow row)
    {
        var editable = row.Enabled;
        gridRow.Cells[_modeColumn.Index].ReadOnly = !editable;
        gridRow.Cells[_amountColumn.Index].ReadOnly = !editable;

        var greyed = !row.IsConnected;
        gridRow.DefaultCellStyle.ForeColor = greyed ? SystemColors.GrayText : SystemColors.ControlText;

        foreach (var column in new DataGridViewColumn[] { _modeColumn, _amountColumn })
        {
            var style = gridRow.Cells[column.Index].Style;
            style.ForeColor = editable && !greyed ? SystemColors.ControlText : SystemColors.GrayText;
            style.BackColor = editable ? SystemColors.Window : SystemColors.Control;
        }
    }

    private void OnGridCurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        // Commit checkbox and dropdown changes right away instead of when the cell is left.
        if (_grid.IsCurrentCellDirty && _grid.CurrentCell?.OwningColumn is DataGridViewCheckBoxColumn or DataGridViewComboBoxColumn)
        {
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void OnGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_populating || e.RowIndex < 0 || _grid.Rows[e.RowIndex] is not { Tag: DeviceRow row } gridRow)
        {
            return;
        }

        var value = gridRow.Cells[e.ColumnIndex].Value;
        if (e.ColumnIndex == _enabledColumn.Index)
        {
            DeviceTableModel.SetEnabled(row, value is true, DefaultMode, DefaultAmount);
            _populating = true;
            try
            {
                gridRow.Cells[_modeColumn.Index].Value = row.Mode;
                gridRow.Cells[_amountColumn.Index].Value = row.Amount;
            }
            finally
            {
                _populating = false;
            }

            ApplyRowStyle(gridRow, row);
            UpdateSelectAllBox();
        }
        else if (e.ColumnIndex == _modeColumn.Index && value is ReductionMode mode)
        {
            row.Mode = mode;
        }
        else if (e.ColumnIndex == _amountColumn.Index && value is int amount)
        {
            row.Amount = amount;
        }
    }

    private void OnGridCellParsing(object? sender, DataGridViewCellParsingEventArgs e)
    {
        if (e.ColumnIndex == _amountColumn.Index && TryParseAmount(e.Value as string, out var amount))
        {
            e.Value = amount;
            e.ParsingApplied = true;
        }
    }

    private void OnGridCellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.ColumnIndex != _amountColumn.Index || _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly)
        {
            return;
        }

        if (!TryParseAmount(e.FormattedValue as string, out _))
        {
            _grid.Rows[e.RowIndex].ErrorText = "Enter a whole number from 0 to 100.";
            e.Cancel = true;
        }
    }

    private void OnGridDataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        // Never show the grid's default error dialog; validation above explains what's wrong.
        _grid.Rows[e.RowIndex].ErrorText = "Enter a whole number from 0 to 100.";
        e.ThrowException = false;
        e.Cancel = true;
    }

    /// <summary>Accepts "20", " 20 " and "20%"; only whole numbers from 0 to 100.</summary>
    private static bool TryParseAmount(string? text, out int amount)
    {
        amount = 0;
        var trimmed = text?.Trim().TrimEnd('%').Trim();
        return int.TryParse(trimmed, out amount)
            && amount is >= DeviceReductionConfig.MinAmount and <= DeviceReductionConfig.MaxAmount;
    }

    // ---- On-screen indicator ----

    private void UpdateOverlayControls()
    {
        var enabled = _overlayEnabledBox.Checked;
        _overlayCornerBox.Enabled = enabled;
        _overlaySizeBox.Enabled = enabled;
        _overlayOpacityBox.Enabled = enabled;
        _overlayMonitorBox.Enabled = enabled;
        _overlayKeepReadyBox.Enabled = enabled;
    }

    private static T SelectedOr<T>(ComboBox box, T fallback)
        where T : struct, Enum =>
        box.SelectedValue is T value ? value : fallback;

    // ---- Layout helpers ----

    private static ComboBox NewChoiceBox<T>(string accessibleName, params Choice<T>[] choices)
        where T : struct, Enum => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        DataSource = choices.ToList(),
        DisplayMember = nameof(Choice<T>.Name),
        ValueMember = nameof(Choice<T>.Value),
        Anchor = AnchorStyles.Left,
        Width = 130,
        AccessibleName = accessibleName,
    };

    private static TableLayoutPanel NewFormLayout(int columns)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        for (var i = 0; i < columns; i++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        return layout;
    }

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 6, 3, 3),
    };

    private static GroupBox NewAutoSizeGroup(string text, Control content)
    {
        var group = new GroupBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 6),
        };
        group.Controls.Add(content);
        return group;
    }

    private sealed record ModeItem(ReductionMode Mode, string Name);

    private sealed record Choice<T>(T Value, string Name)
        where T : struct, Enum;
}
