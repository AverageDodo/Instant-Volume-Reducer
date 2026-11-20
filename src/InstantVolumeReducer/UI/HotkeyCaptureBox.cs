using System.ComponentModel;
using System.Runtime.InteropServices;
using InstantVolumeReducer.Hotkeys;

namespace InstantVolumeReducer.UI;

/// <summary>
/// A read-only text box that records a key combination: click it (or tab into it) and press the keys.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>While only modifiers are held, it shows them, e.g. <c>Ctrl+Alt+…</c>.</item>
/// <item>A combination that isn't <see cref="HotkeyBinding.IsValid"/> (e.g. a letter without a modifier)
/// is rejected: the previous binding stays and <see cref="Hint"/> says why.</item>
/// <item>Tab, Shift+Tab, Enter and Escape without other modifiers keep their usual dialog behaviour
/// (move focus, OK, Cancel), so the box can't trap the keyboard.</item>
/// <item>Backspace or Delete without modifiers goes back to <see cref="OriginalBinding"/>, the hotkey
/// the box was given, as an undo. There is no "no hotkey" state: the app always needs one.</item>
/// <item>The Windows key is read from the keyboard state, because WinForms doesn't report it as a
/// modifier. Combinations Windows reserves for itself (e.g. Win+V) never reach the box.</item>
/// </list>
/// Keys are captured in <see cref="ProcessCmdKey"/> rather than <c>KeyDown</c>, because the text box's
/// own shortcut handling would otherwise swallow combinations like Ctrl+Shift+V or Ctrl+A first.
/// </remarks>
internal sealed class HotkeyCaptureBox : TextBox
{
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private HotkeyBinding? _binding;
    private HotkeyBinding? _originalBinding;
    private string? _hint;

    public HotkeyCaptureBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false; // Also removes the Copy/Paste context menu.
        BackColor = SystemColors.Window; // Read-only boxes are grey by default; this one is for input.
        Cursor = Cursors.Arrow;
        TextAlign = HorizontalAlignment.Center;
        AccessibleDescription = "Press the key combination to use as the hotkey.";
    }

    /// <summary>Raised when <see cref="Binding"/> changes because the user pressed a new valid combination.</summary>
    public event EventHandler? BindingChanged;

    /// <summary>Raised when <see cref="Hint"/> changes.</summary>
    public event EventHandler? HintChanged;

    /// <summary>
    /// The captured hotkey. Setting it also sets <see cref="OriginalBinding"/> (the value Backspace goes
    /// back to) and clears <see cref="Hint"/>; it doesn't raise <see cref="BindingChanged"/>.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public HotkeyBinding? Binding
    {
        get => _binding;
        set
        {
            _binding = value;
            _originalBinding = value;
            SetHint(null);
            ShowBinding();
        }
    }

    /// <summary>The binding the box was given; Backspace or Delete returns to it.</summary>
    [Browsable(false)]
    public HotkeyBinding? OriginalBinding => _originalBinding;

    /// <summary>Why the last combination was rejected, or <c>null</c>.</summary>
    [Browsable(false)]
    public string? Hint => _hint;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Focused || IsDialogNavigationKey(keyData))
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        CaptureKeys(keyData);
        return true; // Swallow the key, including the character message it would produce.
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        // Releasing the modifiers without pressing a key: show the current binding again.
        if (ModifierKeys == Keys.None && !IsWinKeyDown())
        {
            ShowBinding();
        }

        e.Handled = true;
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        ShowBinding();
    }

    private void CaptureKeys(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if (modifiers == Keys.None && (key == Keys.Back || key == Keys.Delete) && !IsWinKeyDown())
        {
            _binding = _originalBinding;
            SetHint(null);
            ShowBinding();
            BindingChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var candidate = HotkeyBinding.FromKeyData(keyData, IsWinKeyDown());
        if (candidate is not { } binding)
        {
            // Only modifiers so far (or a key that can't be used): show what's held.
            ShowPartial(keyData);
            return;
        }

        if (!binding.IsValid)
        {
            SetHint($"{binding} can't be used: add Ctrl, Alt, Shift or Win.");
            ShowBinding();
            return;
        }

        SetHint(null);
        if (_binding != binding)
        {
            _binding = binding;
            ShowBinding();
            BindingChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ShowBinding();
        }
    }

    private void ShowPartial(Keys keyData)
    {
        var parts = new List<string>(5);
        if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
        if ((keyData & Keys.Shift) != 0) parts.Add("Shift");
        if (IsWinKeyDown()) parts.Add("Win");

        if (parts.Count > 0)
        {
            Text = string.Join('+', parts) + "+…";
        }
    }

    private void ShowBinding() => Text = _binding?.ToString() ?? string.Empty;

    private void SetHint(string? hint)
    {
        if (_hint != hint)
        {
            _hint = hint;
            HintChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsDialogNavigationKey(Keys keyData) =>
        (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift) || keyData == Keys.Enter || keyData == Keys.Escape)
        && !IsWinKeyDown();

    private static bool IsWinKeyDown() =>
        (GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
