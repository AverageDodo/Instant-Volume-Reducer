using System.Security;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Tests.Settings;

/// <summary>Uses an in-memory registry; the real Run key is never touched.</summary>
public class AutostartManagerTests
{
    private const string ExePath = @"C:\Program Files\Instant Volume Reducer\InstantVolumeReducer.exe";
    private const string Quoted = "\"" + ExePath + "\"";

    private sealed class FakeRegistry : IAutostartRegistry
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Writes { get; private set; }

        public Exception? ThrowOnAccess { get; set; }

        public string? GetValue(string name)
        {
            ThrowIfConfigured();
            return Values.TryGetValue(name, out var value) ? value : null;
        }

        public void SetValue(string name, string value)
        {
            ThrowIfConfigured();
            Values[name] = value;
            Writes++;
        }

        public void DeleteValue(string name)
        {
            ThrowIfConfigured();
            Values.Remove(name);
            Writes++;
        }

        private void ThrowIfConfigured()
        {
            if (ThrowOnAccess is not null)
            {
                throw ThrowOnAccess;
            }
        }
    }

    [Fact]
    public void BuildCommand_QuotesPath()
    {
        Assert.Equal(Quoted, AutostartManager.BuildCommand(ExePath));
    }

    [Fact]
    public void Apply_Enabled_AddsQuotedExePath()
    {
        var registry = new FakeRegistry();
        var manager = new AutostartManager(registry, ExePath);

        var result = manager.Apply(true);

        Assert.True(result.Success);
        Assert.Equal(AutostartChange.Added, result.Change);
        Assert.Equal(Quoted, registry.Values[AutostartManager.DefaultValueName]);
    }

    [Fact]
    public void Apply_Enabled_AlreadyRegistered_DoesNotWrite()
    {
        var registry = new FakeRegistry();
        registry.Values[AutostartManager.DefaultValueName] = Quoted.ToUpperInvariant();
        var manager = new AutostartManager(registry, ExePath);

        var result = manager.Apply(true);

        Assert.Equal(AutostartChange.None, result.Change);
        Assert.Equal(0, registry.Writes);
    }

    [Fact]
    public void Apply_Enabled_ExeMoved_UpdatesPath()
    {
        var registry = new FakeRegistry();
        registry.Values[AutostartManager.DefaultValueName] = "\"D:\\old place\\InstantVolumeReducer.exe\"";
        var manager = new AutostartManager(registry, ExePath);

        var result = manager.Apply(true);

        Assert.Equal(AutostartChange.Updated, result.Change);
        Assert.Equal(Quoted, registry.Values[AutostartManager.DefaultValueName]);
    }

    [Fact]
    public void Apply_Disabled_RemovesEntry()
    {
        var registry = new FakeRegistry();
        registry.Values[AutostartManager.DefaultValueName] = Quoted;
        var manager = new AutostartManager(registry, ExePath);

        var result = manager.Apply(false);

        Assert.Equal(AutostartChange.Removed, result.Change);
        Assert.Empty(registry.Values);
    }

    [Fact]
    public void Apply_Disabled_NoEntry_DoesNotWrite()
    {
        var registry = new FakeRegistry();
        var manager = new AutostartManager(registry, ExePath);

        var result = manager.Apply(false);

        Assert.True(result.Success);
        Assert.Equal(AutostartChange.None, result.Change);
        Assert.Equal(0, registry.Writes);
    }

    [Fact]
    public void Apply_LeavesOtherValuesAlone()
    {
        var registry = new FakeRegistry();
        registry.Values["SomeOtherApp"] = "\"C:\\other.exe\"";
        var manager = new AutostartManager(registry, ExePath);

        manager.Apply(true);
        manager.Apply(false);

        Assert.Equal("\"C:\\other.exe\"", Assert.Single(registry.Values).Value);
    }

    [Fact]
    public void Apply_UsesCustomValueName()
    {
        var registry = new FakeRegistry();
        var manager = new AutostartManager(registry, ExePath, "Custom");

        manager.Apply(true);

        Assert.True(registry.Values.ContainsKey("Custom"));
    }

    [Theory]
    [MemberData(nameof(AccessErrors))]
    public void Apply_RegistryError_ReturnsFailureInsteadOfThrowing(Exception error)
    {
        var registry = new FakeRegistry { ThrowOnAccess = error };
        var manager = new AutostartManager(registry, ExePath);

        var result = manager.Apply(true);

        Assert.False(result.Success);
        Assert.Equal(AutostartChange.None, result.Change);
        Assert.Equal(error.Message, result.Error);
    }

    public static TheoryData<Exception> AccessErrors() => new()
    {
        new SecurityException("denied"),
        new UnauthorizedAccessException("no access"),
        new IOException("io failed"),
    };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptyPath(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AutostartManager(new FakeRegistry(), path));
    }
}
