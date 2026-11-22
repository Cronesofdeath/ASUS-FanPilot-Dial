using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FanPilot
{
    [DataContract]
    public sealed class Preferences
    {
        [DataMember]
        public int ThemeIndex;
        [DataMember]
        public int Percent = 90;
        [DataMember]
        public CurveSettings Curves = new CurveSettings();
        [DataMember]
        public ModeProfiles Profiles;
        [DataMember]
        public List<CurvePreset> CurvePresets;
        [DataMember(EmitDefaultValue = false)]
        public bool PersonalPresetsImported;
        [DataMember]
        public bool? FollowGHelper;
        [DataMember]
        public bool? UseCurveControl;
        [DataMember]
        public PerformanceMode? ManualProfile;
        public void EnsureProfiles()
        {
            try
            {
                if (Profiles == null)
                    throw new ArgumentException();
                Profiles.Validate();
            }
            catch
            {
                Profiles = ModeProfiles.Migrate(Curves);
            }

            if (!ManualProfile.HasValue || !ModeProfiles.IsValid(ManualProfile.Value))
                ManualProfile = PerformanceMode.Balanced;
        }

        [DataMember]
        public FanCalibration Calibration;
        [DataMember]
        public bool CloseToTray = true;
        [DataMember]
        public bool? AnimateFans;
        [DataMember]
        public bool HotkeyEnabled = false;
        [DataMember]
        public uint Modifiers = 3;
        [DataMember]
        public int Hotkey = 70;
        [DataMember]
        public int[] ShortcutKeys;
        public int[] GetShortcutKeys()
        {
            if (ShortcutKeys != null)
                return KeyChord.Validate(ShortcutKeys);
            if ((Modifiers & ~15u) != 0)
                throw new ArgumentException("Invalid legacy modifier flags.");
            var keys = new System.Collections.Generic.List<int>();
            if ((Modifiers & 2) != 0)
                keys.Add(17);
            if ((Modifiers & 1) != 0)
                keys.Add(18);
            if ((Modifiers & 4) != 0)
                keys.Add(16);
            if ((Modifiers & 8) != 0)
                keys.Add(91);
            keys.Add(Hotkey);
            return KeyChord.Validate(keys.ToArray());
        }

        public static string FilePath
        {
            get
            {
                string test = Environment.GetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS");
                return String.IsNullOrEmpty(test) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanPilotDial", "settings.json") : test;
            }
        }

        public void Validate()
        {
            Rules.Validate(Percent);
            GetShortcutKeys();
            EnsureProfiles();
        }

        public static Preferences Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path) && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS")))
                {
                    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    path = Path.Combine(local, "FanPilotStudio", "settings.json");
                    if (!File.Exists(path))
                        path = Path.Combine(local, "FanPilotExperimental", "settings.json");
                }

                using (FileStream file = File.OpenRead(path))
                {
                    if (file.Length > 262144)
                        return new Preferences();
                    Preferences result = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(file);
                    result.Validate();
                    return result;
                }
            }
            catch
            {
                return new Preferences();
            }
        }

        public void Save()
        {
            Validate();
            string path = FilePath, temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (FileStream file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                new DataContractJsonSerializer(typeof(Preferences)).WriteObject(file, this);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }
    }
}
