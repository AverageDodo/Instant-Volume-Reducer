namespace InstantVolumeReducer.Hotkeys;

/// <summary>
/// Modifier keys of a hotkey. The values match the Win32 <c>MOD_*</c> flags of <c>RegisterHotKey</c>.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,     // MOD_ALT
    Ctrl = 0x0002,    // MOD_CONTROL
    Shift = 0x0004,   // MOD_SHIFT
    Win = 0x0008,     // MOD_WIN
}

/// <summary>
/// A global hotkey: modifier keys plus one key. Its text form (e.g. <c>Ctrl+Alt+V</c>) is what the
/// settings file stores; <see cref="TryParse"/> and <see cref="ToString"/> round-trip it.
/// </summary>
public readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, Keys Key)
{
    private const char Separator = '+';
    private const HotkeyModifiers AllModifiers =
        HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win;

    // Formatting order of the modifiers.
    private static readonly (HotkeyModifiers Modifier, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Ctrl, "Ctrl"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Shift, "Shift"),
        (HotkeyModifiers.Win, "Win"),
    ];

    // Accepted spellings when parsing (case-insensitive).
    private static readonly Dictionary<string, HotkeyModifiers> ModifierAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = HotkeyModifiers.Ctrl,
            ["Control"] = HotkeyModifiers.Ctrl,
            ["Alt"] = HotkeyModifiers.Alt,
            ["Shift"] = HotkeyModifiers.Shift,
            ["Win"] = HotkeyModifiers.Win,
            ["Windows"] = HotkeyModifiers.Win,
        };

    // Friendlier names for keys whose enum name is unusual or ambiguous (several Keys names share a value).
    private static readonly Dictionary<Keys, string> KeyDisplayNames = new()
    {
        [Keys.D0] = "0", [Keys.D1] = "1", [Keys.D2] = "2", [Keys.D3] = "3", [Keys.D4] = "4",
        [Keys.D5] = "5", [Keys.D6] = "6", [Keys.D7] = "7", [Keys.D8] = "8", [Keys.D9] = "9",
        [Keys.Enter] = "Enter",
        [Keys.PageUp] = "PageUp",
        [Keys.PageDown] = "PageDown",
        [Keys.CapsLock] = "CapsLock",
        [Keys.Back] = "Backspace",
        [Keys.Scroll] = "ScrollLock",
        [Keys.Snapshot] = "PrintScreen",
    };

    // Keys that can't be the main key of a hotkey: modifiers themselves, mouse buttons and masks.
    private static readonly HashSet<Keys> NonHotkeyKeys =
    [
        Keys.None, Keys.KeyCode,
        Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
        Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
        Keys.Menu, Keys.LMenu, Keys.RMenu,
        Keys.LWin, Keys.RWin,
        Keys.LButton, Keys.RButton, Keys.MButton, Keys.XButton1, Keys.XButton2,
    ];

    // Keys that are allowed without a modifier: nobody types them by accident.
    private static readonly HashSet<Keys> StandaloneKeys =
    [
        Keys.F13, Keys.F14, Keys.F15, Keys.F16, Keys.F17, Keys.F18,
        Keys.F19, Keys.F20, Keys.F21, Keys.F22, Keys.F23, Keys.F24,
        Keys.VolumeMute, Keys.VolumeDown, Keys.VolumeUp,
        Keys.MediaNextTrack, Keys.MediaPreviousTrack, Keys.MediaStop, Keys.MediaPlayPause,
    ];

    // Declared after the sets above: static fields initialize in textual order and this one uses them.
    private static readonly Dictionary<string, Keys> KeyNameLookup = BuildKeyNameLookup();

    /// <summary>The default hotkey, <c>Ctrl+Alt+V</c>.</summary>
    public static HotkeyBinding Default { get; } = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.V);

    /// <summary>
    /// Whether this binding can be used as a global hotkey: the key is a real, non-modifier key, and at
    /// least one modifier is set, except for F13–F24 and media/volume keys, which may stand alone.
    /// </summary>
    public bool IsValid =>
        IsUsableKey(Key)
        && (Modifiers & ~AllModifiers) == 0
        && (Modifiers != HotkeyModifiers.None || StandaloneKeys.Contains(Key));

    /// <summary>The modifiers as Win32 <c>MOD_*</c> flags (without <c>MOD_NOREPEAT</c>).</summary>
    public uint NativeModifiers => (uint)(Modifiers & AllModifiers);

    /// <summary>The key as a Win32 virtual-key code.</summary>
    public uint VirtualKey => (uint)(Key & Keys.KeyCode);

    /// <summary>
    /// Parses the text form, e.g. <c>Ctrl+Alt+V</c>, <c>ctrl + shift + f12</c> or <c>F13</c>.
    /// Modifiers come first, the key last; parsing is case-insensitive. Succeeds only for bindings
    /// that are <see cref="IsValid"/>.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var tokens = text.Split(Separator, StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (!ModifierAliases.TryGetValue(tokens[i], out var modifier) || (modifiers & modifier) != 0)
            {
                return false; // Unknown, empty or repeated modifier.
            }

            modifiers |= modifier;
        }

        if (!KeyNameLookup.TryGetValue(tokens[^1], out var key))
        {
            return false;
        }

        var candidate = new HotkeyBinding(modifiers, key);
        if (!candidate.IsValid)
        {
            return false;
        }

        binding = candidate;
        return true;
    }

    /// <summary>
    /// Builds a binding from a WinForms key value such as <see cref="KeyEventArgs.KeyData"/>.
    /// WinForms doesn't report the Windows key as a modifier, so pass <paramref name="win"/> separately.
    /// Returns <c>null</c> when the key part is missing, a modifier or a mouse button (e.g. while the
    /// user is still holding only Ctrl). The result may still be not <see cref="IsValid"/>, e.g. a
    /// letter without modifiers.
    /// </summary>
    public static HotkeyBinding? FromKeyData(Keys keyData, bool win = false)
    {
        var key = keyData & Keys.KeyCode;
        if (!IsUsableKey(key))
        {
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        if ((keyData & Keys.Control) != 0) modifiers |= HotkeyModifiers.Ctrl;
        if ((keyData & Keys.Alt) != 0) modifiers |= HotkeyModifiers.Alt;
        if ((keyData & Keys.Shift) != 0) modifiers |= HotkeyModifiers.Shift;
        if (win) modifiers |= HotkeyModifiers.Win;

        return new HotkeyBinding(modifiers, key);
    }

    /// <summary>Builds a binding from a key event; see <see cref="FromKeyData"/>.</summary>
    public static HotkeyBinding? FromKeyEventArgs(KeyEventArgs e, bool win = false) => FromKeyData(e.KeyData, win);

    /// <summary>The text form, e.g. <c>Ctrl+Alt+V</c>. Modifiers are always in the order Ctrl, Alt, Shift, Win.</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        foreach (var (modifier, name) in ModifierNames)
        {
            if ((Modifiers & modifier) != 0)
            {
                parts.Add(name);
            }
        }

        parts.Add(GetKeyName(Key));
        return string.Join(Separator, parts);
    }

    private static bool IsUsableKey(Keys key) =>
        (key & ~Keys.KeyCode) == 0 && !NonHotkeyKeys.Contains(key) && Enum.IsDefined(key);

    private static string GetKeyName(Keys key) =>
        KeyDisplayNames.TryGetValue(key, out var name) ? name : key.ToString();

    private static Dictionary<string, Keys> BuildKeyNameLookup()
    {
        var lookup = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<Keys>())
        {
            var key = Enum.Parse<Keys>(name);
            if (IsUsableKey(key))
            {
                lookup.TryAdd(name, key);
            }
        }

        foreach (var (key, name) in KeyDisplayNames)
        {
            lookup[name] = key;
        }

        return lookup;
    }
}
