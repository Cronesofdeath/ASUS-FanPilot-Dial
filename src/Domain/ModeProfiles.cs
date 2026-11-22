using System;
using System.Runtime.Serialization;

namespace FanPilot
{
    // Values match G-Helper's published ASUS performance-mode identifiers.
    [DataContract]
    public enum PerformanceMode
    {
        [EnumMember]
        Balanced = 0,
        [EnumMember]
        Turbo = 1,
        [EnumMember]
        Silent = 2
    }

    [DataContract]
    public sealed class ModeProfiles
    {
        [DataMember]
        public CurveSettings Silent;
        [DataMember]
        public CurveSettings Balanced;
        [DataMember]
        public CurveSettings Turbo;
        public static bool IsValid(PerformanceMode mode)
        {
            return mode == PerformanceMode.Silent || mode == PerformanceMode.Balanced || mode == PerformanceMode.Turbo;
        }

        public static ModeProfiles Migrate(CurveSettings existing)
        {
            CurveSettings seed = existing ?? new CurveSettings();
            try
            {
                seed.Validate(5, false);
            }
            catch
            {
                seed = new CurveSettings();
            }

            return new ModeProfiles
            {
                Silent = seed.Clone(),
                Balanced = seed.Clone(),
                Turbo = seed.Clone()
            };
        }

        public CurveSettings Get(PerformanceMode mode)
        {
            if (!IsValid(mode))
                throw new ArgumentException("Unknown cooling profile.");
            return (mode == PerformanceMode.Silent ? Silent : mode == PerformanceMode.Turbo ? Turbo : Balanced).Clone();
        }

        public void Set(PerformanceMode mode, CurveSettings settings)
        {
            if (!IsValid(mode))
                throw new ArgumentException("Unknown cooling profile.");
            settings.Validate(5, false);
            if (mode == PerformanceMode.Silent)
                Silent = settings.Clone();
            else if (mode == PerformanceMode.Turbo)
                Turbo = settings.Clone();
            else
                Balanced = settings.Clone();
        }

        public void Validate()
        {
            if (Silent == null || Balanced == null || Turbo == null)
                throw new ArgumentException("Incomplete mode profiles.");
            Silent.Validate(5, false);
            Balanced.Validate(5, false);
            Turbo.Validate(5, false);
        }
    }
}
