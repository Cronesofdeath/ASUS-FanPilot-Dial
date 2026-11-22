using System;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace FanPilot
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0)
                {
                    switch (args[0])
                    {
                        case "--bridge":
                        case "--demo-bridge":
                            return BridgeHost.Main(args);
                        case "--launch-bridge":
                            return WorkerLauncher.Launch(args);
                        case "--remove-legacy-startup":
                            return StartupTask.RemoveLegacyStartup(args);
                    }
                }

                return RunDesktop(args);
            }
            catch (Exception exception)
            {
                AppPaths.Trace(exception.ToString());
                // Hidden workers report through the launch log rather than displaying SYSTEM UI.
                if (args.Length == 0 || args[0] == "--demo" || args[0] == "--startup")
                    ShowStartupError(exception.Message);
                return 1;
            }
        }

        // Keep WPF out of the hardware-only entry points.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int RunDesktop(string[] args)
        {
            bool demo = args.Length == 1 && args[0] == "--demo";
            if (demo && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS")))
                Environment.SetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS", System.IO.Path.Combine(AppPaths.Data, "simulation.json"));
            bool background = args.Length == 1 && args[0] == "--startup";
            if (args.Length != 0 && !demo && !background)
                return 2;
            string identity = WindowsIdentity.GetCurrent().User.Value;
            using (Mutex single = new Mutex(false, "Local\\FanPilot.Experimental." + identity))
            {
                bool owns;
                try
                {
                    owns = single.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    owns = true;
                }

                if (!owns)
                {
                    MessageBox.Show("ASUS FanPilot Dial is running in the system tray.", "ASUS FanPilot Dial");
                    return 0;
                }

                try
                {
                    Application application = new Application
                    {
                        ShutdownMode = ShutdownMode.OnMainWindowClose
                    };
                    MainWindowController controller = new MainWindowController(demo, background);
                    application.Run(controller.Window);
                }
                finally
                {
                    single.ReleaseMutex();
                }
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ShowStartupError(string message)
        {
            MessageBox.Show(message + "\n\nLog: " + AppPaths.Log, "ASUS FanPilot Dial");
        }
    }
}
