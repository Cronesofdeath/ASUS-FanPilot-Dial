using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FanPilot
{
    public sealed class GpuTemperature : IDisposable
    {
        readonly object gate = new object ();
        IntPtr library;
        bool initialized, disposed;
        DateTime retryAfter;
        Init init;
        Shutdown shutdown;
        Count count;
        Handle handle;
        Temperature temperature;
        Name name;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int Init();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int Shutdown();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int Count(out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int Handle(uint index, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int Temperature(IntPtr device, uint sensor, out uint degrees);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        delegate int Name(IntPtr device, StringBuilder name, uint length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string symbol);
        [DllImport("kernel32.dll")]
        static extern bool FreeLibrary(IntPtr module);
        T Function<T>(string symbol)
            where T : class
        {
            IntPtr address = GetProcAddress(library, symbol);
            if (address == IntPtr.Zero)
                throw new EntryPointNotFoundException(symbol);
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        bool Connect()
        {
            if (initialized)
                return true;
            if (DateTime.UtcNow < retryAfter)
                return false;
            retryAfter = DateTime.UtcNow.AddSeconds(30);
            string programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (string path in new[]
            {
                Path.Combine(Environment.SystemDirectory, "nvml.dll"),
                Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvml.dll")
            }

            )
            {
                if (!File.Exists(path))
                    continue;
                library = LoadLibraryEx(path, IntPtr.Zero, 0x00000100 | 0x00000800);
                if (library != IntPtr.Zero)
                    break;
            }

            if (library == IntPtr.Zero)
                return false;
            try
            {
                init = Function<Init>("nvmlInit_v2");
                shutdown = Function<Shutdown>("nvmlShutdown");
                count = Function<Count>("nvmlDeviceGetCount_v2");
                handle = Function<Handle>("nvmlDeviceGetHandleByIndex_v2");
                temperature = Function<Temperature>("nvmlDeviceGetTemperature");
                name = Function<Name>("nvmlDeviceGetName");
                initialized = init() == 0;
                if (!initialized)
                    Disconnect();
                return initialized;
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        public GpuReading Read()
        {
            lock (gate)
            {
                if (disposed)
                    return Unavailable();
                try
                {
                    if (!Connect())
                        return Unavailable();
                    uint devices;
                    if (count(out devices) != 0)
                    {
                        Disconnect();
                        return Unavailable();
                    }

                    int hottest = 0;
                    var labels = new List<string>();
                    var details = new List<string>();
                    for (uint i = 0; i < Math.Min(devices, 16u); i++)
                    {
                        IntPtr device;
                        uint degrees;
                        if (handle(i, out device) != 0 || temperature(device, 0, out degrees) != 0 || degrees == 0 || degrees > 150)
                            continue;
                        hottest = Math.Max(hottest, (int)degrees);
                        var model = new StringBuilder(128);
                        string title = name(device, model, 128) == 0 ? model.ToString() : "GPU " + (i + 1);
                        labels.Add((devices > 1 ? "GPU " + (i + 1) : "GPU") + "  " + degrees + " °C");
                        details.Add(title + ": " + degrees + " °C");
                    }

                    if (labels.Count == 0)
                        return Unavailable();
                    return new GpuReading
                    {
                        Available = true,
                        Temperature = hottest,
                        Label = String.Join("  ·  ", labels),
                        Detail = String.Join("\n", details) + "\nRead-only NVIDIA driver sensor · refreshes every second"
                    };
                }
                catch
                {
                    Disconnect();
                    return Unavailable();
                }
            }
        }

        public static GpuReading Unavailable()
        {
            return new GpuReading
            {
                Label = "GPU  — °C",
                Detail = "GPU temperature unavailable. Requires a supported NVIDIA driver sensor. Sleeping or unsupported GPUs may not report a reading."
            };
        }

        void Disconnect()
        {
            if (initialized)
            {
                try
                {
                    shutdown();
                }
                catch
                {
                }

                initialized = false;
            }

            if (library != IntPtr.Zero)
            {
                FreeLibrary(library);
                library = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                Disconnect();
            }
        }
    }
}
