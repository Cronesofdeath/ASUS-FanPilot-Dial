using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Animation;

namespace FanPilot
{
    internal sealed class CurveEditorController : IDisposable
    {
        readonly Preferences preferences;
        readonly Func<PerformanceMode, CurveSettings, bool, Task> apply;
        readonly Func<string, Task> command;
        readonly CurveChart cpu = new CurveChart(), gpu = new CurveChart();
        readonly Dictionary<PerformanceMode, CurveSettings> drafts = new Dictionary<PerformanceMode, CurveSettings>();
        readonly CurvePreset[] personalPresets;
        bool refreshingPresets;
        PresetAction presetAction;
        CurvePreset editingPreset;
        CurveChart selected;
        Snapshot state = new Snapshot();
        FanCalibration calibration;
        bool syncing, busy, disposed;
        string previousContext;
        bool stale;
        int fanCount;
        public Window Window { get; private set; }
        public CurveSettings Draft { get; private set; }
        public PerformanceMode EditingMode { get; private set; }
        public PerformanceMode TargetMode { get; private set; }
        public bool TargetAvailable { get; private set; }

        bool linkedToGHelper;
        PerformanceMode? detectedMode;
        public bool Transitioning;
        bool IsTargetProfile
        {
            get
            {
                return EditingMode == TargetMode && TargetAvailable;
            }
        }

        internal void SaveDraft()
        {
            CurveSettings previous = preferences.Profiles.Get(EditingMode);
            preferences.Profiles.Set(EditingMode, Draft);
            try
            {
                preferences.Save();
            }
            catch
            {
                preferences.Profiles.Set(EditingMode, previous);
                throw;
            }
        }

        internal void SelectProfile(PerformanceMode mode)
        {
            if (EditingMode == mode)
                return;
            drafts[EditingMode] = Draft;
            EditingMode = mode;
            CurveSettings draft;
            Draft = drafts.TryGetValue(mode, out draft) ? draft : preferences.Profiles.Get(mode);
            syncing = true;
            Find<TabControl>("ProfileTabs").SelectedIndex = mode == PerformanceMode.Silent ? 0 : mode == PerformanceMode.Turbo ? 2 : 1;
            syncing = false;
            Find<ComboBox>("PresetList").SelectedIndex = 0;
            Bind();
            Update(state);
        }

        public void SetTargetMode(PerformanceMode mode, bool available, bool linked, PerformanceMode? detected)
        {
            bool switchTab = available && linked && (mode != TargetMode || !linkedToGHelper || !TargetAvailable);
            detectedMode = detected;
            TargetMode = mode;
            TargetAvailable = available;
            linkedToGHelper = linked;
            if ((switchTab || !Window.IsVisible) && !busy)
                SelectProfile(mode);
        }

        public CurveEditorController(Window owner, Preferences preferences, Func<PerformanceMode, CurveSettings, bool, Task> apply, Func<string, Task> command)
        {
            this.preferences = preferences;
            this.apply = apply;
            this.command = command;
            personalPresets = CurvePreset.ReadPersonalPresets();
            Window = DesktopTheme.LoadWindow("Curves.xaml");
            Window.Icon = owner.Icon;
            WindowFrame.Apply(Window);
            WindowFrame.WireSecondaryControls(Window, "Curves");
            WindowFrame.EnableButton(Find<Button>("CurvesClose"));
            Find<Grid>("CpuPlot").Children.Add(cpu);
            Find<Grid>("GpuPlot").Children.Add(gpu);
            preferences.EnsureProfiles();
            EditingMode = TargetMode = preferences.ManualProfile.Value;
            TargetAvailable = true;
            try
            {
                Draft = preferences.Profiles.Get(EditingMode);
                Draft.Validate(5, false);
            }
            catch
            {
                Draft = new CurveSettings();
            }

            calibration = preferences.Calibration;
            if (calibration != null)
            {
                try
                {
                    int count = calibration.MaximumOnly ? (calibration.Maxima ?? new int[0]).Length : (calibration.Rpms ?? new int[0][]).Length;
                    if (count < 2)
                        throw new ArgumentException();
                    calibration.Validate(count);
                }
                catch (ArgumentException)
                {
                    calibration = null;
                }
            }

            selected = cpu;
            var presetList = Find<ComboBox>("PresetList");
            var updatePreset = Find<Button>("UpdatePreset");
            var renamePreset = Find<Button>("RenamePreset");
            var deletePreset = Find<Button>("DeletePreset");
            presetList.SelectionChanged += delegate
            {
                var preset = presetList.SelectedItem as CurvePreset;
                bool editable = preset != null;
                updatePreset.IsEnabled = renamePreset.IsEnabled = deletePreset.IsEnabled = editable;
                const string unavailable = "Choose a preset first.";
                updatePreset.ToolTip = editable ? "Replace the saved preset with this draft" : unavailable;
                renamePreset.ToolTip = editable ? "Rename the selected preset" : unavailable;
                deletePreset.ToolTip = editable ? "Review and confirm deletion" : unavailable;
                if (refreshingPresets || preset == null)
                    return;
                Draft = preset.Curve.Clone();
                Bind();
                Status("Loaded preset · " + preset.Name);
            };
            RefreshPresets();
            Find<Button>("NewPreset").Click += delegate
            {
                BeginPreset(PresetAction.Create);
            };
            updatePreset.Click += delegate
            {
                BeginPreset(PresetAction.Update);
            };
            renamePreset.Click += delegate
            {
                BeginPreset(PresetAction.Rename);
            };
            deletePreset.Click += delegate
            {
                BeginPreset(PresetAction.Delete);
            };
            Find<Slider>("PreviewCpu").ValueChanged += delegate
            {
                RefreshPreview();
            };
            Find<Slider>("PreviewGpu").ValueChanged += delegate
            {
                RefreshPreview();
            };
            Find<Button>("CancelPreset").Click += delegate
            {
                ShowPresetForm(false);
                Find<Button>("NewPreset").Focus();
            };
            var advanced = Find<ToggleButton>("AdvancedToggle");
            advanced.Checked += delegate
            {
                SetAdvancedOpen(true);
            };
            advanced.Unchecked += delegate
            {
                SetAdvancedOpen(false);
            };
            Find<Button>("SavePreset").Click += delegate
            {
                SavePreset();
            };
            Find<StackPanel>("PresetForm").PreviewKeyDown += delegate (object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape)
                {
                    ShowPresetForm(false);
                    Find<Button>("NewPreset").Focus();
                    args.Handled = true;
                }
                else if (args.Key == Key.Enter && Find<TextBox>("PresetName").IsKeyboardFocused)
                {
                    SavePreset();
                    args.Handled = true;
                }
            };
            cpu.Edited += Select;
            gpu.Edited += Select;
            cpu.GotKeyboardFocus += delegate
            {
                Select(cpu);
            };
            gpu.GotKeyboardFocus += delegate
            {
                Select(gpu);
            };
            Find<Button>("PointToggle").Click += delegate
            {
                var panel = Find<Border>("PointPanel");
                bool opening = panel.Visibility != Visibility.Visible;
                panel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
                Find<Border>("PreviewPanel").Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
                Find<Button>("PointToggle").Content = opening ? "Done editing" : "Edit point…";
                if (opening)
                {
                    var field = Find<TextBox>("PointTemperature");
                    field.Focus();
                    field.SelectAll();
                }
            };
            Find<Button>("CurvesClose").Click += delegate
            {
                Window.Close();
            };
            Window.Closing += async delegate (object sender, System.ComponentModel.CancelEventArgs args)
            {
                if (disposed)
                    return;
                args.Cancel = true;
                if (state.Calibrating)
                    await Run(delegate
                    {
                        return command("DISABLE");
                    });
                Window.Hide();
            };
            Find<Button>("SetPoint").Click += delegate
            {
                SetPoint();
            };
            Find<Button>("CurveDefaults").Click += delegate
            {
                Draft.Cpu = CurveSettings.DefaultPoints();
                Draft.Gpu = CurveSettings.DefaultPoints();
                Bind();
            };
            Find<Button>("SaveCurve").Click += async delegate
            {
                await SaveProfile();
            };
            Find<Button>("StopCurve").Click += async delegate
            {
                if (state.Manual && state.Mode == "Curve")
                    await Run(delegate
                    {
                        return command("DISABLE");
                    });
            };
            Find<Button>("Calibrate").Click += async delegate
            {
                await Run(delegate
                {
                    return command("CALIBRATE");
                });
            };
            Find<Button>("CancelCalibration").Click += async delegate
            {
                await Run(delegate
                {
                    return command("DISABLE");
                });
            };
            foreach (string name in new[]
            {
                "FanShift",
                "HysteresisUp",
                "HysteresisDown"
            }

            )
                Find<Slider>(name).ValueChanged += delegate
                {
                    if (!syncing)
                        ReadOptions();
                };
            foreach (string name in new[]
            {
                "Fan1Source",
                "Fan2Source"
            }

            )
                Find<ComboBox>(name).SelectionChanged += delegate
                {
                    if (!syncing)
                    {
                        ReadOptions();
                        RefreshCharts();
                    }
                };
            Find<CheckBox>("SharedControl").Click += delegate
            {
                ReadOptions();
                RefreshCharts();
            };
            Find<CheckBox>("CpuFallback").Click += delegate
            {
                ReadOptions();
            };
            Find<CheckBox>("ClampGrid").Click += delegate
            {
                RefreshCharts();
            };
            Find<ComboBox>("YAxis").SelectionChanged += delegate
            {
                if (!syncing)
                    RefreshCharts();
            };
            syncing = true;
            Find<TabControl>("ProfileTabs").SelectedIndex = EditingMode == PerformanceMode.Silent ? 0 : EditingMode == PerformanceMode.Turbo ? 2 : 1;
            syncing = false;
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(Find<TabControl>("ProfileTabs"), true);
            Find<TabControl>("ProfileTabs").SelectionChanged += delegate
            {
                if (syncing || disposed)
                    return;
                try
                {
                    int index = Find<TabControl>("ProfileTabs").SelectedIndex;
                    SelectProfile(index == 0 ? PerformanceMode.Silent : index == 2 ? PerformanceMode.Turbo : PerformanceMode.Balanced);
                }
                catch (Exception exception)
                {
                    Status(exception.Message, true);
                }
            };
            Bind();
        }

        void SetAdvancedOpen(bool open)
        {
            var drawer = Find<Grid>("AdvancedDrawer");
            double current = drawer.Width;
            double target = open ? Find<Border>("AdvancedPanel").Width : 0;
            drawer.BeginAnimation(FrameworkElement.WidthProperty, null);
            drawer.Width = target;
            drawer.IsHitTestVisible = open;
            bool animate = (preferences.AnimateFans ?? true) && SystemParameters.ClientAreaAnimation && Window.IsVisible;
            if (!animate || current == target)
            {
                drawer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            drawer.Visibility = Visibility.Visible;
            var slide = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.Stop
            };
            if (!open)
                slide.Completed += delegate
                {
                    if (Find<ToggleButton>("AdvancedToggle").IsChecked != true)
                        drawer.Visibility = Visibility.Collapsed;
                };
            drawer.BeginAnimation(FrameworkElement.WidthProperty, slide);
        }

        internal T Find<T>(string name)
            where T : class
        {
            return (T)Window.FindName(name);
        }

        public void Show()
        {
            Window.Show();
            Window.WindowState = WindowState.Normal;
            Window.Activate();
            Update(state);
        }

        public void Hide()
        {
            Window.Hide();
        }

        void Bind()
        {
            syncing = true;
            Find<Slider>("FanShift").Value = Draft.FanShift;
            Find<Slider>("HysteresisUp").Value = Draft.HysteresisUp;
            Find<Slider>("HysteresisDown").Value = Draft.HysteresisDown;
            Find<CheckBox>("SharedControl").IsChecked = Draft.Shared;
            Find<CheckBox>("CpuFallback").IsChecked = Draft.CpuFallback;
            Find<ComboBox>("Fan1Source").SelectedIndex = (int)(Draft.Fan1Source ?? FanTemperatureSource.Hotter);
            Find<ComboBox>("Fan2Source").SelectedIndex = (int)(Draft.Fan2Source ?? FanTemperatureSource.Hotter);
            syncing = false;
            Labels();
            RefreshCharts();
        }

        void ReadOptions()
        {
            Draft.FanShift = (int)Find<Slider>("FanShift").Value;
            Draft.HysteresisUp = (int)Find<Slider>("HysteresisUp").Value;
            Draft.HysteresisDown = (int)Find<Slider>("HysteresisDown").Value;
            Draft.SharedControl = Find<CheckBox>("SharedControl").IsChecked == true;
            Draft.Fan1Source = (FanTemperatureSource)Math.Max(0, Find<ComboBox>("Fan1Source").SelectedIndex);
            Draft.Fan2Source = (FanTemperatureSource)Math.Max(0, Find<ComboBox>("Fan2Source").SelectedIndex);
            Draft.CpuFallback = Find<CheckBox>("CpuFallback").IsChecked == true;
            Labels();
            RefreshPreview();
        }

        void Labels()
        {
            Find<TextBlock>("ShiftValue").Text = Draft.Shared ? "Auto" : Draft.FanShift + "%";
            Find<Slider>("FanShift").IsEnabled = !Draft.Shared;
            Find<ComboBox>("Fan1Source").IsEnabled = Find<ComboBox>("Fan2Source").IsEnabled = !Draft.Shared;
            Find<TextBlock>("FanResponseHelp").Text = Draft.Shared ? "Both fans follow the hotter component and higher curve demand." : "Each numbered fan follows its selected temperature source.";
            Find<TextBlock>("UpValue").Text = Draft.HysteresisUp + " s";
            Find<TextBlock>("DownValue").Text = Draft.HysteresisDown + " s";
        }

        void RefreshPreview()
        {
            int cpuTemperature = (int)Find<Slider>("PreviewCpu").Value;
            int gpuTemperature = (int)Find<Slider>("PreviewGpu").Value;
            int[] duties = CurveMath.Targets(Draft, cpuTemperature, gpuTemperature, 2);
            Find<TextBlock>("PreviewCpuValue").Text = cpuTemperature + " °C";
            Find<TextBlock>("PreviewGpuValue").Text = gpuTemperature + " °C";
            cpu.PreviewInput = CurveMath.Input(Draft, true, cpuTemperature, gpuTemperature);
            gpu.PreviewInput = CurveMath.Input(Draft, false, cpuTemperature, gpuTemperature);
            cpu.PreviewDuty = duties[0];
            gpu.PreviewDuty = duties[1];
            cpu.InvalidateVisual();
            gpu.InvalidateVisual();
            for (int i = 0; i < 2; i++)
            {
                string estimate = calibration == null ? "RPM uncalibrated" : calibration.ToRpm(i, duties[i]).ToString("N0") + " est. RPM";
                Find<TextBlock>("PreviewFan" + (i + 1)).Text = "Fan " + (i + 1) + "   " + duties[i] + "% PWM  ·  " + estimate;
            }
        }

        void Select(CurveChart chart)
        {
            RefreshPreview();
            cpu.SelectionActive = chart == cpu;
            gpu.SelectionActive = chart == gpu;
            cpu.InvalidateVisual();
            gpu.InvalidateVisual();
            selected = chart;
            CurvePoint point = chart.Points[chart.SelectedIndex];
            if (calibration == null)
            {
                Find<TextBox>("PointValue").Text = "—";
                Find<TextBlock>("PointUnit").Text = "RPM";
                return;
            }

            Find<TextBlock>("SelectedPoint").Text = (chart == cpu ? "Fan 1" : "Fan 2") + " · point " + (chart.SelectedIndex + 1);
            Find<TextBox>("PointTemperature").Text = point.Temperature.ToString();
            Find<TextBox>("PointValue").Text = Math.Round(chart.RpmAxis ? calibration.ToRpm(chart.FanIndex, point.Duty) : point.Duty).ToString();
            Find<TextBlock>("PointUnit").Text = chart.RpmAxis ? "RPM" : "%";
        }

        void SetPoint()
        {
            if (calibration == null)
            {
                Status("Measure maximum RPM before editing the curve.", true);
                return;
            }

            int temperature, value;
            if (!int.TryParse(Find<TextBox>("PointTemperature").Text, out temperature) || !int.TryParse(Find<TextBox>("PointValue").Text, out value))
            {
                Status("Enter whole numbers.");
                return;
            }

            CurvePoint before = selected.Points[selected.SelectedIndex];
            int duty = selected.RpmAxis ? calibration.ToDuty(selected.FanIndex, value) : value;
            if (selected.RpmAxis && (value < calibration.ToRpm(selected.FanIndex, 40) || value > calibration.ToRpm(selected.FanIndex, 100)))
            {
                Status("RPM must be within this fan's measured 40–100% range.");
                return;
            }

            selected.Points[selected.SelectedIndex] = new CurvePoint(temperature, duty);
            try
            {
                CurveSettings.ValidatePoints(selected.Points);
                RefreshCharts();
            }
            catch (ArgumentException exception)
            {
                selected.Points[selected.SelectedIndex] = before;
                Select(selected);
                Status(exception.Message);
            }
        }

        async Task SaveProfile()
        {
            await Run(async delegate
            {
                if (IsTargetProfile && calibration == null)
                    throw new InvalidOperationException("Measure maximum RPM before starting an RPM curve.");
                ReadOptions();
                Draft.Validate(IsTargetProfile ? fanCount : 5, IsTargetProfile);
                if (!IsTargetProfile)
                {
                    SaveDraft();
                    Status(EditingMode + " profile saved · laptop mode unchanged");
                    return;
                }

                await apply(EditingMode, Draft.Clone(), true);
                if (!state.Connected || !state.Manual || state.Mode != "Curve")
                    throw new IOException(String.IsNullOrEmpty(state.Error) ? "Curve did not start. Check the hardware connection." : state.Error);
                if (String.IsNullOrEmpty(state.Error))
                    Status("Curve applied");
            });
        }

        void RefreshPresets(CurvePreset selectedPreset = null)
        {
            var list = Find<ComboBox>("PresetList");
            var presets = PresetLibrary.List(preferences, personalPresets).ToArray();
            refreshingPresets = true;
            try
            {
                string prompt = presets.Length == 0 ? "No saved presets" : "Choose a preset";
                list.ItemsSource = new object[]
                {
                    prompt
                }.Concat(presets).ToArray();
                list.IsEnabled = presets.Length > 0;
                list.SelectedItem = selectedPreset ?? (object)prompt;
            }
            finally
            {
                refreshingPresets = false;
            }
        }

        void ShowPresetForm(bool show)
        {
            Find<Grid>("PresetPanel").Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            Find<StackPanel>("PresetForm").Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        void BeginPreset(PresetAction action)
        {
            presetAction = action;
            editingPreset = Find<ComboBox>("PresetList").SelectedItem as CurvePreset;
            var field = Find<TextBox>("PresetName");
            field.IsReadOnly = action == PresetAction.Update || action == PresetAction.Delete;
            field.Text = action == PresetAction.Create ? "" : editingPreset.Name;
            Find<TextBlock>("PresetFormLabel").Text = action == PresetAction.Create ? "Name this preset" : action == PresetAction.Rename ? "New preset name" : action == PresetAction.Update ? "Replace saved curve" : "Delete preset";
            Find<TextBlock>("PresetError").Visibility = Visibility.Collapsed;
            var confirm = Find<Button>("SavePreset");
            confirm.Content = action == PresetAction.Create ? "Save preset" : action == PresetAction.Update ? "Replace curve" : action == PresetAction.Delete ? "Confirm delete" : "Rename";
            ShowPresetForm(true);
            field.Focus();
            field.SelectAll();
        }

        void SavePreset()
        {
            var field = Find<TextBox>("PresetName");
            string name = field.Text.Trim();
            var errorLabel = Find<TextBlock>("PresetError");
            try
            {
                if (name.Length == 0)
                    throw new ArgumentException("Enter a preset name.");
                CurvePreset preset = PresetLibrary.Edit(preferences, personalPresets, presetAction, editingPreset, name, Draft);
                RefreshPresets(presetAction == PresetAction.Delete ? null : preset);
                ShowPresetForm(false);
                if (!Find<ComboBox>("PresetList").Focus())
                    Find<Button>("NewPreset").Focus();
                Status((presetAction == PresetAction.Delete ? "Deleted preset · " : "Saved preset · ") + preset.Name);
            }
            catch (Exception exception)
            {
                errorLabel.Text = exception is IOException || exception is UnauthorizedAccessException ? "Could not write the preset to settings. " + exception.Message : exception.Message;
                errorLabel.Visibility = Visibility.Visible;
                field.Focus();
            }
        }

        async Task Run(Func<Task> action)
        {
            if (busy)
                return;
            busy = true;
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                Status(exception.Message, true);
            }
            finally
            {
                busy = false;
                Update(state);
            }
        }

        void RefreshCharts()
        {
            cpu.Points = Draft.Cpu;
            gpu.Points = Draft.Gpu;
            cpu.SelectedIndex = Math.Min(cpu.SelectedIndex, Draft.Cpu.Length - 1);
            gpu.SelectedIndex = Math.Min(gpu.SelectedIndex, Draft.Gpu.Length - 1);
            bool rpm = true;
            cpu.Calibration = gpu.Calibration = calibration;
            cpu.FanIndex = 0;
            gpu.FanIndex = 1;
            cpu.RpmAxis = gpu.RpmAxis = rpm;
            cpu.IsEnabled = gpu.IsEnabled = calibration != null;
            Find<Button>("PointToggle").IsEnabled = calibration != null;
            Find<Button>("SetPoint").IsEnabled = calibration != null;
            Find<TextBox>("PointTemperature").IsEnabled = Find<TextBox>("PointValue").IsEnabled = calibration != null;
            cpu.Snap = gpu.Snap = Find<CheckBox>("ClampGrid").IsChecked == true;
            cpu.InvalidateVisual();
            gpu.InvalidateVisual();
            Select(selected);
        }

        public void Update(Snapshot snapshot, bool staleReadings = false)
        {
            stale = staleReadings;
            string previousMode = state.Mode;
            bool wasCalibrating = state.Calibrating;
            state = snapshot;
            if (previousMode != state.Mode)
                Status(state.Mode == "Curve" ? "Curve active" : state.Mode == "Fixed" ? "Fixed PWM active" : "Firmware control");
            if (wasCalibrating && !state.Calibrating)
                Status(state.CalibrationProgress == 100 ? "Calibration complete · firmware control" : "Calibration cancelled · firmware control");
            if (state.Rpms.Length != fanCount)
            {
                fanCount = state.Rpms.Length;
                Bind();
            }

            if (state.Calibration != null && (calibration == null || state.Calibration.Id != calibration.Id))
            {
                calibration = state.Calibration.Clone();
                preferences.Calibration = calibration.Clone();
                try
                {
                    preferences.Save();
                }
                catch (Exception exception)
                {
                    Status(exception.Message);
                }
            }

            if (calibration != null && fanCount > 0)
            {
                try
                {
                    calibration.Validate(fanCount);
                }
                catch
                {
                    calibration = null;
                    Find<ComboBox>("YAxis").SelectedIndex = 0;
                }
            }

            Find<ComboBoxItem>("RpmAxis").IsEnabled = calibration != null;
            bool runningCurve = state.Manual && state.Mode == "Curve";
            Find<Button>("StopCurve").IsEnabled = state.Connected && runningCurve && !Transitioning && !busy;
            Find<Button>("Calibrate").IsEnabled = state.Connected && state.Valid && !state.Calibrating && !busy;
            Find<Button>("SaveCurve").IsEnabled = !state.Calibrating && !Transitioning && !busy && (!IsTargetProfile || (state.Connected && state.Valid && !stale && calibration != null && (state.Manual || state.ReleaseConfirmed)));
            Find<Button>("SaveCurve").Content = !IsTargetProfile ? "Save for " + EditingMode : state.Mode == "Curve" ? "Apply changes" : "Start curve";
            Find<TextBlock>("ModeSource").Text = "G-Helper · " + (detectedMode.HasValue ? detectedMode.ToString() : "Unavailable") + (linkedToGHelper ? "  /  Following" : "  /  Cooling · " + TargetMode);
            Find<TextBlock>("RunningStatus").Text = !state.Manual && !state.ReleaseConfirmed ? "Firmware reset unconfirmed" : !state.Connected ? "Hardware unavailable" : state.Calibrating ? "Measuring maximum RPM" : runningCurve ? TargetMode + (state.CoolingPriority ? " · full cooling" : " curve running") : state.Manual ? "Fixed PWM running" : "Firmware control";
            string context = !TargetAvailable ? "G-Helper mode unavailable. Select a manual profile or restart G-Helper." : calibration == null ? "Measure max RPM in Advanced to edit the curve." : !IsTargetProfile ? "Previewing " + EditingMode + " while " + TargetMode + " is active." : "";
            if (!busy && !Transitioning && context != previousContext)
            {
                previousContext = context;
                Status(context);
            }

            Find<TabControl>("ProfileTabs").IsEnabled = !state.Calibrating && !busy && !Transitioning;
            Find<Button>("CurveDefaults").IsEnabled = !state.Calibrating;
            Find<Button>("CancelCalibration").Visibility = state.Calibrating ? Visibility.Visible : Visibility.Collapsed;
            var progress = Find<ProgressBar>("CalibrationProgress");
            progress.Visibility = state.Calibrating ? Visibility.Visible : Visibility.Collapsed;
            progress.Value = state.CalibrationProgress;
            if (state.Calibrating || !String.IsNullOrEmpty(state.Error))
                Status(String.IsNullOrEmpty(state.Error) ? state.Status + " · " + state.CalibrationProgress + "%" : state.Error, !String.IsNullOrEmpty(state.Error));
            if (!state.Manual && !state.ReleaseConfirmed)
                Status("Firmware reset unconfirmed. Check the dashboard.", true);
            int cpuInput = state.Temperature, gpuInput = state.GpuTemperature ?? state.Temperature;
            string demand = "—";
            if (Draft.Shared && state.Valid && !stale && fanCount >= 2 && (state.GpuTemperature.HasValue || Draft.CpuFallback))
            {
                int duty = CurveMath.Targets(Draft, cpuInput, gpuInput, fanCount)[0];
                demand = duty + "% PWM";
            }

            Find<TextBlock>("SharedTarget").Text = Draft.Shared ? "Shared draft target · " + demand : "";
            cpu.LiveTemperature = state.Valid && !stale ? (double? )CurveMath.Input(Draft, true, cpuInput, gpuInput) : null;
            gpu.LiveTemperature = state.Valid && !stale && (state.GpuTemperature.HasValue || Draft.CpuFallback) ? (double? )CurveMath.Input(Draft, false, cpuInput, gpuInput) : null;
            LiveRpm("Fan1Rpm", 0);
            LiveRpm("Fan2Rpm", 1);
            // Do not replace point edit fields while a user is typing.
            cpu.InvalidateVisual();
            gpu.InvalidateVisual();
            if (cpu.Calibration != calibration)
                RefreshCharts();
        }

        void LiveRpm(string name, int index)
        {
            string rpm = index >= 0 && index < state.Rpms.Length && state.Valid && !stale ? state.Rpms[index].ToString("N0") : "—";
            Find<TextBlock>(name).Text = rpm;
        }

        internal void Status(string message, bool failure = false)
        {
            TextBlock label = Find<TextBlock>("CurveStatus");
            label.Text = message;
            label.Visibility = String.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            label.ToolTip = message;
            label.SetResourceReference(TextBlock.ForegroundProperty, failure ? "StudioWarningText" : "StudioMutedText");
        }

        public void Dispose()
        {
            disposed = true;
            Window.Close();
        }
    }
}
