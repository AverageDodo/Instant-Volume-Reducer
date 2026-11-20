using System.Security;
using Microsoft.Win32;

namespace InstantVolumeReducer.Settings;

/// <summary>
/// The registry values the autostart feature reads and writes. The real implementation is
/// <see cref="RunKeyRegistry"/>; tests use an in-memory fake so they never touch the real registry.
/// </summary>
public interface IAutostartRegistry
{
    /// <summary>Returns the string value, or <c>null</c> if it doesn't exist.</summary>
    string? GetValue(string name);

    void SetValue(string name, string value);

    /// <summary>Deletes the value; does nothing if it doesn't exist.</summary>
    void DeleteValue(string name);
}

/// <summary>
/// <c>HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run</c>: programs started when the
/// current user signs in. Per-user, so no admin rights are needed.
/// </summary>
public sealed class RunKeyRegistry : IAutostartRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(name) as string;
    }

    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>What <see cref="AutostartManager.Apply"/> changed.</summary>
public enum AutostartChange
{
    /// <summary>The registry already matched the setting.</summary>
    None,

    /// <summary>The autostart entry was created.</summary>
    Added,

    /// <summary>The entry existed with a different command (e.g. the exe was moved) and was updated.</summary>
    Updated,

    /// <summary>The autostart entry was removed.</summary>
    Removed,
}

/// <summary>The result of <see cref="AutostartManager.Apply"/>.</summary>
/// <param name="Change">What was changed; <see cref="AutostartChange.None"/> if it failed.</param>
/// <param name="Error">Why the registry couldn't be read or written; <c>null</c> on success.</param>
public sealed record AutostartResult(AutostartChange Change, string? Error = null)
{
    public bool Success => Error is null;
}

/// <summary>
/// Keeps the "Start with Windows" entry in the current user's Run key in line with the setting.
/// </summary>
public sealed class AutostartManager
{
    /// <summary>The name of the value in the Run key.</summary>
    public const string DefaultValueName = "InstantVolumeReducer";

    private readonly IAutostartRegistry _registry;
    private readonly string _valueName;

    /// <param name="registry">The Run key (or a fake in tests).</param>
    /// <param name="executablePath">The full path of the app's .exe, which Windows starts at sign-in.</param>
    /// <param name="valueName">The name of the Run key value.</param>
    public AutostartManager(IAutostartRegistry registry, string executablePath, string valueName = DefaultValueName)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        Command = BuildCommand(executablePath);
        _valueName = valueName;
    }

    /// <summary>The command written to the Run key.</summary>
    public string Command { get; }

    /// <summary>The exe path in quotes, so a path with spaces isn't split into program and arguments.</summary>
    public static string BuildCommand(string executablePath) => $"\"{executablePath}\"";

    /// <summary>
    /// Makes the registry match <paramref name="enabled"/>: creates the entry, updates it if it points
    /// somewhere else (the exe was moved), or removes it. Never throws for registry access problems.
    /// </summary>
    public AutostartResult Apply(bool enabled)
    {
        try
        {
            var current = _registry.GetValue(_valueName);
            if (enabled)
            {
                // Windows paths are case-insensitive, so a difference in case isn't a different exe.
                if (string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                {
                    return new AutostartResult(AutostartChange.None);
                }

                _registry.SetValue(_valueName, Command);
                return new AutostartResult(current is null ? AutostartChange.Added : AutostartChange.Updated);
            }

            if (current is null)
            {
                return new AutostartResult(AutostartChange.None);
            }

            _registry.DeleteValue(_valueName);
            return new AutostartResult(AutostartChange.Removed);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return new AutostartResult(AutostartChange.None, ex.Message);
        }
    }
}
