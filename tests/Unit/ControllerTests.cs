using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FanPilot;

sealed class FakeHardware : IHardware
{
    public int Count = 2, Selected;
    public bool InitResult = true, Closed;
    public uint Temp = 60;
    public int[] Speeds =
    {
        3450,
        3400,
        3300,
        3200,
        3100
    };
    public bool[] Active = new bool[5];
    public int[] Pwm = new int[5];
    public int RejectSelect = -1, RejectDuty = -1, RejectMode = -1, RejectReset = -1;
    public bool ThrowRpm, ThrowTemp, ThrowReset;
    public List<string> Calls = new List<string>();
    public bool Initialize()
    {
        Calls.Add("init");
        return InitResult;
    }

    public int FanCount()
    {
        Calls.Add("count");
        return Count;
    }

    public bool Select(byte index)
    {
        Calls.Add("select:" + index);
        if (index == RejectSelect)
            return false;
        Selected = index;
        return true;
    }

    public int Rpm()
    {
        if (ThrowRpm)
            throw new IOException("sensor");
        return Speeds[Selected];
    }

    public uint Temperature()
    {
        if (ThrowTemp)
            throw new IOException("sensor");
        return Temp;
    }

    public bool TestMode(byte enabled)
    {
        Calls.Add("mode:" + Selected + ":" + enabled);
        if (enabled == 0 && ThrowReset)
            throw new IOException("reset");
        if ((enabled == 1 && Selected == RejectMode) || (enabled == 0 && Selected == RejectReset))
            return false;
        Active[Selected] = enabled != 0;
        return true;
    }

    public bool Duty(byte value)
    {
        Calls.Add("duty:" + Selected + ":" + value);
        if (Selected == RejectDuty)
            return false;
        Pwm[Selected] = value;
        return true;
    }

    public void Dispose()
    {
        Calls.Add("dispose");
        Closed = true;
    }
}

static partial class Tests
{
    static int passed, failed;
    static readonly string output = Environment.GetEnvironmentVariable("FANPILOT_TEST_OUTPUT") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output");
    static DateTime now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    static void Throws(Action action)
    {
        bool thrown = false;
        try
        {
            action();
        }
        catch
        {
            thrown = true;
        }

        Check(thrown, "Expected rejection");
    }

    static void Run(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }

    static FanController Open(FakeHardware fake)
    {
        FanController controller = new FanController(fake, true);
        controller.Connect();
        return controller;
    }

    static void EveryReset(FakeHardware f, int count)
    {
        for (int i = 0; i < count; i++)
            Check(f.Calls.Contains("mode:" + i + ":0"), "No reset attempt for fan " + i);
    }

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--read-mode-source")
        {
            PerformanceMode? detected = new GHelperModeSource().Read();
            Console.WriteLine("Read-only G-Helper mode: " + (detected.HasValue ? detected.Value.ToString() : "Unavailable"));
            return detected.HasValue ? 0 : 1;
        }

        if (args.Length > 1 && args[0] == "--echo")
        {
            File.WriteAllLines(args[1], args.Skip(2).ToArray());
            return 0;
        }

        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS", Path.Combine(output, "preferences.json"));
        Run("valid percent range maps into bounded PWM", delegate
        {
            for (int i = 40; i <= 100; i++)
                Check(Rules.Pwm(i) >= 102 && Rules.Pwm(i) <= 255, "PWM range");
        });
        Run("PWM conversion preserves original 90 percent mapping", delegate
        {
            Check(Rules.Pwm(90) == 229, "PWM mapping");
        });
        foreach (int value in new int[]
        {
            int.MinValue,
            -1,
            0,
            39,
            101,
            255,
            int.MaxValue
        }

        )
        {
            int captured = value;
            Run("reject percent " + value, delegate
            {
                Throws(delegate
                {
                    Rules.Pwm(captured);
                });
            });
        }

        foreach (int count in new int[]
        {
            -1,
            0,
            6,
            255,
            int.MaxValue
        }

        )
        {
            int captured = count;
            Run("reject native fan-count sentinel/range " + count, delegate
            {
                FakeHardware f = new FakeHardware
                {
                    Count = captured
                };
                using (FanController c = new FanController(f, true))
                    Throws(c.Connect);
                Check(!f.Calls.Any(x => x.StartsWith("mode:")), "Touched hardware on invalid count");
            });
        }

        Run("initialization failure prevents fan operations", delegate
        {
            FakeHardware f = new FakeHardware
            {
                InitResult = false
            };
            using (FanController c = new FanController(f, true))
                Throws(c.Connect);
            Check(!f.Calls.Contains("count"), "Read after init failure");
        });
        Run("startup is automatic with no fan writes", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                Check(!c.GetSnapshot().Manual, "Auto-on");
                Check(!f.Calls.Any(x => x.StartsWith("mode:")), "Startup wrote test mode");
            }
        });
        Run("configuration while automatic does not write", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Configure(80);
                Check(!f.Calls.Any(x => x.StartsWith("mode:")), "Configuration wrote fans");
            }
        });
        Run("explicit reset writes firmware mode on a fresh helper", delegate
        {
            var hardware = new FakeHardware
            {
                Count = 2
            };
            using (FanController controller = Open(hardware))
            {
                Protocol.Execute(controller, "DISABLE", DateTime.UtcNow);
                Check(!hardware.Calls.Any(call => call.StartsWith("mode:")), "Ordinary disable wrote to untouched fans");
                Snapshot reset = Protocol.Execute(controller, "RESET", DateTime.UtcNow);
                Check(reset.ReleaseConfirmed && !reset.Manual, "Explicit reset was not confirmed");
                Check(hardware.Calls.Contains("mode:0:0") && hardware.Calls.Contains("mode:1:0"), "Reset skipped a fan");
            }
        });
        Run("invalid configuration leaves previous state unchanged", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                Throws(delegate
                {
                    c.Configure(0);
                });
                Check(c.GetSnapshot().Value == 90, "State mutated");
            }
        });
        Run("all five fans are written before enable returns", delegate
        {
            FakeHardware f = new FakeHardware
            {
                Count = 5
            };
            using (FanController c = Open(f))
            {
                c.Configure(83);
                c.Enable(now);
                Check(c.GetSnapshot().Manual && f.Active.All(x => x), "Incomplete enable");
                Check(f.Pwm.All(x => x == Rules.Pwm(83)), "Incomplete duty");
            }
        });
        Run("all five fans reset synchronously", delegate
        {
            FakeHardware f = new FakeHardware
            {
                Count = 5
            };
            using (FanController c = Open(f))
            {
                c.Enable(now);
                Check(c.Disable(), "Reset failed");
                EveryReset(f, 5);
                Check(f.Active.All(x => !x), "Active after reset");
            }
        });
        Run("dispose resets every fan before shutdown", delegate
        {
            FakeHardware f = new FakeHardware();
            FanController c = Open(f);
            c.Enable(now);
            c.Dispose();
            EveryReset(f, 2);
            Check(f.Closed && !f.Active[0] && !f.Active[1] && f.Calls.Last() == "dispose", "Bad shutdown ordering");
        });
        Run("partial duty failure rolls back every fan", delegate
        {
            FakeHardware f = new FakeHardware
            {
                RejectDuty = 1
            };
            using (FanController c = Open(f))
            {
                c.Enable(now);
                Check(!c.GetSnapshot().Manual && c.GetSnapshot().ReleaseConfirmed, "Failure not latched");
                EveryReset(f, 2);
                Check(!f.Active[0] && !f.Active[1], "Partial override survived");
            }
        });
        Run("mode failure skips PWM and rolls back", delegate
        {
            FakeHardware f = new FakeHardware
            {
                RejectMode = 1
            };
            using (FanController c = Open(f))
            {
                c.Enable(now);
                Check(!f.Calls.Any(x => x.StartsWith("duty:1:")), "PWM after rejected mode");
                EveryReset(f, 2);
            }
        });
        Run("selection failure prevents writing wrong fan", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                f.RejectSelect = 1;
                c.Enable(now);
                Check(!f.Calls.Any(x => x.StartsWith("duty:")), "Write after failed preflight");
            }
        });
        Run("reset failure does not skip remaining fans", delegate
        {
            FakeHardware f = new FakeHardware
            {
                Count = 3
            };
            using (FanController c = Open(f))
            {
                c.Enable(now);
                f.RejectReset = 0;
                Check(!c.Disable() && !c.GetSnapshot().ReleaseConfirmed, "False reset success");
                EveryReset(f, 3);
                Check(!f.Active[1] && !f.Active[2], "Later resets skipped");
            }
        });
        Run("unconfirmed reset blocks re-enable", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Enable(now);
                f.RejectReset = 0;
                c.Disable();
                int writes = f.Calls.Count(x => x.StartsWith("duty:"));
                c.Enable(now);
                Check(!c.GetSnapshot().Manual && f.Calls.Count(x => x.StartsWith("duty:")) == writes, "Re-enabled after failed reset");
            }
        });
        Run("reset can be retried successfully", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Enable(now);
                f.RejectReset = 0;
                c.Disable();
                f.RejectReset = -1;
                Check(c.Disable() && c.GetSnapshot().ReleaseConfirmed, "Retry did not clear failure");
            }
        });
        Run("reset exceptions are contained", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Enable(now);
                f.ThrowReset = true;
                Check(!c.Disable(), "Exception falsely succeeded");
                EveryReset(f, 2);
            }
        });
        foreach (uint temp in new uint[]
        {
            0,
            126,
            uint.MaxValue
        }

        )
        {
            uint captured = temp;
            Run("invalid temperature prevents enable " + temp, delegate
            {
                FakeHardware f = new FakeHardware
                {
                    Temp = captured
                };
                using (FanController c = new FanController(f, true))
                    Throws(c.Connect);
                Check(!f.Calls.Any(x => x.StartsWith("mode:")), "Unsafe enable");
            });
        }

        foreach (int rpm in new int[]
        {
            -1,
            int.MaxValue,
            20001
        }

        )
        {
            int captured = rpm;
            Run("RPM error prevents startup " + rpm, delegate
            {
                FakeHardware f = new FakeHardware();
                f.Speeds[0] = captured;
                using (FanController c = new FanController(f, true))
                    Throws(c.Connect);
            });
        }

        Run("zero RPM allowed before manual spin-up", delegate
        {
            FakeHardware f = new FakeHardware();
            f.Speeds[0] = 0;
            using (FanController c = Open(f))
            {
                c.Enable(now);
                Check(c.GetSnapshot().Manual, "Idle fan wrongly rejected");
            }
        });
        Run("enable denied at 90 C", delegate
        {
            FakeHardware f = new FakeHardware
            {
                Temp = 90
            };
            using (FanController c = Open(f))
            {
                c.Enable(now);
                Check(!c.GetSnapshot().Manual && !f.Calls.Any(x => x.StartsWith("mode:")), "Hot enable");
            }
        });
        Run("90 C cooling priority overrides percentage", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Configure(50);
                c.Enable(now);
                f.Temp = 90;
                Snapshot s = c.Poll(now.AddSeconds(2));
                Check(s.CoolingPriority && s.Duties.All(x => x == 100), "Cooling priority missing");
            }
        });
        Run("configuration during heat immediately prioritizes cooling", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Configure(50);
                c.Enable(now);
                f.Temp = 90;
                c.Configure(40);
                Check(c.GetSnapshot().Duties.All(x => x == 100), "Configuration delayed cooling");
            }
        });
        Run("cooling hysteresis holds until 85 C", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Configure(60);
                c.Enable(now);
                f.Temp = 90;
                c.Poll(now.AddSeconds(2));
                f.Temp = 87;
                Check(c.Poll(now.AddSeconds(4)).Duties.All(x => x == 100), "Early resume");
                f.Temp = 85;
                Check(c.Poll(now.AddSeconds(6)).Duties.All(x => x == 60), "No resume");
            }
        });
        Run("98 C releases control and latches off", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Enable(now);
                f.Temp = 98;
                Snapshot s = c.Poll(now.AddSeconds(2));
                Check(!s.Manual && s.ReleaseConfirmed && s.Error.Contains("98 °C"), "No thermal release");
                f.Temp = 60;
                Check(!c.Poll(now.AddSeconds(4)).Manual, "Automatic re-enable");
            }
        });
        Run("invalid sensor during control triggers reset", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Enable(now);
                f.Temp = 0;
                Snapshot s = c.Poll(now.AddSeconds(2));
                Check(!s.Manual && !s.Valid && s.ReleaseConfirmed, "Invalid sensor retained override");
                EveryReset(f, 2);
            }
        });
        Run("RPM read exception during control triggers reset", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Enable(now);
                f.ThrowRpm = true;
                Snapshot s = c.Poll(now.AddSeconds(2));
                Check(!s.Manual && s.ReleaseConfirmed, "Exception retained override");
            }
        });
        Run("temperature exception during control triggers reset", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Enable(now);
                f.ThrowTemp = true;
                Check(!c.Poll(now.AddSeconds(2)).Manual, "Exception retained override");
            }
        });
        Run("fan stall has grace period then releases", delegate
        {
            FakeHardware f = new FakeHardware();
            f.Speeds[0] = 0;
            using (FanController c = Open(f))
            {
                c.Enable(now);
                Check(c.Poll(now.AddSeconds(2)).Manual, "No grace");
                c.Poll(now.AddSeconds(8));
                c.Poll(now.AddSeconds(9));
                Check(!c.Poll(now.AddSeconds(10)).Manual, "Stall ignored");
            }
        });
        Run("transient zero RPM does not trip persistent stall", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Enable(now);
                f.Speeds[0] = 0;
                c.Poll(now.AddSeconds(9));
                f.Speeds[0] = 3400;
                Check(c.Poll(now.AddSeconds(10)).Manual, "Transient stall trip");
            }
        });
        Run("snapshots cannot mutate controller arrays", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Enable(now);
                Snapshot s = c.GetSnapshot();
                s.Duties[0] = 0;
                Check(c.GetSnapshot().Duties[0] == 90, "Mutable snapshot");
            }
        });
        Run("JSON protocol round trip", delegate
        {
            Snapshot s = new Snapshot
            {
                Rpms = new int[]
                {
                    3500,
                    3400
                },
                Duties = new int[]
                {
                    90,
                    90
                },
                Manual = true,
                Error = "sensor / unavailable"
            };
            Snapshot r = Protocol.Decode(Protocol.Encode(s));
            Check(r.Manual && r.Rpms[1] == 3400 && r.Error == s.Error, "Bad protocol");
        });
        Run("protocol rejects oversized response", delegate
        {
            Throws(delegate
            {
                Protocol.Decode(new string ('A', 32769));
            });
        });
        Run("protocol rejects invalid base64", delegate
        {
            Throws(delegate
            {
                Protocol.Decode("!bad");
            });
        });
        Run("protocol read rejects unbounded line", delegate
        {
            using (MemoryStream s = new MemoryStream(new byte[32770]))
                Throws(delegate
                {
                    Protocol.ReadLine(s);
                });
        });
        Run("protocol read detects disconnect", delegate
        {
            using (MemoryStream s = new MemoryStream())
                Throws(delegate
                {
                    Protocol.ReadLine(s);
                });
        });
        Run("protocol valid configure and enable", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                Protocol.Execute(c, "CONFIG|Percent|75", now);
                Check(Protocol.Execute(c, "ENABLE", now).Duties.All(x => x == 75), "Bad config");
            }
        });
        foreach (string command in new string[]
        {
            "",
            "RUN|cmd",
            "CONFIG|Unknown|90",
            "CONFIG|Percent|0",
            "CONFIG|Percent|90|extra",
            "CONFIG|Rpm|abc",
            "CONFIG|Rpm|-1"
        }

        )
        {
            string captured = command;
            Run("protocol rejects " + command, delegate
            {
                FakeHardware f = new FakeHardware();
                using (FanController c = Open(f))
                    Throws(delegate
                    {
                        Protocol.Execute(c, captured, now);
                    });
            });
        }

        Run("preferences save and load exact inputs", delegate
        {
            Preferences p = new Preferences
            {
                Percent = 83,
                Hotkey = 120,
                Modifiers = 0
            };
            p.Save();
            Preferences q = Preferences.Load();
            Check(q.Percent == 83 && q.Hotkey == 120, "Settings lost");
        });
        Run("corrupt settings fall back safely with no enabled-state persistence", delegate
        {
            File.WriteAllText(Preferences.FilePath, "broken");
            Preferences p = Preferences.Load();
            Check(p.Percent == 90, "Bad default");
            Check(typeof(Preferences).GetField("Manual") == null, "Enabled state persisted");
        });
        Run("out-of-range settings fall back", delegate
        {
            File.WriteAllText(Preferences.FilePath, "{\"Percent\":0}");
            Check(Preferences.Load().Percent == 90, "Bad settings accepted");
        });
        Run("F12 can be observed as a shortcut key", delegate
        {
            new Preferences
            {
                Hotkey = 123,
                Modifiers = 0
            }.Validate();
        });
        Run("bare typing key allowed", delegate
        {
            new Preferences
            {
                Modifiers = 0,
                Hotkey = 70
            }.Validate();
        });
        Run("single F key allowed", delegate
        {
            new Preferences
            {
                Modifiers = 0,
                Hotkey = 120
            }.Validate();
        });
        Run("unexpected legacy modifier flags forbidden", delegate
        {
            Throws(delegate
            {
                new Preferences
                {
                    Modifiers = 16
                }.Validate();
            });
        });
        Run("package verifier detects tampering", delegate
        {
            string path = Path.Combine(output, "tampered.dll");
            File.WriteAllText(path, "not an ASUS DLL");
            Throws(delegate
            {
                PackageTrust.Verify(path, PackageTrust.AsusHash);
            });
        });
        Run("demo hardware interface can enable and release", delegate
        {
            using (FanController c = new FanController(new DemoHardware(), true))
            {
                c.Connect();
                c.Enable(now);
                Check(c.Poll(now.AddSeconds(2)).Rpms.All(x => x > 0), "No simulated spin");
                Check(c.Disable(), "Demo reset");
            }
        });
        Run("100 percent equals full 8-bit PWM only", delegate
        {
            Check(Rules.Pwm(100) == 255, "Full duty mapping");
        });
        Run("single letter triggers once while held", delegate
        {
            KeyChord k = new KeyChord
            {
                Binding = new int[]
                {
                    71
                }
            };
            int fired = 0;
            k.Activated += delegate
            {
                fired++;
            };
            k.Input(71, true);
            k.Input(71, true);
            k.Input(71, true);
            Check(fired == 1, "Auto repeat");
            k.Input(71, false);
            k.Input(71, true);
            Check(fired == 2, "Did not rearm");
        });
        Run("two arbitrary keys require both keys", delegate
        {
            KeyChord k = new KeyChord
            {
                Binding = new int[]
                {
                    71,
                    72
                }
            };
            int fired = 0;
            k.Activated += delegate
            {
                fired++;
            };
            k.Input(71, true);
            Check(fired == 0, "Partial chord fired");
            k.Input(72, true);
            Check(fired == 1, "Pair not recognized");
            k.Input(72, false);
            k.Input(71, false);
        });
        Run("three arbitrary keys trigger once", delegate
        {
            KeyChord k = new KeyChord
            {
                Binding = new int[]
                {
                    71,
                    72,
                    74
                }
            };
            int fired = 0;
            k.Activated += delegate
            {
                fired++;
            };
            k.Input(74, true);
            k.Input(71, true);
            Check(fired == 0, "Partial trio fired");
            k.Input(72, true);
            k.Input(72, true);
            Check(fired == 1, "Trio repeat");
        });
        Run("extra held keys do not match a chord", delegate
        {
            KeyChord k = new KeyChord
            {
                Binding = new int[]
                {
                    71,
                    72
                }
            };
            int fired = 0;
            k.Activated += delegate
            {
                fired++;
            };
            k.Input(17, true);
            k.Input(71, true);
            k.Input(72, true);
            Check(fired == 0, "Extra-key match");
        });
        Run("disabled shortcut never fires", delegate
        {
            KeyChord k = new KeyChord
            {
                Binding = new int[]
                {
                    71
                },
                Enabled = false
            };
            int fired = 0;
            k.Activated += delegate
            {
                fired++;
            };
            k.Input(71, true);
            k.Input(71, false);
            Check(fired == 0, "Disabled key fired");
        });
        Run("capture waits for release and never toggles", delegate
        {
            KeyChord k = new KeyChord
            {
                Binding = new int[]
                {
                    71
                }
            };
            int fired = 0;
            int[] saved = null;
            k.Activated += delegate
            {
                fired++;
            };
            k.Captured += delegate (int[] x)
            {
                saved = x;
            };
            k.BeginCapture();
            k.Input(71, true);
            k.Input(72, true);
            k.Input(71, false);
            Check(saved == null, "Saved before all released");
            k.Input(72, false);
            Check(saved.SequenceEqual(new int[] { 71, 72 }) && fired == 0, "Capture triggered control");
        });
        Run("four-key recording keeps previous binding", delegate
        {
            KeyChord k = new KeyChord
            {
                Binding = new int[]
                {
                    71
                }
            };
            bool rejected = false;
            k.CaptureRejected += delegate
            {
                rejected = true;
            };
            k.BeginCapture();
            for (int i = 65; i < 69; i++)
                k.Input(i, true);
            for (int i = 65; i < 69; i++)
                k.Input(i, false);
            Check(rejected && k.Binding.SequenceEqual(new int[] { 71 }), "Invalid recording replaced shortcut");
        });
        Run("capture cancellation preserves old shortcut", delegate
        {
            KeyChord k = new KeyChord
            {
                Binding = new int[]
                {
                    71
                }
            };
            k.BeginCapture();
            k.Input(72, true);
            k.CancelCapture();
            Check(!k.Capturing && k.Binding[0] == 71, "Cancel changed shortcut");
        });
        Run("legacy shortcut migrates with modifiers", delegate
        {
            Preferences p = new Preferences
            {
                Modifiers = 3,
                Hotkey = 70
            };
            Check(p.GetShortcutKeys().SequenceEqual(new int[] { 17, 18, 70 }), "Legacy migration");
        });
        Run("custom key combination persists", delegate
        {
            Preferences p = new Preferences
            {
                ShortcutKeys = new int[]
                {
                    71,
                    72,
                    74
                }
            };
            p.Save();
            Check(Preferences.Load().GetShortcutKeys().SequenceEqual(new int[] { 71, 72, 74 }), "Custom chord not saved");
        });
        Run("duplicate normalized keys rejected", delegate
        {
            Throws(delegate
            {
                KeyChord.Validate(new int[] { 160, 161 });
            });
        });
        Run("one modifier alone is allowed", delegate
        {
            KeyChord.Validate(new int[] { 17 });
        });
        Run("removed RPM mode rejected", delegate
        {
            using (FanController c = Open(new FakeHardware()))
                Throws(delegate
                {
                    Protocol.Execute(c, "CONFIG|Rpm|3500", now);
                });
        });
        Run("unknown calibration command rejected", delegate
        {
            using (FanController c = Open(new FakeHardware()))
                Throws(delegate
                {
                    Protocol.Execute(c, "CALIBRATE|invalid", now);
                });
        });
        Run("direct PWM never tracks RPM", delegate
        {
            FakeHardware f = new FakeHardware();
            using (FanController c = Open(f))
            {
                c.Configure(87);
                c.Enable(now);
                f.Speeds[0] = 5200;
                f.Speeds[1] = 4950;
                Snapshot s = c.Poll(now.AddSeconds(5));
                Check(s.Manual && s.Duties.All(x => x == 87), "Unexpected RPM feedback");
            }
        });
        Run("legacy RPM settings are ignored", delegate
        {
            File.WriteAllText(Preferences.FilePath, "{\"Percent\":83,\"Rpm\":9000,\"RpmMode\":true,\"Modifiers\":3,\"Hotkey\":70}");
            Check(Preferences.Load().Percent == 83, "Legacy settings lost");
        });
        Run("animation preference persists", delegate
        {
            Preferences p = new Preferences
            {
                AnimateFans = false
            };
            p.Save();
            Check(Preferences.Load().AnimateFans == false, "Animation preference lost");
        });
        Run("launcher arguments survive Windows quoting", delegate
        {
            string path = Path.Combine(output, "arguments.txt");
            string[] expected =
            {
                "a path with spaces",
                "a\"b",
                "C:\\trailing\\",
                ""
            };
            string[] arguments = new[]
            {
                "--echo",
                path
            }.Concat(expected).ToArray();
            using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(typeof(Tests).Assembly.Location, WorkerLauncher.Arguments(arguments)) { UseShellExecute = false, CreateNoWindow = true }))
                Check(child.WaitForExit(5000) && child.ExitCode == 0, "Argument probe failed");
            Check(File.ReadAllLines(path).SequenceEqual(expected), "Argument round trip changed a value");
        });
        Run("malformed launcher arguments rejected", delegate
        {
            Check(WorkerLauncher.Launch(new string[0]) == 2, "Launcher accepted an empty command");
        });
        Run("normal startup entry quotes the executable path", delegate
        {
            string executable = @"C:\Program Files\Fan & Tools\FanPilot.exe";
            Check(StartupTask.StartupCommand(executable) == "\"" + executable + "\" --startup", "Invalid startup command");
        });
        Run("dependency override validates bytes before use", delegate
        {
            string folder = Path.Combine(output, "dependency-fixture");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "fixture.dll");
            File.WriteAllText(path, "fixture");
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            string previous = Environment.GetEnvironmentVariable("FANPILOT_DEPENDENCIES");
            Environment.SetEnvironmentVariable("FANPILOT_DEPENDENCIES", folder);
            try
            {
                Check(DependencyFiles.Resolve("fixture.dll", hash) == path, "Dependency path changed");
                File.WriteAllText(path, "modified");
                Throws(delegate
                {
                    DependencyFiles.Resolve("fixture.dll", hash);
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable("FANPILOT_DEPENDENCIES", previous);
            }
        });
        RunCurveTests();
        RunCalibrationTimingTests();
        RunModeProfileTests();
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed. Simulated hardware only.");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "unit-summary.txt"), passed + " passed, " + failed + " failed. No native ASUS library loaded.");
        return failed == 0 ? 0 : 1;
    }
}
