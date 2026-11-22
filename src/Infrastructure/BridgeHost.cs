using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FanPilot
{
    public static class BridgeHost
    {
        public static bool ValidArguments(string[] args)
        {
            int pid;
            return (args.Length == 5 || (args.Length == 6 && args[0] == "--bridge" && Path.IsPathRooted(args[5]))) && (args[0] == "--bridge" || args[0] == "--demo-bridge") && Regex.IsMatch(args[1], "^FanPilot-[a-f0-9]{32}$") && Regex.IsMatch(args[2], "^[a-f0-9]{64}$") && Regex.IsMatch(args[3], "^S-1-5-[0-9-]{1,100}$") && int.TryParse(args[4], out pid) && pid > 0;
        }

        public static int Main(string[] args)
        {
            if (!ValidArguments(args))
                return 2;
            bool demo = args[0] == "--demo-bridge";
            if (!demo && !WindowsIdentity.GetCurrent().IsSystem)
                return 3;
            bool abandoned;
            using (Mutex hardwareOwner = new Mutex(false, demo ? @"Local\FanPilot.Experimental.DemoOwner" : @"Global\FanPilot.HardwareOwner"))
            {
                try
                {
                    abandoned = hardwareOwner.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    abandoned = true;
                }

                if (!abandoned)
                    return 4;
                try
                {
                    return Serve(args, demo);
                }
                catch (Exception ex)
                {
                    Log(ex);
                    return 1;
                }
                finally
                {
                    hardwareOwner.ReleaseMutex();
                }
            }
        }

        static void Log(Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FanPilot.bridge.log"), DateTime.UtcNow.ToString("O") + " " + ex.Message + Environment.NewLine);
            }
            catch
            {
            }
        }

        static int Serve(string[] args, bool demo)
        {
            if (!demo)
                foreach (string name in new string[]
                {
                    "AsusFanControl",
                    "AsusFanControlGUI"
                }

                )
                    if (Process.GetProcessesByName(name).Length > 0)
                        throw new IOException("Close the original AsusFanControl before connecting FanPilot.");
            SecurityIdentifier owner = new SecurityIdentifier(args[3]);
            PipeSecurity security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            using (NamedPipeServerStream pipe = new NamedPipeServerStream(args[1], PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
            {
                Process parent = Process.GetProcessById(int.Parse(args[4]));
                long heartbeat = DateTime.UtcNow.Ticks;
                int connected = 0;
                using (System.Threading.Timer lease = new System.Threading.Timer(delegate
                {
                    try
                    {
                        double idle = (DateTime.UtcNow - new DateTime(Interlocked.Read(ref heartbeat), DateTimeKind.Utc)).TotalSeconds;
                        if (parent.HasExited || idle > (Volatile.Read(ref connected) == 1 ? 8 : 30))
                            pipe.Dispose();
                    }
                    catch
                    {
                        try
                        {
                            pipe.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }, null, 500, 500))
                {
                    IAsyncResult pending = pipe.BeginWaitForConnection(null, null);
                    using (WaitHandle ready = pending.AsyncWaitHandle)
                    {
                        if (!ready.WaitOne(30000))
                            return 5;
                        pipe.EndWaitForConnection(pending);
                    }

                    if (Protocol.ReadLine(pipe) != args[2])
                        return 6;
                    string actualSid = "";
                    pipe.RunAsClient(delegate
                    {
                        actualSid = WindowsIdentity.GetCurrent().User.Value;
                    });
                    if (actualSid != args[3])
                        return 7;
                    Interlocked.Exchange(ref heartbeat, DateTime.UtcNow.Ticks);
                    Volatile.Write(ref connected, 1);
                    var clock = Stopwatch.StartNew();
                    DateTime epoch = DateTime.UtcNow;
                    using (FanController controller = new FanController(demo ? (IHardware)new DemoHardware() : new AsusHardware(args.Length == 6 ? args[5] : null), demo, null, delegate
                    {
                        return epoch.Add(clock.Elapsed);
                    }))
                    {
                        try
                        {
                            controller.Connect();
                            Protocol.WriteLine(pipe, Protocol.Encode(controller.GetSnapshot()));
                            long nextTick = 0;
                            Task<string> incoming = Task.Run(delegate
                            {
                                return Protocol.ReadLine(pipe);
                            });
                            while (pipe.IsConnected)
                            {
                                if (clock.ElapsedMilliseconds >= nextTick)
                                {
                                    controller.Poll(epoch.Add(clock.Elapsed));
                                    nextTick = clock.ElapsedMilliseconds + 1000;
                                }

                                int wait = (int)Math.Max(1, nextTick - clock.ElapsedMilliseconds);
                                if (!incoming.Wait(wait))
                                    continue;
                                string command = incoming.Result;
                                Interlocked.Exchange(ref heartbeat, DateTime.UtcNow.Ticks);
                                if (command == "QUIT")
                                {
                                    controller.Disable();
                                    Protocol.WriteLine(pipe, Protocol.Encode(controller.GetSnapshot()));
                                    if (controller.GetSnapshot().ReleaseConfirmed)
                                        break;
                                    incoming = Task.Run(delegate
                                    {
                                        return Protocol.ReadLine(pipe);
                                    });
                                    continue;
                                }

                                try
                                {
                                    Protocol.WriteLine(pipe, Protocol.Encode(Protocol.Execute(controller, command, epoch.Add(clock.Elapsed))));
                                }
                                catch (ArgumentException ex)
                                {
                                    // Malformed requests cannot leave a prior override running.
                                    controller.Disable();
                                    Snapshot rejected = controller.GetSnapshot();
                                    rejected.Error = ex.Message;
                                    Protocol.WriteLine(pipe, Protocol.Encode(rejected));
                                }

                                incoming = Task.Run(delegate
                                {
                                    return Protocol.ReadLine(pipe);
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            controller.Disable();
                            Log(ex);
                            try
                            {
                                Snapshot fail = controller.GetSnapshot();
                                fail.Error = ex.Message;
                                Protocol.WriteLine(pipe, Protocol.Encode(fail));
                            }
                            catch
                            {
                            }
                        }
                    }
                }

                parent.Dispose();
            }

            return 0;
        }
    }
}
