using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace FanPilot
{
    public static class Protocol
    {
        public static string Encode(Snapshot snapshot)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Snapshot)).WriteObject(ms, snapshot);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        public static Snapshot Decode(string value)
        {
            if (value == null || value.Length > 32768)
                throw new IOException("Invalid bridge response.");
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(value)))
                return (Snapshot)new DataContractJsonSerializer(typeof(Snapshot)).ReadObject(ms);
        }

        public static string ReadLine(Stream stream)
        {
            List<byte> bytes = new List<byte>();
            for (int i = 0; i <= 32768; i++)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new EndOfStreamException("Bridge disconnected.");
                if (b == 10)
                    return Encoding.UTF8.GetString(bytes.ToArray());
                bytes.Add((byte)b);
            }

            throw new IOException("Bridge message is too long.");
        }

        public static void WriteLine(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        public static Snapshot Execute(FanController controller, string command, DateTime now)
        {
            if (command == null)
                throw new ArgumentException("Missing command.");
            if (command == "POLL")
                return controller.GetSnapshot();
            if (command == "ENABLE")
                controller.Enable(now);
            else if (command == "DISABLE")
                controller.Disable();
            else if (command == "RESET")
                controller.ResetFirmware();
            else if (command == "ENABLECURVE")
                controller.EnableCurve(now);
            else if (command == "CALIBRATE")
                controller.StartCalibration(now);
            else if (command.StartsWith("CURVE|", StringComparison.Ordinal))
                controller.ConfigureCurve(CurveCodec.Decode<CurveSettings>(command.Substring(6)));
            else if (command.StartsWith("CALIBRATION|", StringComparison.Ordinal))
                controller.LoadCalibration(CurveCodec.Decode<FanCalibration>(command.Substring(12)));
            else if (command != null && command.StartsWith("CONFIG|", StringComparison.Ordinal))
            {
                string[] parts = command.Split('|');
                int percent;
                if (parts.Length != 3 || parts[1] != "Percent" || !int.TryParse(parts[2], out percent))
                    throw new ArgumentException("Invalid PWM configuration.");
                controller.Configure(percent);
            }
            else
                throw new ArgumentException("Unknown command.");
            return controller.GetSnapshot();
        }
    }
}
