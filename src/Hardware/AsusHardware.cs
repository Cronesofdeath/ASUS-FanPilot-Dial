using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FanPilot
{
    public sealed class AsusHardware : IHardware
    {
        readonly string configuredPath;
        public AsusHardware(string libraryPath = null)
        {
            configuredPath = libraryPath;
        }

        IntPtr library;
        bool initialized;
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate int NoArg();
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate int ByteArg(byte value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate uint TempArg();
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void VoidArg();
        NoArg init, count, rpm;
        ByteArg select, mode, duty;
        TempArg temp;
        VoidArg shutdown;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")]
        static extern bool FreeLibrary(IntPtr module);
        T Export<T>(string name)
            where T : class
        {
            IntPtr ptr = GetProcAddress(library, name);
            if (ptr == IntPtr.Zero)
                throw new IOException("Missing ASUS export: " + name);
            return Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T;
        }

        public bool Initialize()
        {
            string path = configuredPath ?? DependencyFiles.Resolve("AsusWinIO64.dll", PackageTrust.AsusHash);
            PackageTrust.Verify(path, PackageTrust.AsusHash);
            library = LoadLibraryEx(path, IntPtr.Zero, 0x800); // Exact DLL path; dependencies only from System32.
            if (library == IntPtr.Zero)
                throw new IOException("ASUS library could not load (Windows error " + Marshal.GetLastWin32Error() + ").");
            init = Export<NoArg>("InitializeWinIo");
            shutdown = Export<VoidArg>("ShutdownWinIo");
            count = Export<NoArg>("HealthyTable_FanCounts");
            rpm = Export<NoArg>("HealthyTable_FanRPM");
            select = Export<ByteArg>("HealthyTable_SetFanIndex");
            mode = Export<ByteArg>("HealthyTable_SetFanTestMode");
            duty = Export<ByteArg>("HealthyTable_SetFanPwmDuty");
            temp = Export<TempArg>("Thermal_Read_Cpu_Temperature");
            initialized = init() == 1;
            return initialized;
        }

        public int FanCount()
        {
            return count();
        }

        public bool Select(byte index)
        {
            bool ok = select(index) == 1;
            System.Threading.Thread.Sleep(20);
            return ok;
        }

        public int Rpm()
        {
            return rpm();
        }

        public uint Temperature()
        {
            return temp();
        }

        public bool TestMode(byte enabled)
        {
            bool ok = mode(enabled) == 1;
            System.Threading.Thread.Sleep(20);
            return ok;
        }

        public bool Duty(byte value)
        {
            bool ok = duty(value) == 1;
            System.Threading.Thread.Sleep(20);
            return ok;
        }

        public void Dispose()
        {
            if (initialized)
            {
                shutdown();
                initialized = false;
            }

            if (library != IntPtr.Zero)
            {
                FreeLibrary(library);
                library = IntPtr.Zero;
            }
        }
    }
}
