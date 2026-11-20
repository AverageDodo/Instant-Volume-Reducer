using System.ComponentModel;
using System.Runtime.InteropServices;

namespace InstantVolumeReducer.Hotkeys;

/// <summary>Outcome of <see cref="HotkeyManager.Register"/>.</summary>
public enum HotkeyRegistrationStatus
{
    /// <summary>The hotkey is registered and will raise <see cref="HotkeyManager.Pressed"/>.</summary>
    Registered,

    /// <summary>The binding isn't a usable hotkey (see <see cref="HotkeyBinding.IsValid"/>).</summary>
    InvalidBinding,

    /// <summary>Another application (or Windows) already uses this key combination.</summary>
    AlreadyInUse,

    /// <summary>Registration failed for another reason; see <see cref="HotkeyRegistrationResult.Message"/>.</summary>
    Failed,
}

/// <summary>Result of registering a hotkey, with a message suitable for showing to the user.</summary>
public sealed record HotkeyRegistrationResult(HotkeyRegistrationStatus Status, HotkeyBinding Binding, string Message)
{
    public bool Success => Status == HotkeyRegistrationStatus.Registered;
}

/// <summary>
/// Registers one system-wide hotkey with <c>RegisterHotKey</c> and raises <see cref="Pressed"/> when
/// it's pressed. Uses a hidden message-only window, so the hotkey works without any visible window.
/// </summary>
/// <remarks>
/// Create, use and dispose it on the UI thread: Windows delivers <c>WM_HOTKEY</c> to the thread that
/// registered the hotkey, so that thread needs a message loop (<c>Application.Run</c>).
/// <c>MOD_NOREPEAT</c> is always set, so holding the keys down raises <see cref="Pressed"/> only once.
/// </remarks>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 1;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly MessageWindow _window;
    private bool _disposed;

    public HotkeyManager()
    {
        _window = new MessageWindow(this);
    }

    /// <summary>Raised on the UI thread each time the registered hotkey is pressed.</summary>
    public event EventHandler? Pressed;

    /// <summary>The currently registered hotkey, or <c>null</c> if none is registered.</summary>
    public HotkeyBinding? Current { get; private set; }

    /// <summary>
    /// Registers <paramref name="binding"/>, replacing the current hotkey. If the new hotkey can't be
    /// registered, the previous one is registered again (if that also fails, none is registered and
    /// <see cref="Current"/> becomes <c>null</c>).
    /// </summary>
    public HotkeyRegistrationResult Register(HotkeyBinding binding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!binding.IsValid)
        {
            return new HotkeyRegistrationResult(
                HotkeyRegistrationStatus.InvalidBinding,
                binding,
                $"\"{binding}\" can't be used as a hotkey. Use at least one of Ctrl, Alt, Shift or Win with another key.");
        }

        if (Current == binding)
        {
            return Registered(binding);
        }

        var previous = Current;
        Unregister();

        if (TryRegister(binding, out var error))
        {
            Current = binding;
            return Registered(binding);
        }

        if (previous is { } old && TryRegister(old, out _))
        {
            Current = old;
        }

        return error == ERROR_HOTKEY_ALREADY_REGISTERED
            ? new HotkeyRegistrationResult(
                HotkeyRegistrationStatus.AlreadyInUse,
                binding,
                $"{binding} is already used by another application or by Windows. Choose a different hotkey.")
            : new HotkeyRegistrationResult(
                HotkeyRegistrationStatus.Failed,
                binding,
                $"{binding} couldn't be registered: {new Win32Exception(error).Message}");
    }

    /// <summary>Unregisters the current hotkey, if any.</summary>
    public void Unregister()
    {
        if (Current is null)
        {
            return;
        }

        UnregisterHotKey(_window.Handle, HotkeyId);
        Current = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();
        _window.DestroyHandle();
        _disposed = true;
    }

    private static HotkeyRegistrationResult Registered(HotkeyBinding binding) =>
        new(HotkeyRegistrationStatus.Registered, binding, $"{binding} is registered.");

    private bool TryRegister(HotkeyBinding binding, out int error)
    {
        if (RegisterHotKey(_window.Handle, HotkeyId, binding.NativeModifiers | MOD_NOREPEAT, binding.VirtualKey))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastPInvokeError();
        return false;
    }

    private void OnHotkey() => Pressed?.Invoke(this, EventArgs.Empty);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>A message-only window (no UI, not enumerable) that receives <c>WM_HOTKEY</c>.</summary>
    private sealed class MessageWindow : NativeWindow
    {
        private readonly HotkeyManager _owner;

        public MessageWindow(HotkeyManager owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam == HotkeyId)
            {
                _owner.OnHotkey();
                return;
            }

            base.WndProc(ref m);
        }
    }
}
