using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FanPilot
{
    // Owns the dashboard's connection to the worker and the reset acknowledgment across reconnects.
    internal sealed class FanSession
    {
        readonly bool demo;
        readonly string resetMarker;
        readonly SemaphoreSlim exchange = new SemaphoreSlim(1, 1);
        BridgeClient client;
        bool recoveryRequired, closing;
        int requests;
        string lastError;
        readonly System.Diagnostics.Stopwatch sampleAge = new System.Diagnostics.Stopwatch();
        public bool Stale
        {
            get
            {
                return Connected && sampleAge.IsRunning && sampleAge.Elapsed.TotalSeconds >= 4;
            }
        }

        public Snapshot State { get; private set; }

        public bool Connected
        {
            get
            {
                return client != null && client.Connected;
            }
        }

        public bool IsBusy
        {
            get
            {
                return requests != 0;
            }
        }

        public FanSession(bool demo, string preferencesPath)
        {
            this.demo = demo;
            resetMarker = preferencesPath + (demo ? ".demo-reset-pending" : ".reset-pending");
            recoveryRequired = File.Exists(resetMarker);
            State = new Snapshot
            {
                ReleaseConfirmed = !recoveryRequired
            };
        }

        public async Task ConnectAsync()
        {
            requests++;
            await exchange.WaitAsync();
            try
            {
                if (closing)
                    return;
                Disconnect();
                client = new BridgeClient();
                State = await Task.Run(delegate
                {
                    return client.Connect(demo);
                });
                sampleAge.Restart();
                if (recoveryRequired)
                {
                    State.ReleaseConfirmed = false;
                    await ExchangeAsync("RESET");
                }
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
            finally
            {
                exchange.Release();
                requests--;
            }
        }

        public async Task<bool> SendAsync(string command)
        {
            if (!Connected || closing)
                return false;
            requests++;
            await exchange.WaitAsync();
            try
            {
                if (!Connected || closing)
                    return false;
                bool acquiresControl = command == "ENABLE" || command == "ENABLECURVE" || command == "CALIBRATE";
                if (acquiresControl)
                {
                    if (recoveryRequired)
                        return false;
                    RecordPendingReset();
                }

                await ExchangeAsync(command);
                return true;
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
                return true;
            }
            finally
            {
                exchange.Release();
                requests--;
            }
        }

        public async Task<bool> StopAsync(bool leaveUnconfirmed)
        {
            closing = true;
            await exchange.WaitAsync();
            try
            {
                if (!Connected)
                    Disconnect();
                else if (!leaveUnconfirmed)
                {
                    try
                    {
                        await ExchangeAsync("DISABLE");
                        if (State.ReleaseConfirmed)
                            await ExchangeAsync("QUIT");
                    }
                    catch (Exception exception)
                    {
                        ReportFailure(exception);
                    }
                }

                if (!leaveUnconfirmed && !State.ReleaseConfirmed)
                {
                    closing = false;
                    return false;
                }

                Disconnect();
                return true;
            }
            finally
            {
                exchange.Release();
            }
        }

        async Task ExchangeAsync(string command)
        {
            State = await Task.Run(delegate
            {
                return client.Exchange(command);
            });
            sampleAge.Restart();
            if (recoveryRequired && command != "RESET")
                State.ReleaseConfirmed = false;
            else if (State.ReleaseConfirmed)
            {
                if (File.Exists(resetMarker))
                    File.Delete(resetMarker);
                recoveryRequired = false;
            }

            if (!String.IsNullOrEmpty(State.Error) && State.Error != lastError && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS")))
                AppPaths.Trace("Controller error: " + State.Error + " | CPU=" + State.Temperature + " GPU=" + State.GpuTemperature + " RPM=" + String.Join(",", State.Rpms));
            lastError = State.Error;
        }

        void RecordPendingReset()
        {
            if (File.Exists(resetMarker))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resetMarker)));
            using (var marker = File.Create(resetMarker))
            {
                marker.WriteByte(1);
                marker.Flush(true);
            }
        }

        public void ReportFailure(Exception exception)
        {
            Disconnect();
            State.Error = exception.Message;
            AppPaths.Trace(exception.ToString());
        }

        void Disconnect()
        {
            if (client != null)
            {
                client.Dispose();
                client = null;
            }

            State.Connected = false;
            State.Valid = false;
            State.Manual = false;
            State.Calibrating = false;
            recoveryRequired = !State.ReleaseConfirmed || File.Exists(resetMarker);
            State.ReleaseConfirmed = !recoveryRequired;
            State.Mode = recoveryRequired ? "Unknown" : "Firmware";
        }
    }
}
