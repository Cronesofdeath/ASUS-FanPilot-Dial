using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace FanPilot
{
    public static class StartupTask
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Entry = "ASUS FanPilot Dial";
        static string LegacyMarker
        {
            get
            {
                return Path.Combine(AppPaths.Data, "startup-enabled");
            }
        }

        internal static bool LegacyEnabled
        {
            get
            {
                return File.Exists(LegacyMarker);
            }
        }

        public static bool StartupEnabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return LegacyEnabled || (key != null && key.GetValue(Entry) is string);
            }
        }

        internal static string StartupCommand(string executable)
        {
            return WorkerLauncher.Arguments(new[] { executable }) + " --startup";
        }

        public static void SetStartup(bool enabled)
        {
            bool legacy = LegacyEnabled;
            if (legacy)
                RemoveLegacyForCurrentUser();
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                    key.SetValue(Entry, StartupCommand(AppPaths.Executable), RegistryValueKind.String);
                else
                    key.DeleteValue(Entry, false);
            }

            if (legacy)
                File.Delete(LegacyMarker);
        }

        static void RemoveLegacyForCurrentUser()
        {
            string sid = WindowsIdentity.GetCurrent().User.Value;
            if (AppPaths.Administrator)
                RemoveLegacyTask(sid);
            else
            {
                var start = new ProcessStartInfo(AppPaths.Executable, "--remove-legacy-startup " + sid)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(start))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new IOException("Windows could not remove the old elevated startup task.");
                }
            }
        }

        public static int RemoveLegacyStartup(string[] args)
        {
            if (args.Length != 2 || !AppPaths.Administrator || args[1] != WindowsIdentity.GetCurrent().User.Value)
                return 2;
            RemoveLegacyTask(args[1]);
            return 0;
        }

        static void RemoveLegacyTask(string sid)
        {
            object scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            object folder = null;
            try
            {
                scheduler.GetType().InvokeMember("Connect", BindingFlags.InvokeMethod, null, scheduler, new object[] { null, null, null, null });
                folder = scheduler.GetType().InvokeMember("GetFolder", BindingFlags.InvokeMethod, null, scheduler, new object[] { @"\" });
                try
                {
                    folder.GetType().InvokeMember("DeleteTask", BindingFlags.InvokeMethod, null, folder, new object[] { "FanPilot-Studio-" + sid, 0 });
                }
                catch (Exception exception)
                {
                    // The old task was already removed outside the app.
                    if (exception.GetBaseException().HResult != unchecked((int)0x80070002))
                        throw;
                }
            }
            finally
            {
                if (folder != null)
                    Marshal.ReleaseComObject(folder);
                Marshal.ReleaseComObject(scheduler);
            }
        }
    }
}
