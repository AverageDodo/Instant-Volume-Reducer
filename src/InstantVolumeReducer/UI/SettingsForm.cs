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
    private const string Title = "Instant Volume Reducer – Settings";

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

    /// <param name="settings">The current settings; the dialog doesn't modify this object.</param>
    /// <param name="audio">Used to list the output devices (initially and on Refresh).</param>
    /// <param name="hotkeys">The app's hotkey manager; OK registers the new hotkey through it.</param>
    /// <param name="icon">The window icon (not owned by the dialog).</param>
    public SettingsForm(AppSettings settings, IAudioDeviceService audio, HotkeyManager hotkeys, Icon? icon)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _original = settings;
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        _rows = DeviceTableModel.BuildRows(_audio.GetOutputDevices(), settings.Devices, settings.DefaultMode, settings.DefaultAmount);

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
        ClientSize = new Size(680, 560);
        MinimumSize = new Size(560, 480);
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
            Text = "Relative: reduce to Amount % of the current volume. Absolute: set the volume to Amount % " +
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

        var optionsGroup = NewAutoSizeGroup("Options", optionsLayout);

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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        root.Controls.Add(devicesGroup, 0, 0);
        root.Controls.Add(defaultsGroup, 0, 1);
        root.Controls.Add(hotkeyGroup, 0, 2);
        root.Controls.Add(optionsGroup, 0, 3);
        root.Controls.Add(buttons, 0, 4);
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

        PopulateGrid();
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
            DefaultMode = DefaultMode,
            DefaultAmount = DefaultAmount,
            StartWithWindows = _startWithWindowsBox.Checked,
            ShowNotifications = _showNotificationsBox.Checked,
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

        _rows = DeviceTableModel.BuildRows(_audio.GetOutputDevices(), DeviceTableModel.ToConfigs(_rows), DefaultMode, DefaultAmount);
        PopulateGrid();
    }

    private void PopulateGrid()
    {
        _populating = true;
        try
        {
            _grid.Rows.Clear();
            foreach (var row in _rows)
            {
                var index = _grid.Rows.Add(row.Enabled, row.Name, row.Mode, row.Amount, row.IsConnected ? "Connected" : "Not connected");
                var gridRow = _grid.Rows[index];
                gridRow.Tag = row;
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

    // ---- Layout helpers ----

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
}
