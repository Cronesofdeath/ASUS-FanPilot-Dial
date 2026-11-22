using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FanPilot
{
    internal interface IModeSource
    {
        PerformanceMode? Read();
    }

    internal sealed class DemoModeSource : IModeSource
    {
        public PerformanceMode? Mode = PerformanceMode.Balanced;
        public bool Fail { get; set; }

        public PerformanceMode? Read()
        {
            if (Fail)
                throw new IOException("Simulated mode-source failure.");
            return Mode;
        }
    }

    internal sealed class GHelperModeSource : IModeSource
    {
        readonly string path;
        readonly Func<bool> running;
        public GHelperModeSource() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHelper", "config.json"), IsRunning)
        {
        }

        internal GHelperModeSource(string path, Func<bool> running)
        {
            this.path = path;
            this.running = running;
        }

        static bool IsRunning()
        {
            Process[] processes = Process.GetProcessesByName("GHelper");
            bool result = processes.Length > 0;
            foreach (Process process in processes)
                process.Dispose();
            return result;
        }

        public PerformanceMode? Read()
        {
            try
            {
                if (!running())
                    return null;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (file.Length > 65536)
                        return null;
                    var config = (ModeConfig)new DataContractJsonSerializer(typeof(ModeConfig)).ReadObject(file);
                    if (config == null || !config.Value.HasValue)
                        return null;
                    var mode = (PerformanceMode)config.Value.Value;
                    return ModeProfiles.IsValid(mode) ? (PerformanceMode? )mode : null;
                }
            }
            catch
            {
                return null;
            }
        }

        [DataContract]
        sealed class ModeConfig
        {
            [DataMember(Name = "performance_mode")]
            public int? Value = null;
        }
    }
}
