using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Forms = System.Windows.Forms;

namespace FanPilot
{
    public sealed class KeyboardShortcut : IDisposable
    {
        public readonly KeyChord Keys = new KeyChord();
        readonly Thread thread;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly bool testing;
        IntPtr handle;
        uint threadId;
        Exception error;
        Hook callback;
        bool disposed;
        const long TestTag = 0x46504B59;
        delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        struct KeyData
        {
            public uint Key, Scan, Flags, Time;
            public IntPtr Extra;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int id, Hook proc, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")]
        static extern bool PostThreadMessage(uint thread, uint message, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);
        public KeyboardShortcut(bool testInput = false)
        {
            testing = testInput;
            thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "ASUS FanPilot shortcut"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.WaitOne(5000))
                throw new TimeoutException("Keyboard shortcut initialization timed out.");
            if (error != null)
                throw new InvalidOperationException("Keyboard shortcut could not start.", error);
        }

        void Run()
        {
            try
            {
                threadId = GetCurrentThreadId();
                callback = HandleKey;
                handle = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
                if (handle == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                ready.Set();
                Forms.Application.Run();
            }
            catch (Exception ex)
            {
                error = ex;
                ready.Set();
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(handle);
                    handle = IntPtr.Zero;
                }
            }
        }

        IntPtr HandleKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !disposed)
            {
                KeyData key = (KeyData)Marshal.PtrToStructure(data, typeof(KeyData));
                bool tagged = testing && key.Extra.ToInt64() == TestTag;
                // Physical shortcuts only in production; tagged input is accepted by demo tests.
                if ((key.Flags & 0x10) == 0 || tagged)
                {
                    int m = message.ToInt32();
                    try
                    {
                        if (m == 0x100 || m == 0x104)
                            Keys.Input((int)key.Key, true);
                        else if (m == 0x101 || m == 0x105)
                            Keys.Input((int)key.Key, false);
                    }
                    catch
                    {
                    }
                }

                // Test-generated keys must not reach the user's other apps. Real input is always forwarded.
                if (tagged)
                    return new IntPtr(1);
            }

            return CallNextHookEx(handle, code, message, data);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Keys.CancelCapture();
            if (threadId != 0)
                PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero);
            if (Thread.CurrentThread != thread)
                thread.Join(2000);
        }
    }
}
