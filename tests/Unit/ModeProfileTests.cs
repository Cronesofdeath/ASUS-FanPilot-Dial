using System;
using System.IO;
using FanPilot;

static partial class Tests
{
    static void RunModeProfileTests()
    {
        Run("existing curve migrates independently into all three modes", delegate
        {
            var previous = Profile();
            previous.FanShift = 100;
            var profiles = ModeProfiles.Migrate(previous);
            profiles.Validate();
            foreach (PerformanceMode mode in new[]
            {
                PerformanceMode.Silent,
                PerformanceMode.Balanced,
                PerformanceMode.Turbo
            }

            )
                Check(profiles.Get(mode).FanShift == 100, "Lost current settings");
            var silent = profiles.Get(PerformanceMode.Silent);
            silent.Cpu[2].Duty = 46;
            profiles.Set(PerformanceMode.Silent, silent);
            Check(profiles.Get(PerformanceMode.Balanced).Cpu[2].Duty == 45 && previous.Cpu[2].Duty == 45, "Shared curve objects");
        });
        Run("editing one mode leaves the other mode curves intact", delegate
        {
            var profiles = ModeProfiles.Migrate(Profile());
            var turbo = profiles.Get(PerformanceMode.Turbo);
            turbo.Gpu[3].Duty = 60;
            profiles.Set(PerformanceMode.Turbo, turbo);
            Check(profiles.Get(PerformanceMode.Turbo).Gpu[3].Duty == 60 && profiles.Get(PerformanceMode.Silent).Gpu[3].Duty == 55, "Profile contamination");
            var copy = profiles.Get(PerformanceMode.Turbo);
            copy.Gpu[3].Duty = 65;
            Check(profiles.Get(PerformanceMode.Turbo).Gpu[3].Duty == 60, "Mutable stored profile");
        });
        Run("fan response settings remain independent between profiles", delegate
        {
            var profiles = ModeProfiles.Migrate(new CurveSettings());
            var balanced = profiles.Get(PerformanceMode.Balanced);
            balanced.SharedControl = false;
            balanced.Fan1Source = FanTemperatureSource.Cpu;
            profiles.Set(PerformanceMode.Balanced, balanced);
            Check(profiles.Get(PerformanceMode.Turbo).Shared && profiles.Get(PerformanceMode.Balanced).Fan1Source == FanTemperatureSource.Cpu, "Response settings leaked across modes");
        });
        Run("mode profiles persist without an enabled state", delegate
        {
            var prefs = new Preferences();
            prefs.EnsureProfiles();
            prefs.FollowGHelper = true;
            prefs.ManualProfile = PerformanceMode.Turbo;
            var silent = prefs.Profiles.Get(PerformanceMode.Silent);
            silent.HysteresisDown = 20;
            prefs.Profiles.Set(PerformanceMode.Silent, silent);
            prefs.Save();
            var loaded = Preferences.Load();
            Check(loaded.Profiles.Get(PerformanceMode.Silent).HysteresisDown == 20 && loaded.Profiles.Get(PerformanceMode.Balanced).HysteresisDown == 8 && loaded.ManualProfile == PerformanceMode.Turbo, "Persistence");
            Check(!File.ReadAllText(Preferences.FilePath).Contains("ManualEnabled"), "Persisted fan override");
        });
        Run("G-Helper mode identifiers map correctly and reading leaves its config unchanged", delegate
        {
            string file = Path.Combine(output, "ghelper-mode.json");
            foreach (int value in new[]
            {
                0,
                1,
                2
            }

            )
            {
                string json = "{\"performance_mode\":" + value + ",\"unrelated_setting\":123}";
                File.WriteAllText(file, json);
                var source = new GHelperModeSource(file, delegate
                {
                    return true;
                });
                Check(source.Read() == (PerformanceMode)value, "Incorrect mode identifier");
                Check(File.ReadAllText(file) == json, "Changed G-Helper config");
            }
        });
        Run("missing corrupt unknown and oversized G-Helper modes are unavailable", delegate
        {
            string file = Path.Combine(output, "ghelper-invalid.json");
            var source = new GHelperModeSource(file, delegate
            {
                return true;
            });
            foreach (string json in new[]
            {
                "{}",
                "{",
                "{\"performance_mode\":99}",
                new string ('x', 65537)
            }

            )
            {
                File.WriteAllText(file, json);
                Check(source.Read() == null, "Accepted invalid mode");
            }

            Check(new GHelperModeSource(file + ".missing", delegate
            {
                return true;
            }).Read() == null, "Missing file");
        });
        Run("a stopped G-Helper is not treated as an active mode source", delegate
        {
            string file = Path.Combine(output, "ghelper-stopped.json");
            File.WriteAllText(file, "{\"performance_mode\":1}");
            Check(new GHelperModeSource(file, delegate
            {
                return false;
            }).Read() == null, "Used stale saved mode");
        });
        Run("temporarily locked G-Helper config is unavailable without an exception", delegate
        {
            string file = Path.Combine(output, "ghelper-locked.json");
            File.WriteAllText(file, "{\"performance_mode\":0}");
            using (var held = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(new GHelperModeSource(file, delegate
                {
                    return true;
                }).Read() == null, "Lock handling");
        });
        Run("preset name collisions preserve the library", delegate
        {
            var settings = new Preferences();
            var personal = new[]
            {
                new CurvePreset
                {
                    Name = "Reference",
                    Curve = Profile()
                }
            };
            Throws(delegate
            {
                PresetLibrary.Edit(settings, personal, PresetAction.Create, null, "reference", Profile());
            });
            Check(settings.CurvePresets == null, "Failed create changed presets");
        });
        Run("a failed preset write restores the previous library", delegate
        {
            var settings = new Preferences();
            var previousPath = Environment.GetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS");
            try
            {
                string blocker = Path.Combine(output, "preset-blocker.txt");
                File.WriteAllText(blocker, "file instead of directory");
                Environment.SetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS", Path.Combine(blocker, "settings.json"));
                try
                {
                    PresetLibrary.Edit(settings, new CurvePreset[0], PresetAction.Create, null, "Example", Profile());
                    throw new Exception("Write unexpectedly succeeded");
                }
                catch (IOException)
                {
                    Check(settings.CurvePresets == null, "Uncommitted preset leaked into memory");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS", previousPath);
            }
        });
        Run("unknown profile selections are rejected", delegate
        {
            var profiles = ModeProfiles.Migrate(Profile());
            Throws(delegate
            {
                profiles.Get((PerformanceMode)99);
            });
            Throws(delegate
            {
                profiles.Set((PerformanceMode)99, Profile());
            });
        });
    }
}
