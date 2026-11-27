using System.Text.Encodings.Web;
using System.Text.Json;
using InstantVolumeReducer.Hotkeys;

namespace InstantVolumeReducer.Settings;

/// <summary>How <see cref="SettingsStore.Load"/> found the settings file.</summary>
public enum SettingsLoadStatus
{
    /// <summary>The file was read successfully.</summary>
    Loaded,

    /// <summary>There is no settings file yet (first run). Defaults are returned.</summary>
    Missing,

    /// <summary>
    /// The file couldn't be read or isn't valid settings JSON. Defaults are returned and, if possible,
    /// the bad file was copied to <see cref="SettingsLoadResult.BackupPath"/>.
    /// </summary>
    Corrupt,
}

/// <summary>The result of <see cref="SettingsStore.Load"/>.</summary>
/// <param name="Settings">The loaded (and normalized) settings, or defaults.</param>
/// <param name="Status">Whether the file was loaded, missing or corrupt.</param>
/// <param name="BackupPath">Where a corrupt file was copied to; <c>null</c> if there was no backup.</param>
public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadStatus Status, string? BackupPath = null);

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as indented JSON, by default at
/// <c>%APPDATA%\InstantVolumeReducer\settings.json</c>.
/// </summary>
public sealed class SettingsStore
{
    // Relaxed escaping keeps the file readable and hand-editable ("Ctrl+Alt+V" instead of "Ctrl+Alt+V",
    // non-ASCII device names as they are). Safe here: the JSON never ends up in HTML or a script.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="filePath">The settings file. <c>null</c> uses <see cref="DefaultFilePath"/>; tests pass a temp path.</param>
    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultFilePath;
    }

    /// <summary><c>%APPDATA%\InstantVolumeReducer\settings.json</c>.</summary>
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "InstantVolumeReducer",
        "settings.json");

    /// <summary>The settings file this store reads and writes.</summary>
    public string FilePath { get; }

    /// <summary>Where a corrupt settings file is copied to before it gets overwritten.</summary>
    public string BackupFilePath => FilePath + ".bak";

    /// <summary>
    /// Reads the settings file. Never throws for a missing, unreadable or invalid file: it returns defaults
    /// and says why in <see cref="SettingsLoadResult.Status"/>. Loaded values are normalized (see
    /// <see cref="Normalize"/>).
    /// </summary>
    public SettingsLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Missing);
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new JsonException("The settings file contains null.");
            return new SettingsLoadResult(Normalize(settings), SettingsLoadStatus.Loaded);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Corrupt, TryBackUpBadFile());
        }
    }

    /// <summary>
    /// Writes the settings atomically: to a temporary file next to the settings file, which then replaces
    /// it. A crash or full disk while saving can't leave a half-written settings file behind.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or file isn't writable.</exception>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AtomicFile.WriteJson(FilePath, settings, JsonOptions);
    }

    /// <summary>
    /// Makes loaded settings safe to use, since the file may have been edited by hand: a missing or invalid
    /// hotkey becomes the default (and a valid one is written in its standard form), amounts are clamped
    /// to 0–100, unknown modes become the default mode, and device entries without an ID, or repeating
    /// an earlier entry's ID, are dropped. Missing sections get their defaults, unknown overlay choices
    /// their default value, and the overlay opacity is clamped.
    /// </summary>
    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Hotkey = HotkeyBinding.TryParse(settings.Hotkey, out var hotkey)
            ? hotkey.ToString()
            : AppSettings.DefaultHotkey;
        settings.DefaultMode = NormalizeMode(settings.DefaultMode);
        settings.DefaultAmount = ClampAmount(settings.DefaultAmount);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var devices = new List<DeviceReductionConfig>();
        foreach (var device in settings.Devices ?? [])
        {
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceId) || !seen.Add(device.DeviceId))
            {
                continue;
            }

            device.LastKnownName ??= string.Empty;
            device.Mode = NormalizeMode(device.Mode);
            device.Amount = ClampAmount(device.Amount);
            devices.Add(device);
        }

        settings.Devices = devices;

        settings.WindowsDefaultDevice ??= new WindowsDefaultDeviceConfig();
        settings.WindowsDefaultDevice.Mode = NormalizeMode(settings.WindowsDefaultDevice.Mode);
        settings.WindowsDefaultDevice.Amount = ClampAmount(settings.WindowsDefaultDevice.Amount);

        var overlay = settings.Overlay ??= new OverlaySettings();
        var overlayDefaults = new OverlaySettings();
        overlay.Corner = Enum.IsDefined(overlay.Corner) ? overlay.Corner : overlayDefaults.Corner;
        overlay.Size = Enum.IsDefined(overlay.Size) ? overlay.Size : overlayDefaults.Size;
        overlay.Monitor = Enum.IsDefined(overlay.Monitor) ? overlay.Monitor : overlayDefaults.Monitor;
        overlay.Opacity = Math.Clamp(overlay.Opacity, OverlaySettings.MinOpacity, OverlaySettings.MaxOpacity);
        return settings;
    }

    private static ReductionMode NormalizeMode(ReductionMode mode) =>
        Enum.IsDefined(mode) ? mode : AppSettings.DefaultReductionMode;

    private static int ClampAmount(int amount) =>
        Math.Clamp(amount, DeviceReductionConfig.MinAmount, DeviceReductionConfig.MaxAmount);

    /// <summary>Copies the bad file to <see cref="BackupFilePath"/>; returns that path, or <c>null</c> if the copy failed.</summary>
    private string? TryBackUpBadFile()
    {
        try
        {
            File.Copy(FilePath, BackupFilePath, overwrite: true);
            return BackupFilePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
