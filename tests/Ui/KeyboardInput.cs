using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FanPilot
{
    internal static class KeyboardInputDriver
    {
        const long TestTag = 0x46504B59;
        [StructLayout(LayoutKind.Sequential)]
        struct KeyboardInput
        {
            public ushort Key, Scan;
            public uint Flags, Time;
            public IntPtr Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MouseInput
        {
            public int X, Y;
            public uint Data, Flags, Time;
            public IntPtr Extra;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public KeyboardInput Keyboard;
            [FieldOffset(0)]
            public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Input
        {
            public uint Type;
            public InputUnion Data;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, Input[] inputs, int size);
        public static void TestKey(int key, bool down)
        {
            Input input = new Input
            {
                Type = 1,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        Key = (ushort)key,
                        Flags = down ? 0u : 2u,
                        Extra = new IntPtr(TestTag)
                    }
                }
            };
            if (SendInput(1, new Input[] { input }, Marshal.SizeOf(typeof(Input))) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
