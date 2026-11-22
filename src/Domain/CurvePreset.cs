using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FanPilot
{
    [DataContract]
    public sealed class CurvePreset
    {
        [DataMember]
        public string Name;
        [DataMember]
        public CurveSettings Curve;
        public void Validate()
        {
            if (String.IsNullOrWhiteSpace(Name) || Name.Length > 40 || Curve == null)
                throw new ArgumentException("Enter a profile name of up to 40 characters.");
            Curve.Validate(5, false);
        }

        internal static CurvePreset[] ReadPersonalPresets()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "personal-curves.json");
            try
            {
                using (var file = File.OpenRead(path))
                {
                    if (file.Length > 32768)
                        return new CurvePreset[0];
                    var presets = (CurvePreset[])new DataContractJsonSerializer(typeof(CurvePreset[])).ReadObject(file);
                    if (presets == null || presets.Length > 8)
                        return new CurvePreset[0];
                    foreach (CurvePreset preset in presets)
                    {
                        if (preset == null)
                            return new CurvePreset[0];
                        preset.Validate();
                    }

                    return presets;
                }
            }
            catch (IOException)
            {
                return new CurvePreset[0];
            }
            catch (UnauthorizedAccessException)
            {
                return new CurvePreset[0];
            }
            catch (SerializationException)
            {
                return new CurvePreset[0];
            }
            catch (ArgumentException)
            {
                return new CurvePreset[0];
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
