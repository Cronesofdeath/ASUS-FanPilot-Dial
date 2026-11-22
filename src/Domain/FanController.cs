using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace FanPilot
{
    // The bridge owns this controller on one thread. Hardware writes are synchronous.
    public sealed class FanController : IDisposable
    {
        readonly IHardware hardware;
        readonly bool demo;
        readonly IGpuSensor gpu;
        readonly Func<DateTime> clock;
        DateTime lastCalibrationWrite;
        MaximumRpmMeasurement measurement;
        int? gpuTemperature;
        CurveSettings curves = new CurveSettings();
        CurveResponse response;
        string mode = "Fixed";
        bool calibrating;
        int calibrationProgress;
        FanCalibration calibration;
        int fanCount, temperature, percent = 90;
        int[] rpms = new int[0], duties = new int[0], stalls = new int[0];
        bool initialized, manual, valid, touched, released = true, cooling, disposed;
        string error = "", status = "Automatic control";
        DateTime enabledAt, lastAdjustment = DateTime.MinValue;
        DateTime? severeHeatSince;
        public FanController(IHardware hardware, bool simulation, IGpuSensor gpuSensor = null, Func<DateTime> monotonicClock = null)
        {
            if (hardware == null)
                throw new ArgumentNullException("hardware");
            this.hardware = hardware;
            demo = simulation;
            clock = monotonicClock;
            gpu = gpuSensor ?? (simulation ? (IGpuSensor)new DemoGpuSensor() : new CachedGpuSensor());
        }

        public void Connect()
        {
            if (initialized)
                throw new InvalidOperationException("Already initialized.");
            if (!hardware.Initialize())
                throw new IOException("ASUS driver initialization failed.");
            initialized = true;
            fanCount = hardware.FanCount();
            if (fanCount < 1 || fanCount > 5)
                throw new IOException("Invalid fan count. Manual control is blocked.");
            rpms = new int[fanCount];
            duties = new int[fanCount];
            stalls = new int[fanCount];
            ReadSensors(DateTime.UtcNow);
        }

        void ReadSensors(DateTime now)
        {
            valid = false;
            uint reading = hardware.Temperature();
            if (reading < 1 || reading > 125)
                throw new IOException("CPU temperature unavailable or invalid.");
            int[] readings = new int[fanCount];
            for (int i = 0; i < fanCount; i++)
            {
                if (!hardware.Select((byte)i))
                    throw new IOException("Fan selection failed.");
                int rpm = hardware.Rpm();
                if (rpm < 0 || rpm > 20000)
                    throw new IOException("Invalid RPM reading for fan " + (i + 1) + ".");
                readings[i] = rpm;
            }

            GpuSample graphics = gpu.Read(now);
            gpuTemperature = graphics != null && (now - graphics.Timestamp).TotalSeconds >= -0.5 && (now - graphics.Timestamp).TotalSeconds <= 3 ? graphics.Temperature : null;
            if (gpuTemperature.HasValue && (gpuTemperature.Value < 1 || gpuTemperature.Value > 125))
                throw new IOException("Invalid GPU temperature.");
            temperature = (int)reading;
            rpms = readings;
            valid = true;
        }

        void WriteDuty(int index, int duty)
        {
            byte pwm = Rules.Pwm(duty);
            if (!hardware.Select((byte)index))
                throw new IOException("Fan selection failed; write cancelled.");
            // A rejected mode/duty call can still leave the device partially changed.
            touched = true;
            released = false;
            if (!hardware.TestMode(1))
                throw new IOException("Manual mode rejected for fan " + (index + 1) + ".");
            if (!hardware.Duty(pwm))
                throw new IOException("PWM duty rejected for fan " + (index + 1) + ".");
            duties[index] = duty;
        }

        public void Configure(int requestedPercent)
        {
            Rules.Validate(requestedPercent);
            if (calibrating)
                throw new ArgumentException("Cancel calibration first.");
            mode = "Fixed";
            percent = requestedPercent;
            if (!manual)
                return;
            try
            {
                ReadSensors(DateTime.UtcNow);
                UpdateCooling(CurrentTime(DateTime.UtcNow));
                int duty = cooling ? Rules.MaxPercent : percent;
                for (int i = 0; i < fanCount; i++)
                    if (!cooling || duties[i] != duty)
                        WriteDuty(i, duty);
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        public void Enable(DateTime now)
        {
            if (!initialized)
                throw new InvalidOperationException("Hardware is not initialized.");
            try
            {
                if (!released)
                    throw new IOException("Previous reset is unconfirmed. Reset before enabling manual control.");
                mode = "Fixed";
                ReadSensors(now);
                if (NeedsCooling)
                    throw new IOException("Manual control blocked at 90 °C CPU or 85 °C GPU.");
                error = "";
                cooling = false;
                Array.Clear(stalls, 0, stalls.Length);
                for (int i = 0; i < fanCount; i++)
                    WriteDuty(i, percent);
                manual = true;
                enabledAt = lastAdjustment = now;
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        public bool Disable()
        {
            manual = false;
            cooling = false;
            severeHeatSince = null;
            calibrating = false;
            bool success = RestoreFirmware();
            error = success ? "" : "Automatic control is unconfirmed for one or more fans. Retry reset.";
            status = success ? "Automatic control" : "Reset needs attention";
            return success;
        }

        public void ResetFirmware()
        {
            if (!initialized || fanCount < 1 || fanCount > 5)
                throw new InvalidOperationException("Fan hardware is not initialized.");
            touched = true;
            released = false;
            Disable();
        }

        bool RestoreFirmware()
        {
            if (!touched)
                return released;
            bool success = true;
            for (int i = 0; i < fanCount; i++)
            {
                try
                {
                    if (!hardware.Select((byte)i) || !hardware.TestMode(0))
                        success = false;
                }
                catch
                {
                    success = false;
                }
            }

            released = success;
            if (success)
            {
                touched = false;
                Array.Clear(duties, 0, duties.Length);
            }

            return success;
        }

        void Fail(string message)
        {
            manual = false;
            cooling = false;
            severeHeatSince = null;
            calibrating = false;
            bool success = RestoreFirmware();
            error = message + (success ? " Manual control is off." : " RESET UNCONFIRMED: retry automatic control.");
            status = success ? "Automatic / needs attention" : "Reset needs attention";
        }

        public Snapshot Poll(DateTime now)
        {
            try
            {
                ReadSensors(now);
                now = CurrentTime(now);
                if (manual)
                {
                    if (mode == "Curve" && !gpuTemperature.HasValue && !curves.CpuFallback)
                        throw new IOException("GPU sensor unavailable.");
                    bool wasCooling = cooling;
                    if (!calibrating)
                        UpdateCooling(now);
                    for (int i = 0; i < fanCount; i++)
                    {
                        stalls[i] = (now - enabledAt).TotalSeconds >= 8 && rpms[i] < 200 ? stalls[i] + 1 : 0;
                        if (stalls[i] >= 3)
                            throw new IOException("Fan rotation unavailable.");
                    }

                    if (calibrating)
                    {
                        if (temperature >= 75)
                            throw new IOException("Calibration stopped: CPU is " + temperature + " °C (limit 75 °C).");
                        if (!gpuTemperature.HasValue)
                            throw new IOException("Calibration stopped: GPU temperature is unavailable.");
                        if (gpuTemperature.Value >= 70)
                            throw new IOException("Calibration stopped: GPU is " + gpuTemperature.Value + " °C (limit 70 °C).");
                        AdvanceCalibration(now);
                    }
                    else if ((cooling && !wasCooling) || (now - lastAdjustment).TotalSeconds >= (mode == "Curve" ? 1 : 2))
                    {
                        lastAdjustment = now;
                        int[] desired;
                        if (mode == "Curve")
                        {
                            desired = CurveMath.Targets(curves, temperature, gpuTemperature ?? temperature, fanCount);
                            if (cooling)
                            {
                                desired = Enumerable.Repeat(100, fanCount).ToArray();
                                response = new CurveResponse(desired);
                            }
                            else
                                desired = response.Step(desired, curves.HysteresisUp, curves.HysteresisDown, now);
                        }
                        else
                            desired = Enumerable.Repeat(cooling ? 100 : percent, fanCount).ToArray();
                        for (int i = 0; i < fanCount; i++)
                            if (mode == "Curve" || duties[i] != desired[i])
                                WriteDuty(i, desired[i]);
                    }
                }
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }

            return GetSnapshot();
        }

        public Snapshot GetSnapshot()
        {
            return new Snapshot
            {
                Connected = initialized && fanCount >= 1 && fanCount <= 5,
                Manual = manual,
                Valid = valid,
                ReleaseConfirmed = released,
                CoolingPriority = cooling,
                Temperature = temperature,
                Rpms = (int[])rpms.Clone(),
                Duties = (int[])duties.Clone(),
                Value = percent,
                Error = error,
                Status = manual ? (calibrating ? "Measuring maximum RPM" : cooling ? "Cooling priority: 100%" : mode == "Curve" ? "Automatic curve" : "Manual PWM") : status,
                Mode = manual ? (calibrating ? "Calibration" : mode) : "Firmware",
                GpuTemperature = gpuTemperature,
                PreviewDuties = Preview(),
                Calibrating = calibrating,
                CalibrationProgress = calibrationProgress,
                Calibration = calibration == null ? null : calibration.Clone(),
                Demo = demo
            };
        }

        void UpdateCooling(DateTime now)
        {
            if (temperature >= 98 || gpuTemperature >= 93)
                throw new IOException("Emergency thermal threshold reached (98 °C CPU / 93 °C GPU).");
            // Count sustained heat only after full cooling has actually been requested.
            if ((temperature >= 95 || gpuTemperature >= 90) && duties.All(duty => duty == 100))
            {
                if (!severeHeatSince.HasValue)
                    severeHeatSince = now;
                if ((now - severeHeatSince.Value).TotalSeconds >= 5)
                    throw new IOException("Temperature stayed at or above 95 °C CPU / 90 °C GPU despite full cooling.");
            }
            else
                severeHeatSince = null;
            if (NeedsCooling)
                cooling = true;
            else if (temperature <= 85 && (!gpuTemperature.HasValue || gpuTemperature <= 80))
                cooling = false;
        }

        bool NeedsCooling
        {
            get
            {
                return temperature >= 90 || gpuTemperature >= 85;
            }
        }

        public void ConfigureCurve(CurveSettings settings)
        {
            if (calibrating)
                throw new ArgumentException("Cancel calibration first.");
            if (settings == null)
                throw new ArgumentException("Curve settings missing.");
            settings.Validate(fanCount, false);
            curves = settings.Clone();
            if (manual && mode == "Curve")
                response = new CurveResponse(duties);
        }

        public void EnableCurve(DateTime now)
        {
            if (!initialized)
                throw new InvalidOperationException("Hardware is not initialized.");
            curves.Validate(fanCount, true);
            if (calibrating)
                throw new ArgumentException("Cancel calibration first.");
            if (manual && mode == "Curve")
                return;
            if (manual && !Disable())
                return;
            try
            {
                if (!released)
                    throw new IOException("Previous reset unconfirmed.");
                ReadSensors(now);
                if (!gpuTemperature.HasValue && !curves.CpuFallback)
                    throw new IOException("GPU sensor unavailable.");
                if (NeedsCooling)
                    throw new IOException("Temperature too high to enable curve control.");
                error = "";
                cooling = false;
                mode = "Curve";
                Array.Clear(stalls, 0, stalls.Length);
                int[] initial = CurveMath.Targets(curves, temperature, gpuTemperature ?? temperature, fanCount);
                for (int i = 0; i < fanCount; i++)
                    WriteDuty(i, initial[i]);
                response = new CurveResponse(duties);
                manual = true;
                enabledAt = now;
                lastAdjustment = now.AddSeconds(-1);
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        int[] Preview()
        {
            try
            {
                curves.Validate(fanCount, false);
                if (!gpuTemperature.HasValue && !curves.CpuFallback)
                    return new int[0];
                return CurveMath.Targets(curves, temperature, gpuTemperature ?? temperature, fanCount);
            }
            catch
            {
                return new int[0];
            }
        }

        public void LoadCalibration(FanCalibration value)
        {
            if (calibrating)
                throw new ArgumentException("Cancel calibration first.");
            value.Validate(fanCount);
            calibration = value.Clone();
        }

        public void StartCalibration(DateTime now)
        {
            if (!initialized)
                throw new InvalidOperationException("Hardware is not initialized.");
            if (calibrating)
                return;
            if (manual && !Disable())
                return;
            try
            {
                if (!released)
                    throw new IOException("Previous reset unconfirmed.");
                ReadSensors(now);
                if (!gpuTemperature.HasValue)
                    throw new IOException("Calibration needs a live GPU temperature reading. PWM curves do not need calibration.");
                if (temperature >= 70 || gpuTemperature.Value >= 65)
                    throw new IOException("Calibration needs CPU below 70 °C and GPU below 65 °C. Current: CPU " + temperature + " °C, GPU " + gpuTemperature.Value + " °C.");
                calibrationProgress = 0;
                error = "";
                cooling = false;
                for (int i = 0; i < fanCount; i++)
                    WriteDuty(i, 100);
                calibrating = true;
                manual = true;
                mode = "Calibration";
                // Settling begins after every native write has returned, not at command receipt.
                lastCalibrationWrite = enabledAt = CurrentTime(now);
                measurement = new MaximumRpmMeasurement(fanCount, enabledAt);
                Array.Clear(stalls, 0, stalls.Length);
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        void AdvanceCalibration(DateTime now)
        {
            FanCalibration measured = measurement.Observe(rpms, now);
            calibrationProgress = measurement.Progress;
            if (measured != null)
            {
                if (!Disable())
                    return;
                calibration = measured;
                calibrationProgress = 100;
                status = "Maximum RPM measured · firmware control";
                return;
            }

            if ((now - lastCalibrationWrite).TotalSeconds >= 1)
            {
                for (int i = 0; i < fanCount; i++)
                    WriteDuty(i, 100);
                lastCalibrationWrite = CurrentTime(now);
            }
        }

        DateTime CurrentTime(DateTime fallback)
        {
            return clock == null ? fallback : clock();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            manual = false;
            try
            {
                if (!RestoreFirmware())
                {
                    System.Threading.Thread.Sleep(100);
                    RestoreFirmware();
                }
            }
            finally
            {
                hardware.Dispose();
                gpu.Dispose();
            }
        }
    }
}
