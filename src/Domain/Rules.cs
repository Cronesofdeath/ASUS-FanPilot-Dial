using System;

namespace FanPilot
{
    public static class Rules
    {
        public const int MinPercent = 40;
        public const int MaxPercent = 100;
        public const int CoolingTemperature = 85;
        public const int ReleaseTemperature = 90;
        public static void Validate(int percent)
        {
            if (percent < MinPercent || percent > MaxPercent)
                throw new ArgumentOutOfRangeException("percent", "PWM duty must be between 40 and 100 percent.");
        }

        public static byte Pwm(int percent)
        {
            Validate(percent);
            return (byte)(percent / 100.0f * 255);
        }
    }
}
