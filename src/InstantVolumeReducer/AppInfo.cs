using System.Reflection;

namespace InstantVolumeReducer;

/// <summary>The app's name and version, as shown in the tray menu and the settings dialog.</summary>
public static class AppInfo
{
    public const string Name = "Instant Volume Reducer";

    /// <summary>The GitHub repository whose releases the installed app updates from.</summary>
    public const string RepositoryUrl = "https://github.com/AverageDodo/Instant-Volume-Reducer";

    /// <summary>
    /// The release version, e.g. <c>0.2.0</c> or <c>0.3.0-beta.1</c>. It comes from <c>&lt;Version&gt;</c> in
    /// the project file, which is also what the release workflow tags and publishes.
    /// </summary>
    public static string Version { get; } = ReadVersion(typeof(AppInfo).Assembly);

    /// <summary><c>true</c> for a pre-release version such as <c>0.3.0-beta.1</c>.</summary>
    public static bool IsPrerelease => Version.Contains('-');

    /// <summary>
    /// Reads the informational version without the build metadata the SDK appends (<c>+&lt;commit hash&gt;</c>).
    /// </summary>
    public static string ReadVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        return StripBuildMetadata(informational);
    }

    /// <summary>Removes SemVer build metadata: <c>1.2.3-beta.1+abc123</c> becomes <c>1.2.3-beta.1</c>.</summary>
    public static string StripBuildMetadata(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var plus = version.IndexOf('+');
        return (plus >= 0 ? version[..plus] : version).Trim();
    }
}
