using InstantVolumeReducer.Hotkeys;

namespace InstantVolumeReducer.Tests.Hotkeys;

/// <summary>
/// Uses real <c>RegisterHotKey</c> calls with combinations nobody uses (Ctrl+Alt+Shift+F23/F24, no Win
/// so the Office-key shortcuts aren't hit). Every manager is disposed, which unregisters its hotkey.
/// </summary>
public class HotkeyManagerTests
{
    private static readonly HotkeyBinding UnusedA =
        new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, Keys.F24);

    private static readonly HotkeyBinding UnusedB =
        new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, Keys.F23);

    [Fact]
    public void Register_InvalidBinding_FailsWithoutRegistering()
    {
        using var manager = new HotkeyManager();

        var result = manager.Register(new HotkeyBinding(HotkeyModifiers.None, Keys.V));

        Assert.Equal(HotkeyRegistrationStatus.InvalidBinding, result.Status);
        Assert.False(result.Success);
        Assert.Null(manager.Current);
    }

    [Fact]
    public void Register_TakenCombination_ReportsAlreadyInUse_AndKeepsPreviousHotkey()
    {
        using var first = new HotkeyManager();
        using var second = new HotkeyManager();

        Assert.True(first.Register(UnusedA).Success);
        Assert.True(second.Register(UnusedB).Success);

        var result = second.Register(UnusedA);

        Assert.Equal(HotkeyRegistrationStatus.AlreadyInUse, result.Status);
        Assert.Equal(UnusedA, result.Binding);
        Assert.Equal(UnusedB, second.Current);
        Assert.Equal(UnusedA, first.Current);
    }

    [Fact]
    public void Register_SameBindingAgain_Succeeds()
    {
        using var manager = new HotkeyManager();

        Assert.True(manager.Register(UnusedA).Success);
        Assert.True(manager.Register(UnusedA).Success);
        Assert.Equal(UnusedA, manager.Current);
    }

    [Fact]
    public void Register_NewBinding_ReleasesTheOldOne()
    {
        using var manager = new HotkeyManager();
        using var other = new HotkeyManager();

        Assert.True(manager.Register(UnusedA).Success);
        Assert.True(manager.Register(UnusedB).Success);

        Assert.True(other.Register(UnusedA).Success);
    }

    [Fact]
    public void Dispose_UnregistersTheHotkey()
    {
        using (var manager = new HotkeyManager())
        {
            Assert.True(manager.Register(UnusedA).Success);
        }

        using var other = new HotkeyManager();
        Assert.True(other.Register(UnusedA).Success);
    }

    [Fact]
    public void Unregister_ClearsCurrent()
    {
        using var manager = new HotkeyManager();
        Assert.True(manager.Register(UnusedA).Success);

        manager.Unregister();

        Assert.Null(manager.Current);
    }
}
