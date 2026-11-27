using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Core;
using InstantVolumeReducer.Settings;
using Velopack;

namespace InstantVolumeReducer.Updates;

/// <summary>
/// Velopack's install, update and uninstall hooks. <see cref="Run"/> must be the first thing the app does:
/// when Velopack starts the exe for a hook, the hook runs and the process exits inside it.
/// </summary>
internal static class InstallHooks
{
    public static void Run()
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => BeforeUninstall())
            .Run();
    }

    /// <summary>
    /// Runs while the app is being uninstalled (at most 30 seconds, no UI). Undoes what the app changed
    /// outside its install folder: a volume that's still reduced (restored from the saved state, the same
    /// way as after a crash) and the "Start with Windows" entry. Settings in <c>%APPDATA%</c> are kept, so
    /// a reinstall picks them up again. Never throws: a failing cleanup must not block the uninstall.
    /// </summary>
    private static void BeforeUninstall()
    {
        try
        {
            using var audio = new CoreAudioDeviceService();
            CrashRecovery.Run(new ReductionStateStore(), audio);
        }
        catch (Exception)
        {
            // Best effort; the volume can still be set by hand.
        }

        try
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            new AutostartManager(new RunKeyRegistry(), exePath).Apply(enabled: false);
        }
        catch (Exception)
        {
            // Best effort; the entry would just point to a missing file.
        }
    }
}
