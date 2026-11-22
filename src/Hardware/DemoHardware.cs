using System;
using System.IO;

namespace FanPilot
{
    public sealed class DemoHardware : IHardware
    {
        int selected;
        readonly int[] duties =
        {
            90,
            90
        };
        readonly bool[] enabled = new bool[2];
        void Audit(string message)
        {
            string file = Environment.GetEnvironmentVariable("FANPILOT_DEMO_AUDIT");
            if (!String.IsNullOrEmpty(file))
                File.AppendAllText(file, message + Environment.NewLine);
        }

        public bool Initialize()
        {
            Audit("init");
            return true;
        }

        public int FanCount()
        {
            return 2;
        }

        public bool Select(byte index)
        {
            if (index > 1)
                return false;
            selected = index;
            return true;
        }

        public int Rpm()
        {
            return enabled[selected] ? 900 + duties[selected] * (selected == 0 ? 43 : 41) : (selected == 0 ? 3450 : 3380);
        }

        public uint Temperature()
        {
            return 61;
        }

        public bool TestMode(byte value)
        {
            enabled[selected] = value != 0;
            Audit("mode:" + selected + ":" + value);
            return true;
        }

        public bool Duty(byte value)
        {
            duties[selected] = (int)Math.Round(value / 255.0 * 100);
            return true;
        }

        public void Dispose()
        {
            Audit("dispose:" + enabled[0] + ":" + enabled[1]);
        }
    }
}
