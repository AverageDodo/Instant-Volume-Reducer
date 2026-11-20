using InstantVolumeReducer.Hotkeys;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Tests.Hotkeys;

public class HotkeyBindingTests
{
    [Theory]
    [InlineData("Ctrl+Alt+V", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.V)]
    [InlineData("Ctrl+Shift+F12", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, Keys.F12)]
    [InlineData("Win+Alt+Shift+Ctrl+M", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win, Keys.M)]
    [InlineData("F13", HotkeyModifiers.None, Keys.F13)]
    [InlineData("VolumeMute", HotkeyModifiers.None, Keys.VolumeMute)]
    [InlineData("MediaPlayPause", HotkeyModifiers.None, Keys.MediaPlayPause)]
    [InlineData("Alt+1", HotkeyModifiers.Alt, Keys.D1)]
    [InlineData("Alt+D1", HotkeyModifiers.Alt, Keys.D1)]
    [InlineData("Ctrl+Enter", HotkeyModifiers.Ctrl, Keys.Enter)]
    [InlineData("Ctrl+Return", HotkeyModifiers.Ctrl, Keys.Enter)]
    [InlineData("Ctrl+PageDown", HotkeyModifiers.Ctrl, Keys.PageDown)]
    [InlineData("Ctrl+NumPad5", HotkeyModifiers.Ctrl, Keys.NumPad5)]
    [InlineData("Shift+Space", HotkeyModifiers.Shift, Keys.Space)]
    public void TryParse_ParsesValidText(string text, HotkeyModifiers modifiers, Keys key)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(new HotkeyBinding(modifiers, key), binding);
    }

    [Theory]
    [InlineData("ctrl+alt+v")]
    [InlineData("CONTROL+ALT+V")]
    [InlineData(" Ctrl + Alt + V ")]
    public void TryParse_IgnoresCaseWhitespaceAndAliases(string text)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(HotkeyBinding.Default, binding);
    }

    [Fact]
    public void TryParse_AcceptsWindowsAlias()
    {
        Assert.True(HotkeyBinding.TryParse("Windows+F1", out var binding));
        Assert.Equal(new HotkeyBinding(HotkeyModifiers.Win, Keys.F1), binding);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("+V")]
    [InlineData("Ctrl++V")]
    [InlineData("Ctrl+Ctrl+V")]
    [InlineData("Ctrl+Hyper+V")]
    [InlineData("Ctrl+Alt+NotAKey")]
    [InlineData("Ctrl+V+Alt")]
    [InlineData("Ctrl+65")]
    [InlineData("Ctrl+A, Shift")]
    [InlineData("Ctrl+KeyCode")]
    [InlineData("Ctrl+Modifiers")]
    public void TryParse_RejectsMalformedText(string? text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(default(HotkeyBinding), binding);
    }

    [Theory]
    [InlineData("V")]
    [InlineData("F12")]
    [InlineData("Space")]
    [InlineData("1")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+ShiftKey")]
    [InlineData("Ctrl+ControlKey")]
    [InlineData("Ctrl+LWin")]
    [InlineData("Alt+Menu")]
    [InlineData("Ctrl+LButton")]
    [InlineData("Ctrl+None")]
    public void TryParse_RejectsBindingsThatBreakTheValidityRule(string text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out _));
    }

    [Theory]
    [InlineData(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.V, "Ctrl+Alt+V")]
    [InlineData(HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, Keys.M, "Ctrl+Alt+Shift+Win+M")]
    [InlineData(HotkeyModifiers.None, Keys.F13, "F13")]
    [InlineData(HotkeyModifiers.Alt, Keys.D1, "Alt+1")]
    [InlineData(HotkeyModifiers.Ctrl, Keys.Return, "Ctrl+Enter")]
    [InlineData(HotkeyModifiers.Ctrl, Keys.Next, "Ctrl+PageDown")]
    [InlineData(HotkeyModifiers.Ctrl, Keys.Back, "Ctrl+Backspace")]
    public void ToString_FormatsModifiersInFixedOrder(HotkeyModifiers modifiers, Keys key, string expected)
    {
        Assert.Equal(expected, new HotkeyBinding(modifiers, key).ToString());
    }

    public static TheoryData<Keys> AllUsableKeys()
    {
        var data = new TheoryData<Keys>();
        foreach (var key in Enum.GetValues<Keys>().Distinct())
        {
            if (HotkeyBinding.FromKeyData(key) is not null)
            {
                data.Add(key);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllUsableKeys))]
    public void ToString_RoundTripsThroughTryParse_ForEveryKey(Keys key)
    {
        var binding = new HotkeyBinding(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, key);

        Assert.True(HotkeyBinding.TryParse(binding.ToString(), out var parsed));
        Assert.Equal(binding, parsed);
    }

    [Fact]
    public void Default_MatchesTheSettingsDefault()
    {
        Assert.True(HotkeyBinding.TryParse(AppSettings.DefaultHotkey, out var parsed));
        Assert.Equal(HotkeyBinding.Default, parsed);
        Assert.Equal(AppSettings.DefaultHotkey, HotkeyBinding.Default.ToString());
    }

    [Theory]
    [InlineData(HotkeyModifiers.Ctrl, Keys.V, true)]
    [InlineData(HotkeyModifiers.Win, Keys.F1, true)]
    [InlineData(HotkeyModifiers.None, Keys.V, false)]
    [InlineData(HotkeyModifiers.None, Keys.F12, false)]
    [InlineData(HotkeyModifiers.None, Keys.F13, true)]
    [InlineData(HotkeyModifiers.None, Keys.F24, true)]
    [InlineData(HotkeyModifiers.None, Keys.VolumeUp, true)]
    [InlineData(HotkeyModifiers.None, Keys.VolumeDown, true)]
    [InlineData(HotkeyModifiers.None, Keys.VolumeMute, true)]
    [InlineData(HotkeyModifiers.None, Keys.MediaNextTrack, true)]
    [InlineData(HotkeyModifiers.None, Keys.MediaPreviousTrack, true)]
    [InlineData(HotkeyModifiers.None, Keys.MediaStop, true)]
    [InlineData(HotkeyModifiers.Ctrl, Keys.None, false)]
    [InlineData(HotkeyModifiers.Ctrl, Keys.ControlKey, false)]
    [InlineData(HotkeyModifiers.Ctrl, Keys.RShiftKey, false)]
    [InlineData(HotkeyModifiers.Ctrl, Keys.RWin, false)]
    [InlineData(HotkeyModifiers.Ctrl, Keys.XButton1, false)]
    [InlineData(HotkeyModifiers.Ctrl, Keys.V | Keys.Shift, false)]
    [InlineData((HotkeyModifiers)0x10, Keys.V, false)]
    public void IsValid_RequiresAModifierExceptForStandaloneKeys(HotkeyModifiers modifiers, Keys key, bool expected)
    {
        Assert.Equal(expected, new HotkeyBinding(modifiers, key).IsValid);
    }

    [Fact]
    public void DefaultValue_IsNotValid()
    {
        Assert.False(default(HotkeyBinding).IsValid);
    }

    [Theory]
    [InlineData(HotkeyModifiers.None, 0x0000u)]
    [InlineData(HotkeyModifiers.Alt, 0x0001u)]
    [InlineData(HotkeyModifiers.Ctrl, 0x0002u)]
    [InlineData(HotkeyModifiers.Shift, 0x0004u)]
    [InlineData(HotkeyModifiers.Win, 0x0008u)]
    [InlineData(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win, 0x000Fu)]
    public void NativeModifiers_MapToWin32ModFlags(HotkeyModifiers modifiers, uint expected)
    {
        Assert.Equal(expected, new HotkeyBinding(modifiers, Keys.F13).NativeModifiers);
    }

    [Theory]
    [InlineData(Keys.V, 0x56u)]
    [InlineData(Keys.F13, 0x7Cu)]
    [InlineData(Keys.VolumeMute, 0xADu)]
    [InlineData(Keys.D1, 0x31u)]
    public void VirtualKey_IsTheWin32KeyCode(Keys key, uint expected)
    {
        Assert.Equal(expected, new HotkeyBinding(HotkeyModifiers.Ctrl, key).VirtualKey);
    }

    [Theory]
    [InlineData(Keys.Control | Keys.Alt | Keys.V, false, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.V)]
    [InlineData(Keys.Shift | Keys.F12, true, HotkeyModifiers.Shift | HotkeyModifiers.Win, Keys.F12)]
    [InlineData(Keys.F13, false, HotkeyModifiers.None, Keys.F13)]
    [InlineData(Keys.V, false, HotkeyModifiers.None, Keys.V)]
    public void FromKeyData_SplitsModifiersAndKey(Keys keyData, bool win, HotkeyModifiers modifiers, Keys key)
    {
        Assert.Equal(new HotkeyBinding(modifiers, key), HotkeyBinding.FromKeyData(keyData, win));
    }

    [Theory]
    [InlineData(Keys.Control | Keys.ControlKey)]
    [InlineData(Keys.Shift | Keys.ShiftKey)]
    [InlineData(Keys.Alt | Keys.Menu)]
    [InlineData(Keys.LWin)]
    [InlineData(Keys.Control)]
    [InlineData(Keys.None)]
    public void FromKeyData_ReturnsNullWhileOnlyModifiersArePressed(Keys keyData)
    {
        Assert.Null(HotkeyBinding.FromKeyData(keyData));
    }

    [Fact]
    public void FromKeyData_LetterWithoutModifier_IsReturnedButNotValid()
    {
        var binding = HotkeyBinding.FromKeyData(Keys.V);

        Assert.NotNull(binding);
        Assert.False(binding.Value.IsValid);
    }

    [Fact]
    public void FromKeyEventArgs_UsesKeyData()
    {
        var e = new KeyEventArgs(Keys.Control | Keys.Alt | Keys.V);

        Assert.Equal(HotkeyBinding.Default, HotkeyBinding.FromKeyEventArgs(e));
    }
}
