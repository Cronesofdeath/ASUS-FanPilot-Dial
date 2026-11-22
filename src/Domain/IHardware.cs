using System;

namespace FanPilot
{
    public interface IHardware : IDisposable
    {
        bool Initialize();
        int FanCount();
        bool Select(byte index);
        int Rpm();
        uint Temperature();
        bool TestMode(byte enabled);
        bool Duty(byte value);
    }
}
