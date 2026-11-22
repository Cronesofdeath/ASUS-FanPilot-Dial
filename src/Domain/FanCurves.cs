using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FanPilot
{
    [DataContract]
    public enum FanTemperatureSource
    {
        [EnumMember]
        Hotter = 0,
        [EnumMember]
        Cpu = 1,
        [EnumMember]
        Gpu = 2
    }

    [DataContract]
    public sealed class CurvePoint
    {
        [DataMember]
        public int Temperature;
        [DataMember]
        public int Duty;
        public CurvePoint()
        {
        }

        public CurvePoint(int temperature, int duty)
        {
            Temperature = temperature;
            Duty = duty;
        }
    }

    [DataContract]
    public sealed class CurveSettings
    {
        [DataMember]
        public CurvePoint[] Cpu = DefaultPoints();
        [DataMember]
        public CurvePoint[] Gpu = DefaultPoints();
        [DataMember]
        public int FanShift = 50;
        [DataMember]
        public int HysteresisUp = 2;
        [DataMember]
        public int HysteresisDown = 8;
        [DataMember]
        public int CpuFan = 0;
        [DataMember]
        public int GpuFan = 1;
        [DataMember]
        public bool MappingConfirmed;
        [DataMember]
        public bool CpuFallback;
        [DataMember]
        public bool? SharedControl;
        [DataMember]
        public FanTemperatureSource? Fan1Source;
        [DataMember]
        public FanTemperatureSource? Fan2Source;
        public bool Shared
        {
            get
            {
                return SharedControl ?? true;
            }
        }

        public static CurvePoint[] DefaultPoints()
        {
            return new[]
            {
                new CurvePoint(30, 40),
                new CurvePoint(40, 40),
                new CurvePoint(50, 45),
                new CurvePoint(60, 55),
                new CurvePoint(70, 70),
                new CurvePoint(80, 90),
                new CurvePoint(90, 100),
                new CurvePoint(100, 100)
            };
        }

        public void Validate(int fanCount, bool requireMapping)
        {
            ValidatePoints(Cpu);
            ValidatePoints(Gpu);
            if (FanShift < 0 || FanShift > 100 || HysteresisUp < 0 || HysteresisUp > 15 || HysteresisDown < 0 || HysteresisDown > 60)
                throw new ArgumentException("Invalid fan shift or hysteresis.");
            if (fanCount < 2 || fanCount > 5)
                throw new ArgumentException("Curve control requires 2–5 fans.");
            foreach (FanTemperatureSource? source in new[]
            {
                Fan1Source,
                Fan2Source
            }

            )
                if (source.HasValue && source != FanTemperatureSource.Hotter && source != FanTemperatureSource.Cpu && source != FanTemperatureSource.Gpu)
                    throw new ArgumentException("Unknown fan temperature source.");
        }

        public static void ValidatePoints(CurvePoint[] points)
        {
            if (points == null || points.Length < 2 || points.Length > 16)
                throw new ArgumentException("A curve requires 2–16 points.");
            for (int i = 0; i < points.Length; i++)
            {
                CurvePoint point = points[i];
                if (point == null || point.Temperature < 20 || point.Temperature > 100)
                    throw new ArgumentException("Curve temperatures must be 20–100 °C.");
                Rules.Validate(point.Duty);
                if (i > 0 && (point.Temperature <= points[i - 1].Temperature || point.Duty < points[i - 1].Duty))
                    throw new ArgumentException("Temperature points must increase; fan duty cannot decrease along the curve.");
            }
        }

        public CurveSettings Clone()
        {
            return CurveCodec.Decode<CurveSettings>(CurveCodec.Encode(this));
        }
    }

    public static class CurveCodec
    {
        public static string Encode<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        public static T Decode<T>(string value)
        {
            try
            {
                if (String.IsNullOrEmpty(value) || value.Length > 16000)
                    throw new ArgumentException("Invalid curve data length.");
                using (var stream = new MemoryStream(Convert.FromBase64String(value)))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            catch (Exception exception)
            {
                throw new ArgumentException("Invalid curve data.", exception);
            }
        }
    }

    public static class CurveMath
    {
        public static double Interpolate(CurvePoint[] points, double temperature)
        {
            if (temperature <= points[0].Temperature)
                return points[0].Duty;
            for (int i = 1; i < points.Length; i++)
            {
                if (temperature > points[i].Temperature)
                    continue;
                CurvePoint a = points[i - 1], b = points[i];
                return a.Duty + (b.Duty - a.Duty) * (temperature - a.Temperature) / (b.Temperature - a.Temperature);
            }

            return points[points.Length - 1].Duty;
        }

        public static double Shift(double own, double other, int weight)
        {
            return own + (Math.Max(own, other) - own) * weight / 100.0;
        }

        public static int[] Targets(CurveSettings settings, int cpu, int gpu, int fanCount)
        {
            int first = (int)Math.Round(Interpolate(settings.Cpu, Input(settings, true, cpu, gpu)));
            int second = (int)Math.Round(Interpolate(settings.Gpu, Input(settings, false, cpu, gpu)));
            int[] result = Enumerable.Repeat(Math.Max(first, second), fanCount).ToArray();
            if (!settings.Shared)
            {
                result[0] = first;
                result[1] = second;
            }

            return result;
        }

        public static double Input(CurveSettings settings, bool first, int cpu, int gpu)
        {
            FanTemperatureSource source = settings.Shared ? FanTemperatureSource.Hotter : (first ? settings.Fan1Source : settings.Fan2Source) ?? FanTemperatureSource.Hotter;
            int own = source == FanTemperatureSource.Cpu ? cpu : source == FanTemperatureSource.Gpu ? gpu : Math.Max(cpu, gpu);
            return Shift(own, Math.Max(cpu, gpu), settings.FanShift);
        }
    }

    public sealed class CurveResponse
    {
        readonly int[] output, direction;
        readonly DateTime[] pending;
        public CurveResponse(int[] initial)
        {
            output = (int[])initial.Clone();
            direction = new int[initial.Length];
            pending = new DateTime[initial.Length];
        }

        public int[] Step(int[] requested, int upSeconds, int downSeconds, DateTime now)
        {
            for (int i = 0; i < output.Length; i++)
            {
                int difference = requested[i] - output[i];
                if (Math.Abs(difference) < 2)
                {
                    direction[i] = 0;
                    continue;
                }

                int nextDirection = Math.Sign(difference);
                if (direction[i] != nextDirection)
                {
                    direction[i] = nextDirection;
                    pending[i] = now;
                }

                int delay = difference > 0 ? upSeconds : downSeconds;
                if ((now - pending[i]).TotalSeconds < delay)
                    continue;
                output[i] += Math.Sign(difference) * Math.Min(Math.Abs(difference), difference > 0 ? 10 : 3);
            }

            return (int[])output.Clone();
        }
    }

    [DataContract]
    public sealed class FanCalibration
    {
        [DataMember]
        public string Id;
        [DataMember]
        public DateTime CreatedUtc;
        [DataMember]
        public int[][] Rpms;
        [DataMember]
        public bool MaximumOnly;
        [DataMember]
        public int[] Maxima;
        public void Validate(int fanCount)
        {
            if (MaximumOnly)
            {
                if (String.IsNullOrEmpty(Id) || Maxima == null || Maxima.Length != fanCount || Maxima.Any(x => x < 800 || x > 20000))
                    throw new ArgumentException("Invalid maximum-RPM measurement.");
                return;
            }

            if (String.IsNullOrEmpty(Id) || Rpms == null || Rpms.Length != fanCount)
                throw new ArgumentException("Calibration fan count differs.");
            foreach (int[] row in Rpms)
            {
                if (row == null || row.Length != 13)
                    throw new ArgumentException("Calibration requires 40–100% in 5% steps.");
                for (int i = 0; i < row.Length; i++)
                    if (row[i] < 200 || row[i] > 20000 || (i > 0 && row[i] < row[i - 1]))
                        throw new ArgumentException("Invalid calibration readings.");
                if (row[12] - row[0] < 200)
                    throw new ArgumentException("Fan did not respond sufficiently to calibration.");
            }
        }

        public FanCalibration Clone()
        {
            return CurveCodec.Decode<FanCalibration>(CurveCodec.Encode(this));
        }

        public double ToRpm(int fan, double duty)
        {
            if (MaximumOnly)
                return Maxima[fan] * Math.Max(40, Math.Min(100, duty)) / 100.0;
            double position = Math.Max(0, Math.Min(12, (duty - 40) / 5));
            int low = (int)Math.Floor(position), high = Math.Min(12, low + 1);
            return Rpms[fan][low] + (Rpms[fan][high] - Rpms[fan][low]) * (position - low);
        }

        public int ToDuty(int fan, double rpm)
        {
            if (MaximumOnly)
                return (int)Math.Round(Math.Max(40, Math.Min(100, rpm * 100 / Maxima[fan])));
            int[] row = Rpms[fan];
            if (rpm <= row[0])
                return 40;
            for (int i = 1; i < row.Length; i++)
                if (rpm <= row[i])
                    return (int)Math.Round(40 + 5 * (i - 1) + (row[i] == row[i - 1] ? 0 : 5 * (rpm - row[i - 1]) / (row[i] - row[i - 1])));
            return 100;
        }
    }
}
