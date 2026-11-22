using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using FanPilot;

static class MemoryProbe
{
    [STAThread]
    static int Main()
    {
        string folder = AppDomain.CurrentDomain.BaseDirectory;
        string result = Path.Combine(folder, "memory.txt");
        Environment.SetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS", Path.Combine(folder, "preferences.json"));
        Environment.SetEnvironmentVariable("FANPILOT_TEST_SETTINGS", Path.Combine(folder, "preferences.json"));
        new Preferences
        {
            FollowGHelper = false,
            UseCurveControl = false,
            CloseToTray = false
        }.Save();
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        var controller = new MainWindowController(true, true, true);
        bool started = false;
        int exitCode = 1;
        controller.Window.ContentRendered += async delegate
        {
            if (started)
                return;
            started = true;
            try
            {
                await controller.Ready;
                await controller.WakeAsync();
                if (controller.Window.IsVisible || !controller.CurrentSnapshot.Connected || !controller.CurrentSnapshot.Demo || controller.CurrentSnapshot.Manual)
                    throw new InvalidOperationException("The tray simulation did not connect in firmware mode.");
                await Task.Delay(4000);
                using (Process worker = FindWorker(folder))
                using (Process dashboard = Process.GetCurrentProcess())
                {
                    var dashboardMemory = new List<long>();
                    var workerMemory = new List<long>();
                    var elapsed = Stopwatch.StartNew();
                    var dashboardCpu = dashboard.TotalProcessorTime;
                    var workerCpu = worker.TotalProcessorTime;
                    for (int sample = 0; sample < 9; sample++)
                    {
                        dashboard.Refresh();
                        worker.Refresh();
                        dashboardMemory.Add(dashboard.PrivateMemorySize64);
                        workerMemory.Add(worker.PrivateMemorySize64);
                        await Task.Delay(1000);
                    }

                    dashboard.Refresh();
                    worker.Refresh();
                    double seconds = elapsed.Elapsed.TotalSeconds;
                    bool workerWpf = worker.Modules.Cast<ProcessModule>().Any(module => module.ModuleName.StartsWith("PresentationFramework", StringComparison.OrdinalIgnoreCase));
                    File.WriteAllText(result, String.Format("dashboard={0:F1} MiB; worker={1:F1} MiB; combined={2:F1} MiB; worker_wpf_loaded={3}; dashboard CPU={4:F2}% of one core; worker CPU={5:F2}% of one core", Median(dashboardMemory), Median(workerMemory), Median(dashboardMemory) + Median(workerMemory), workerWpf, 100 * (dashboard.TotalProcessorTime - dashboardCpu).TotalSeconds / seconds, 100 * (worker.TotalProcessorTime - workerCpu).TotalSeconds / seconds));
                    exitCode = 0;
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(result, ex.ToString());
            }

            await controller.ExitAsync(true);
        };
        application.Run(controller.Window);
        return exitCode;
    }

    static double Median(List<long> values)
    {
        return values.OrderBy(value => value).ElementAt(values.Count / 2) / 1048576.0;
    }

    static Process FindWorker(string folder)
    {
        foreach (Process candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(typeof(Preferences).Assembly.Location)))
        {
            try
            {
                if (String.Equals(Path.GetDirectoryName(candidate.MainModule.FileName), folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            candidate.Dispose();
        }

        throw new InvalidOperationException("Could not identify the isolated simulated worker.");
    }
}
