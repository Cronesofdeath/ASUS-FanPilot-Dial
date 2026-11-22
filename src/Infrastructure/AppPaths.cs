using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace FanPilot
{
    public static class AppPaths
    {
        public static string Executable
        {
            get
            {
                return Assembly.GetExecutingAssembly().Location;
            }
        }

        public static string Data
        {
            get
            {
                return Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanPilotDial"));
            }
        }

        public static string Cache
        {
            get
            {
                return Path.Combine(Data, "runtime-v2");
            }
        }

        public static string Log
        {
            get
            {
                return Path.Combine(Data, "startup.log");
            }
        }

        public static bool Administrator
        {
            get
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        static string Ensure(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        public static void Trace(string message)
        {
            try
            {
                File.AppendAllText(Log, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
