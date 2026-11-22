using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FanPilot
{
    internal sealed class MainWindowController
    {
        readonly bool demo, background;
        readonly Preferences preferences;
        readonly SettingsController settings;
        readonly TrayController tray;
        CurveEditorController curves;
        readonly IModeSource modeSource;
        readonly ModeTracker modeTracker = new ModeTracker();
        PerformanceMode activeProfile;
        bool refreshingMode, profileOperation;
        bool Following
        {
            get
            {
                return preferences.FollowGHelper ?? true;
            }
        }

        bool ProfileAvailable
        {
            get
            {
                return !Following || modeTracker.DetectedMode.HasValue;
            }
        }

        internal PerformanceMode ActiveProfile
        {
            get
            {
                return activeProfile;
            }
        }

        internal IModeSource ModeSource
        {
            get
            {
                return modeSource;
            }
        }

        readonly FanSession session;
        readonly DispatcherTimer poll = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        readonly DispatcherTimer edit = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        readonly TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
        readonly List<FanTile> tiles = new List<FanTile>();
        readonly CheckBox toggle;
        readonly TextBox target;
        readonly Slider slider;
        bool deferredStartup;
        bool syncing, starting, exiting, closed, changingManual, curveLayout;
        bool CurveSelected
        {
            get
            {
                return Find<ComboBox>("ControlMode").SelectedIndex == 1;
            }
        }

        internal CurveEditorController CurveEditor
        {
            get
            {
                return curves;
            }
        }

        bool? requestedManual;
        int percent;
        public Window Window { get; private set; }

        internal TrayController Tray
        {
            get
            {
                return tray;
            }
        }

        internal SettingsController Settings
        {
            get
            {
                return settings;
            }
        }

        internal Preferences Preferences
        {
            get
            {
                return preferences;
            }
        }

        internal Snapshot CurrentSnapshot
        {
            get
            {
                return session.State;
            }
        }

        internal Task Ready
        {
            get
            {
                return ready.Task;
            }
        }

        public MainWindowController(bool simulation, bool startInTray, bool allowTestInput = false, IModeSource source = null)
        {
            if (allowTestInput && !simulation)
                throw new ArgumentException("Test input requires simulated hardware.");
            demo = simulation;
            background = startInTray;
            deferredStartup = startInTray;
            session = new FanSession(demo, Preferences.FilePath);
            preferences = Preferences.Load();
            preferences.EnsureProfiles();
            activeProfile = preferences.ManualProfile.Value;
            modeSource = source ?? (simulation ? (IModeSource)new DemoModeSource() : new GHelperModeSource());
            percent = preferences.Percent;
            DesktopTheme.Apply(preferences.ThemeIndex);
            Window = DesktopTheme.LoadWindow("Shell.xaml");
            if (background)
                Window.Opacity = 0;
            WindowFrame.Apply(Window);
            Window.Title = demo ? "ASUS FanPilot Dial · Preview" : "ASUS FanPilot Dial";
            System.Drawing.Icon icon;
            using (Stream resource = typeof(MainWindowController).Assembly.GetManifestResourceStream("FanPilot.ico"))
                icon = new System.Drawing.Icon(resource);
            Window.Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            settings = new SettingsController(Window, preferences, allowTestInput, ShowError);
            settings.ToggleRequested += async delegate
            {
                await ToggleAsync();
            };
            settings.Changed += UpdateMotion;
            tray = new TrayController(icon);
            settings.Changed += tray.RefreshTheme;
            tray.Open += async delegate
            {
                Show();
                await WakeAsync();
            };
            tray.Toggle += async delegate
            {
                await ToggleAsync();
            };
            tray.Automatic += async delegate
            {
                if (!CurrentSnapshot.ReleaseConfirmed && !CurrentSnapshot.Manual)
                    await RetryAsync();
                else
                    await ChangeManualAsync(false);
            };
            tray.Exit += async delegate
            {
                await ExitAsync();
            };
            toggle = Find<CheckBox>("ManualToggle");
            target = Find<TextBox>("Target");
            slider = Find<Slider>("TargetSlider");
            syncing = true;
            Find<ComboBox>("ActiveProfile").SelectedIndex = Following ? FollowModeIndex : ModeIndex(activeProfile);
            Find<ComboBox>("ControlMode").SelectedIndex = (preferences.UseCurveControl ?? true) ? 1 : 0;
            syncing = false;
            BuildTiles(2);
            LoadTarget();
            WireControls();
            WireWindow();
        }

        internal T Find<T>(string name)
            where T : class
        {
            T element = (Window.FindName(name) ?? settings.Find<T>(name) ?? (curves == null ? null : curves.Window.FindName(name))) as T;
            if (element == null)
                throw new InvalidOperationException("Missing UI element: " + name);
            return element;
        }

        const int FollowModeIndex = 3;
        static int ModeIndex(PerformanceMode mode)
        {
            return mode == PerformanceMode.Silent ? 0 : mode == PerformanceMode.Turbo ? 2 : 1;
        }

        static PerformanceMode IndexMode(int index)
        {
            return index == 0 ? PerformanceMode.Silent : index == 2 ? PerformanceMode.Turbo : PerformanceMode.Balanced;
        }

        void WireControls()
        {
            Find<ComboBox>("ActiveProfile").SelectionChanged += async delegate
            {
                int selection = Find<ComboBox>("ActiveProfile").SelectedIndex;
                if (syncing || changingManual || CurrentSnapshot.Calibrating || selection < 0)
                    return;
                try
                {
                    preferences.FollowGHelper = selection == FollowModeIndex;
                    if (Following)
                        await RefreshModeAsync();
                    else
                    {
                        await SwitchProfileAsync(IndexMode(selection));
                        preferences.ManualProfile = activeProfile;
                    }

                    preferences.Save();
                    Render();
                }
                catch (Exception exception)
                {
                    ShowError(exception.Message);
                }
            };
            Find<Button>("EditCurves").Click += delegate
            {
                ShowCurves();
            };
            Find<ComboBox>("ControlMode").SelectionChanged += async delegate
            {
                if (syncing)
                    return;
                preferences.UseCurveControl = CurveSelected;
                try
                {
                    preferences.Save();
                }
                catch (Exception exception)
                {
                    ShowError(exception.Message);
                }

                if (CurrentSnapshot.Manual)
                    await ChangeManualAsync(false);
                Render();
            };
            toggle.Click += async delegate
            {
                if (!syncing)
                    await ChangeManualAsync(toggle.IsChecked == true);
            };
            slider.ValueChanged += delegate
            {
                if (syncing)
                    return;
                percent = (int)Math.Round(slider.Value);
                target.Text = percent.ToString();
                Remember();
                edit.Stop();
                edit.Start();
            };
            target.LostKeyboardFocus += async delegate
            {
                if (TryApplyTarget())
                    await ConfigureAsync();
            };
            target.KeyDown += async delegate (object sender, KeyEventArgs args)
            {
                if (args.Key != Key.Enter)
                    return;
                args.Handled = true;
                if (TryApplyTarget())
                    await ConfigureAsync();
                Keyboard.ClearFocus();
            };
            Find<Button>("Minus").Click += async delegate
            {
                Adjust(-1);
                await ConfigureAsync();
            };
            Find<Button>("Plus").Click += async delegate
            {
                Adjust(1);
                await ConfigureAsync();
            };
            Find<Button>("Retry").Click += async delegate
            {
                await RetryAsync();
            };
            Find<Button>("ExitUnconfirmed").Click += async delegate
            {
                if (MessageBox.Show(Window, "Firmware control is still unconfirmed. Exit and keep this warning for the next launch?", "ASUS FanPilot Dial", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                    await ExitAsync(true);
            };
            Find<Button>("OpenLog").Click += delegate
            {
                Process.Start("notepad.exe", "\"" + AppPaths.Log + "\"");
            };
            Find<Button>("Minimize").Click += delegate
            {
                Window.WindowState = WindowState.Minimized;
            };
            Find<Button>("WindowClose").Click += delegate
            {
                Window.Close();
            };
            Find<Button>("OpenSettings").Click += delegate
            {
                settings.Show();
            };
            foreach (string name in new[]
            {
                "OpenSettings",
                "Minimize",
                "WindowClose"
            }

            )
                WindowFrame.EnableButton(Find<Button>(name));
            edit.Tick += async delegate
            {
                edit.Stop();
                await ConfigureAsync();
            };
            poll.Tick += async delegate
            {
                if (!session.IsBusy && session.Connected && !exiting)
                {
                    await SendAsync("POLL");
                    await RefreshModeAsync();
                }
                else if (session.Stale)
                    Render();
            };
        }

        void WireWindow()
        {
            Window.SourceInitialized += delegate
            {
                settings.InitializeShortcut();
            };
            Window.StateChanged += delegate
            {
                if (Window.WindowState == WindowState.Minimized)
                    Hide();
            };
            Window.IsVisibleChanged += delegate
            {
                UpdateMotion();
            };
            Window.Closing += async delegate (object sender, System.ComponentModel.CancelEventArgs args)
            {
                if (closed)
                    return;
                args.Cancel = true;
                if (preferences.CloseToTray && !exiting)
                    Hide();
                else
                    await ExitAsync();
            };
            Window.ContentRendered += async delegate
            {
                if (ready.Task.IsCompleted)
                    return;
                try
                {
                    Window.MaxHeight = SystemParameters.WorkArea.Height - 20;
                    if (background)
                    {
                        Hide();
                        Window.Opacity = 1;
                        Render();
                        // An already-elevated launch from the old task can remove that task without a prompt.
                        if (!demo && AppPaths.Administrator)
                            await MigrateStartupAsync();
                    }
                    else
                        await WakeAsync();
                    ready.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    ready.TrySetException(exception);
                    ShowError(exception.Message);
                }
            };
        }

        void LoadTarget()
        {
            syncing = true;
            slider.Value = percent;
            target.Text = percent.ToString();
            syncing = false;
        }

        internal bool TryApplyTarget()
        {
            int parsed;
            if (!int.TryParse(target.Text.Replace(",", ""), out parsed))
            {
                target.Text = percent.ToString();
                return false;
            }

            try
            {
                Rules.Validate(parsed);
                percent = parsed;
                LoadTarget();
                Remember();
                return true;
            }
            catch (ArgumentException)
            {
                target.Text = percent.ToString();
                Find<TextBlock>("ModeHelp").Text = "Valid range: 40–100%.";
                return false;
            }
        }

        void Adjust(int change)
        {
            percent = Math.Max(Rules.MinPercent, Math.Min(Rules.MaxPercent, percent + change));
            LoadTarget();
            Remember();
        }

        void Remember()
        {
            preferences.Percent = percent;
            Find<TextBlock>("ModeHelp").Text = "Manual mode overrides firmware control.";
            try
            {
                preferences.Save();
            }
            catch (Exception exception)
            {
                ShowError("Settings save failed: " + exception.Message);
            }
        }

        internal async Task ConfigureAsync()
        {
            edit.Stop();
            Remember();
            if (session.Connected && !CurveSelected && !CurrentSnapshot.Calibrating)
                await SendAsync("CONFIG|Percent|" + percent);
        }

        async Task MigrateStartupAsync()
        {
            if (demo || !StartupTask.LegacyEnabled)
                return;
            try
            {
                await Task.Run(delegate
                {
                    StartupTask.SetStartup(true);
                });
                settings.RefreshStartup();
            }
            catch (Exception exception)
            {
                ShowError("Remove the old elevated startup task in Settings. " + exception.Message);
            }
        }

        internal async Task WakeAsync()
        {
            if (exiting || starting || (!deferredStartup && ready.Task.IsCompleted))
                return;
            deferredStartup = false;
            await InitializeAsync();
            await MigrateStartupAsync();
        }

        internal async Task RetryAsync()
        {
            if (exiting || starting || changingManual)
                return;
            Find<Border>("Notice").Visibility = Visibility.Collapsed;
            if (deferredStartup)
            {
                await WakeAsync();
                return;
            }

            if (session.Connected && !CurrentSnapshot.ReleaseConfirmed)
                await SendAsync(CurrentSnapshot.Manual ? "DISABLE" : "RESET");
            else
                await InitializeAsync();
        }

        async Task InitializeAsync()
        {
            if (starting || exiting)
                return;
            starting = true;
            poll.Stop();
            Find<Button>("Retry").IsEnabled = false;
            Find<Border>("Notice").Visibility = Visibility.Collapsed;
            try
            {
                await RefreshModeAsync();
                await session.ConnectAsync();
                if (!session.Connected)
                    return;
                await ConfigureAsync();
                await LoadCurveSettingsAsync();
                if (session.Connected)
                    poll.Start();
            }
            catch (Exception exception)
            {
                session.ReportFailure(exception);
                ShowError(exception.Message);
            }
            finally
            {
                starting = false;
                Find<Button>("Retry").IsEnabled = true;
                Render();
            }
        }

        async Task LoadCurveSettingsAsync()
        {
            int fanCount = CurrentSnapshot.Rpms.Length;
            if (fanCount < 2)
                return;
            try
            {
                CurveSettings profile = preferences.Profiles.Get(activeProfile);
                profile.Validate(fanCount, false);
                await SendAsync("CURVE|" + CurveCodec.Encode(profile));
                if (preferences.Calibration != null)
                {
                    preferences.Calibration.Validate(fanCount);
                    await SendAsync("CALIBRATION|" + CurveCodec.Encode(preferences.Calibration));
                }
            }
            catch (ArgumentException exception)
            {
                ShowError(exception.Message);
            }
        }

        internal async Task SendAsync(string command)
        {
            if (await session.SendAsync(command))
            {
                if (!session.Connected)
                    poll.Stop();
                Render();
            }
        }

        internal async Task ChangeManualAsync(bool enabled)
        {
            if (changingManual || exiting || !session.Connected)
                return;
            changingManual = true;
            requestedManual = enabled;
            Render();
            try
            {
                if (enabled && CurveSelected)
                {
                    if (CurrentSnapshot.Calibration == null)
                        throw new ArgumentException("Measure maximum RPM in Fan curves before starting an RPM curve.");
                    if (!ProfileAvailable)
                        throw new ArgumentException("G-Helper mode unavailable. Turn off mode following to select a profile manually.");
                    CurveSettings profile = preferences.Profiles.Get(activeProfile);
                    profile.Validate(CurrentSnapshot.Rpms.Length, true);
                    await SendAsync("CURVE|" + CurveCodec.Encode(profile));
                    await SendAsync("ENABLECURVE");
                }
                else
                {
                    if (enabled)
                        await ConfigureAsync();
                    await SendAsync(enabled ? "ENABLE" : "DISABLE");
                }
            }
            catch (ArgumentException exception)
            {
                ShowError(exception.Message);
                if (CurveSelected)
                {
                    ShowCurves();
                    curves.Status(exception.Message);
                }
            }
            finally
            {
                requestedManual = null;
                changingManual = false;
                Render();
            }
        }

        async Task ApplyCurveAsync(PerformanceMode mode, CurveSettings profile, bool enabled)
        {
            if (mode != activeProfile || !ProfileAvailable)
                throw new ArgumentException("This is a preview profile. The laptop mode has not changed.");
            profileOperation = true;
            try
            {
                profile.Validate(CurrentSnapshot.Rpms.Length, enabled);
                CurveSettings previous = preferences.Profiles.Get(mode);
                bool? previousControl = preferences.UseCurveControl;
                preferences.Profiles.Set(mode, profile);
                if (enabled)
                    preferences.UseCurveControl = true;
                try
                {
                    preferences.Save();
                }
                catch
                {
                    preferences.Profiles.Set(mode, previous);
                    preferences.UseCurveControl = previousControl;
                    throw;
                }

                if (enabled)
                {
                    if (CurrentSnapshot.Manual && CurrentSnapshot.Mode != "Curve")
                        await ChangeManualAsync(false);
                    syncing = true;
                    Find<ComboBox>("ControlMode").SelectedIndex = 1;
                    syncing = false;
                }

                if (enabled && !(CurrentSnapshot.Manual && CurrentSnapshot.Mode == "Curve"))
                    await ChangeManualAsync(true);
                else
                    await SendAsync("CURVE|" + CurveCodec.Encode(profile));
            }
            finally
            {
                profileOperation = false;
            }
        }

        internal async Task RefreshModeAsync()
        {
            if (refreshingMode || changingManual || profileOperation || exiting)
                return;
            PerformanceMode? previousDetection = modeTracker.DetectedMode;
            refreshingMode = true;
            string failure = null;
            try
            {
                PerformanceMode? read = await Task.Run(delegate
                {
                    return modeSource.Read();
                });
                if (changingManual || profileOperation || session.IsBusy || exiting)
                    return;
                bool curveOrCalibration = CurrentSnapshot.Mode == "Curve" || CurrentSnapshot.Calibrating;
                if (!modeTracker.Observe(read, Following && read != activeProfile && curveOrCalibration))
                    return;
                if (Following && read.HasValue)
                    await SwitchProfileAsync(read.Value);
                else if (Following && curveOrCalibration)
                {
                    await ChangeManualAsync(false);
                    ShowError(CurrentSnapshot.ReleaseConfirmed ? "G-Helper mode unavailable. Fan control returned to firmware." : "G-Helper mode unavailable. Firmware reset unconfirmed.");
                }
            }
            catch (Exception exception)
            {
                failure = "Mode following: " + exception.Message;
            }
            finally
            {
                refreshingMode = false;
                if (modeTracker.DetectedMode != previousDetection)
                    Render();
            }

            if (failure != null)
            {
                if (Following && (CurrentSnapshot.Mode == "Curve" || CurrentSnapshot.Calibrating))
                    await ChangeManualAsync(false);
                ShowError(failure);
            }
        }

        internal async Task SwitchProfileAsync(PerformanceMode mode)
        {
            if (!ModeProfiles.IsValid(mode))
                throw new ArgumentException("Unknown cooling profile.");
            if (mode == activeProfile)
                return;
            bool wasCurve = CurrentSnapshot.Mode == "Curve";
            if (CurrentSnapshot.Calibrating)
                await SendAsync("DISABLE");
            activeProfile = mode;
            CurveSettings profile = preferences.Profiles.Get(mode);
            if (!wasCurve)
            {
                Render();
                return;
            }

            changingManual = true;
            requestedManual = true;
            Render();
            try
            {
                string rejected = null;
                try
                {
                    profile.Validate(CurrentSnapshot.Rpms.Length, true);
                }
                catch (ArgumentException exception)
                {
                    rejected = exception.Message;
                }

                if (rejected != null)
                {
                    await SendAsync("DISABLE");
                    ShowError("Profile needs setup: " + rejected);
                    return;
                }

                await SendAsync("CURVE|" + CurveCodec.Encode(profile));
            }
            finally
            {
                changingManual = false;
                requestedManual = null;
                Render();
            }
        }

        async Task ToggleAsync()
        {
            if (changingManual || exiting)
                return;
            if (deferredStartup)
                await WakeAsync();
            if (!session.Connected)
            {
                Show();
                return;
            }

            await ChangeManualAsync(!CurrentSnapshot.Manual && CurrentSnapshot.ReleaseConfirmed);
        }

        void Render()
        {
            syncing = true;
            if (!Window.IsVisible)
            {
                if (curves != null && curves.Window.IsVisible)
                    RefreshCurveEditor();
                tray.Update(CurrentSnapshot);
                return;
            }

            bool displayed = requestedManual ?? CurrentSnapshot.Manual;
            if (toggle.IsChecked != displayed)
                toggle.IsChecked = displayed;
            Find<ComboBox>("ActiveProfile").SelectedIndex = Following ? FollowModeIndex : ModeIndex(activeProfile);
            syncing = false;
            Find<ComboBox>("ActiveProfile").IsEnabled = !changingManual && !CurrentSnapshot.Calibrating;
            Find<TextBlock>("ModeLinkState").Text = "G-Helper   /   " + (modeTracker.DetectedMode.HasValue ? modeTracker.DetectedMode.ToString() : "Unavailable");
            Find<TextBlock>("CoolingLabel").Text = Following && !ProfileAvailable ? "FANPILOT COOLING · WAITING" : "FANPILOT · " + activeProfile.ToString().ToUpperInvariant() + " COOLING";
            Find<TextBlock>("ControlAction").Text = CurrentSnapshot.Calibrating ? "Calibrating" : CurveSelected ? (displayed ? "Stop curve" : "Start curve") : (displayed ? "Stop fixed PWM" : "Start fixed PWM");
            bool live = CurrentSnapshot.Connected && CurrentSnapshot.Valid && session.Connected && !session.Stale;
            Find<TextBlock>("Badge").Text = session.Stale ? "STALE READINGS" : demo ? "PREVIEW" : !live ? "UNAVAILABLE" : CurrentSnapshot.Mode == "Curve" ? "CURVE" : CurrentSnapshot.Manual ? "FIXED" : "LIVE";
            Find<TextBlock>("Cpu").Text = live ? "CPU  " + CurrentSnapshot.Temperature + " °C" : "CPU  — °C";
            Find<TextBlock>("ControlStatus").Text = ControlStatus(live);
            toggle.IsEnabled = live && (CurrentSnapshot.Manual || CurrentSnapshot.ReleaseConfirmed) && !CurrentSnapshot.Calibrating && !changingManual && !starting && !exiting;
            Find<ComboBox>("ControlMode").IsEnabled = !CurrentSnapshot.Calibrating && !changingManual;
            slider.IsEnabled = target.IsEnabled = Find<Button>("Plus").IsEnabled = Find<Button>("Minus").IsEnabled = !CurveSelected && !CurrentSnapshot.Calibrating;
            Find<TextBlock>("ModeHelp").Text = CurveSelected ? "Temperature-based fan control" : "Fixed PWM duty · 40–100%";
            Find<TextBlock>("Gpu").Text = live && CurrentSnapshot.GpuTemperature.HasValue ? "GPU  " + CurrentSnapshot.GpuTemperature.Value + " °C" : "GPU  — °C";
            Find<FrameworkElement>("FixedTarget").Visibility = CurveSelected ? Visibility.Collapsed : Visibility.Visible;
            slider.Visibility = Find<FrameworkElement>("FixedRange").Visibility = CurveSelected ? Visibility.Collapsed : Visibility.Visible;
            Find<TextBlock>("SpeedTarget").Text = CurveSelected ? "CPU / GPU temperature curves" : "Direct PWM control";
            if (curveLayout != CurveSelected)
            {
                curveLayout = CurveSelected;
                Window.Height = Math.Max(Window.MinHeight, curveLayout ? 445 : 550);
            }

            if (curves != null && curves.Window.IsVisible)
                RefreshCurveEditor();
            if (live)
            {
                if (tiles.Count != CurrentSnapshot.Rpms.Length)
                    BuildTiles(CurrentSnapshot.Rpms.Length);
                for (int i = 0; i < tiles.Count; i++)
                    tiles[i].Update(CurrentSnapshot.Rpms[i], CurrentSnapshot.Manual ? CurrentSnapshot.Duties[i] + "% PWM duty" : CurrentSnapshot.ReleaseConfirmed ? "Automatic" : "Control unconfirmed", MotionEnabled);
            }
            else
                foreach (FanTile tile in tiles)
                    tile.Offline();
            bool unconfirmed = !CurrentSnapshot.Manual && !CurrentSnapshot.ReleaseConfirmed;
            Find<Button>("Retry").Content = unconfirmed ? "Retry reset" : "Retry";
            Find<Button>("ExitUnconfirmed").Visibility = unconfirmed ? Visibility.Visible : Visibility.Collapsed;
            if (unconfirmed)
                ShowError("Firmware reset unconfirmed. " + CurrentSnapshot.Error);
            else if (!String.IsNullOrEmpty(CurrentSnapshot.Error))
                ShowError(CurrentSnapshot.Error);
            else if (live)
                Find<Border>("Notice").Visibility = Visibility.Collapsed;
            tray.Update(CurrentSnapshot);
        }

        string ControlStatus(bool live)
        {
            if (!CurrentSnapshot.Manual && !CurrentSnapshot.ReleaseConfirmed)
                return "Firmware reset unconfirmed";
            if (changingManual)
                return requestedManual == true ? "Applying fan control…" : "Restoring firmware control…";
            if (deferredStartup)
                return "Automatic · firmware control";
            if (!live)
                return session.Stale ? "Waiting for fresh readings" : "Hardware unavailable";
            return CurrentSnapshot.Calibrating ? "Calibration in progress" : CurrentSnapshot.Manual ? (CurrentSnapshot.Mode == "Curve" ? activeProfile + " curve running" : "Fixed PWM running") : "Firmware control";
        }

        void ShowCurves()
        {
            if (curves == null)
                curves = new CurveEditorController(Window, preferences, ApplyCurveAsync, SendAsync);
            RefreshCurveEditor();
            curves.Show();
        }

        void RefreshCurveEditor()
        {
            if (curves == null)
                return;
            curves.Transitioning = changingManual;
            curves.SetTargetMode(activeProfile, ProfileAvailable, Following, modeTracker.DetectedMode);
            curves.Update(CurrentSnapshot, session.Stale);
        }

        void ShowError(string error)
        {
            Find<TextBlock>("NoticeText").Text = error;
            Find<Border>("Notice").Visibility = Visibility.Visible;
        }

        void BuildTiles(int count)
        {
            UniformGrid panel = Find<UniformGrid>("FanCards");
            panel.Children.Clear();
            tiles.Clear();
            panel.Columns = 2;
            for (int i = 0; i < count; i++)
            {
                FanTile tile = new FanTile(i + 1);
                tiles.Add(tile);
                panel.Children.Add(tile.Root);
            }
        }

        bool MotionEnabled
        {
            get
            {
                return Window.IsVisible && (settings.Window == null || !settings.Window.IsVisible) && (preferences.AnimateFans ?? true);
            }
        }

        void UpdateMotion()
        {
            foreach (FanTile tile in tiles)
                tile.Motion(MotionEnabled);
        }

        internal Task RefreshGpuAsync()
        {
            Find<TextBlock>("Gpu").Text = CurrentSnapshot.Valid && CurrentSnapshot.Connected && CurrentSnapshot.GpuTemperature.HasValue ? "GPU  " + CurrentSnapshot.GpuTemperature.Value + " °C" : "GPU  — °C";
            return Task.FromResult(true);
        }

        internal void Hide()
        {
            Window.Hide();
            poll.Interval = TimeSpan.FromSeconds(Following ? 1 : 2);
            UpdateMotion();
        }

        internal void Show()
        {
            Window.Show();
            Window.WindowState = WindowState.Normal;
            Window.Activate();
            poll.Interval = TimeSpan.FromSeconds(1);
            Render();
            UpdateMotion();
        }

        internal async Task ExitAsync(bool leaveUnconfirmed = false)
        {
            if (exiting)
                return;
            if (starting)
            {
                ShowError("Wait for the hardware connection to finish before exiting.");
                return;
            }

            exiting = true;
            poll.Stop();
            edit.Stop();
            if (!await session.StopAsync(leaveUnconfirmed))
            {
                exiting = false;
                if (session.Connected)
                    poll.Start();
                Show();
                ShowError("Firmware reset unconfirmed. Retry reset before exiting.");
                return;
            }

            settings.Dispose();
            if (curves != null)
                curves.Dispose();
            tray.Dispose();
            closed = true;
            Window.Close();
        }
    }
}
