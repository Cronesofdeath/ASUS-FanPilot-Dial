using System;
using System.IO;
using System.Windows;
using FanPilot;

internal static class UiTestProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        string output = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output", "ui");
        Directory.CreateDirectory(output);
        string result = Path.Combine(output, "result.txt");
        if (File.Exists(result))
            File.Delete(result);
        Environment.SetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS", Path.Combine(output, "settings.json"));
        string pendingReset = Preferences.FilePath + ".demo-reset-pending";
        try
        {
            new Preferences
            {
                Modifiers = 6,
                HotkeyEnabled = true,
                UseCurveControl = false,
                Hotkey = 121
            }.Save();
            File.WriteAllText(pendingReset, "simulated unconfirmed previous session");
            Application application = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            MainWindowController controller = new MainWindowController(true, true, true);
            bool started = false, passed = false;
            controller.Window.ContentRendered += async delegate
            {
                if (started)
                    return;
                started = true;
                Exception failure = null;
                try
                {
                    await controller.Ready;
                    if (controller.Window.IsVisible || controller.CurrentSnapshot.Connected || controller.CurrentSnapshot.ReleaseConfirmed || !File.Exists(pendingReset))
                        throw new InvalidOperationException("Sign-in startup opened the window or started the hardware helper.");
                    controller.Show();
                    await controller.WakeAsync();
                    if (!controller.CurrentSnapshot.Connected || !controller.CurrentSnapshot.ReleaseConfirmed || File.Exists(pendingReset))
                        throw new InvalidOperationException("Opening the dashboard did not resolve the pending firmware reset.");
                    passed = await new UiSmokeTests(controller, output).RunAsync();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                if (failure != null)
                {
                    File.WriteAllText(result, "FAIL: " + failure);
                    await controller.ExitAsync(true);
                }
            };
            application.Run(controller.Window);
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            File.WriteAllText(result, "FAIL startup: " + exception);
            return 1;
        }
    }
}
