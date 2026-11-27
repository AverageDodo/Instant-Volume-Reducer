using InstantVolumeReducer.Updates;
using Velopack;

namespace InstantVolumeReducer;

internal static class Program
{
    /// <summary>
    /// Name of the mutex that keeps the app to one instance per Windows session. <c>Local\</c> scopes it
    /// to the session, so each signed-in user (e.g. with fast user switching) can run their own instance.
    /// </summary>
    private const string SingleInstanceMutexName = @"Local\InstantVolumeReducer-3F9C2E71-5B8A-4D0E-9A6B-2C7E1D4F8A90";

    [STAThread]
    private static void Main()
    {
        // Must come first, directly in Main (vpk pack checks for it here): during install, update and
        // uninstall, Velopack starts the exe to run a hook, and the process exits in Run(). It also applies
        // a downloaded update on a normal start.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => InstallHooks.BeforeUninstall())
            .Run();

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance is already running in this session; leave quietly.
            return;
        }

        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext();
        Application.Run(context);
    }
}
