using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FanPilot
{
    internal sealed class UiSmokeTests
    {
        readonly MainWindowController app;
        readonly string smoke;
        Window window
        {
            get
            {
                return app.Window;
            }
        }

        Window settingsWindow
        {
            get
            {
                return app.Settings.Window;
            }
        }

        Snapshot state
        {
            get
            {
                return app.CurrentSnapshot;
            }
        }

        Preferences prefs
        {
            get
            {
                return app.Preferences;
            }
        }

        CheckBox toggle
        {
            get
            {
                return Get<CheckBox>("ManualToggle");
            }
        }

        TextBox target
        {
            get
            {
                return Get<TextBox>("Target");
            }
        }

        Button shortcut
        {
            get
            {
                return Get<Button>("Shortcut");
            }
        }

        bool hotkey
        {
            get
            {
                return app.Settings.ShortcutAvailable;
            }
        }

        bool capturing
        {
            get
            {
                return app.Settings.IsRecording;
            }
        }

        int value
        {
            set
            {
                target.Text = value.ToString();
            }
        }

        public UiSmokeTests(MainWindowController controller, string folder)
        {
            app = controller;
            smoke = folder;
        }

        T Get<T>(string name)
            where T : class
        {
            return app.Find<T>(name);
        }

        bool ParseTarget()
        {
            return app.TryApplyTarget();
        }

        Task Configure()
        {
            return app.ConfigureAsync();
        }

        Task Send(string command)
        {
            return app.SendAsync(command);
        }

        Task ChangeManual(bool enabled)
        {
            return app.ChangeManualAsync(enabled);
        }

        Task Exit()
        {
            return app.ExitAsync();
        }

        Task RefreshGpu()
        {
            return app.RefreshGpuAsync();
        }

        void Show()
        {
            app.Show();
        }

        void Hide()
        {
            app.Hide();
        }

        void ShowSettings(bool visible)
        {
            if (visible)
                app.Settings.Show();
            else
                app.Settings.Hide();
        }

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
        static int ChromeHit(Window view, Point point)
        {
            Point screen = view.PointToScreen(point);
            int packed = ((int)screen.X & 65535) | (((int)screen.Y & 65535) << 16);
            return SendMessage(new WindowInteropHelper(view).Handle, 0x84, IntPtr.Zero, new IntPtr(packed)).ToInt32();
        }

        void Image(string name)
        {
            var editor = app.CurveEditor;
            Window view = editor != null && editor.Window.IsVisible ? editor.Window : settingsWindow != null && settingsWindow.IsVisible ? settingsWindow : window;
            view.UpdateLayout();
            var image = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(System.IO.Path.Combine(smoke, name)))
                encoder.Save(file);
        }

        public async Task<bool> RunAsync()
        {
            Directory.CreateDirectory(smoke);
            try
            {
                if (!state.Connected || !state.Demo)
                    throw new Exception("Demo startup failed.");
                if (!hotkey)
                    throw new Exception("Shortcut not registered.");
                if (!window.IsVisible)
                    Show();
                if (settingsWindow != null)
                    throw new Exception("Settings was built before it was opened.");
                if (window.WindowStyle != WindowStyle.None || WindowChrome.GetWindowChrome(window) == null)
                    throw new Exception("Integrated window chrome missing.");
                if (ChromeHit(window, new Point(190, 20)) != 2 || ChromeHit(window, new Point(2, 200)) != 10)
                    throw new Exception("Caption drag/edge resize hit testing failed.");
                var captionButton = Get<Button>("WindowClose");
                Point closePoint = captionButton.TranslatePoint(new Point(captionButton.ActualWidth / 2, captionButton.ActualHeight / 2), window);
                if (ChromeHit(window, closePoint) != 1)
                    throw new Exception("Header close button is not interactive.");
                if (window.FindName("ToTray") != null || window.FindName("Exit") != null)
                    throw new Exception("Duplicate dashboard footer controls.");
                if (window.FindName("Maximize") != null || window.WindowState != WindowState.Normal)
                    throw new Exception("Unexpected window state or maximize button.");
                value = 83;
                target.Text = "83";
                ParseTarget();
                await Configure();
                int onTransitions = 0, offTransitions = 0;
                RoutedEventHandler onWatch = delegate
                {
                    onTransitions++;
                }, offWatch = delegate
                {
                    offTransitions++;
                };
                toggle.Checked += onWatch;
                toggle.Unchecked += offWatch;
                toggle.IsChecked = true;
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                for (int wait = 0; wait < 30 && !state.Manual; wait++)
                    await Task.Delay(100);
                await Send("POLL");
                if (!state.Manual || state.Duties.Any(x => x != 83) || Get<System.Windows.Controls.Primitives.UniformGrid>("FanCards").Children.Count != 2)
                    throw new Exception("Both fan controls/readings failed.");
                toggle.Checked -= onWatch;
                toggle.Unchecked -= offWatch;
                if (onTransitions != 1 || offTransitions != 0)
                    throw new Exception("Toggle bounced back off while enabling.");
                int enabledChanges = 0, checkedChanges = 0;
                DependencyPropertyChangedEventHandler enabledWatch = delegate
                {
                    enabledChanges++;
                };
                RoutedEventHandler checkedWatch = delegate
                {
                    checkedChanges++;
                };
                toggle.IsEnabledChanged += enabledWatch;
                toggle.Checked += checkedWatch;
                toggle.Unchecked += checkedWatch;
                for (int pass = 0; pass < 12; pass++)
                    await Send("POLL");
                toggle.IsEnabledChanged -= enabledWatch;
                toggle.Checked -= checkedWatch;
                toggle.Unchecked -= checkedWatch;
                if (enabledChanges != 0 || checkedChanges != 0 || !toggle.IsEnabled)
                    throw new Exception("Polling changed the manual switch (flicker regression).");
                await RefreshGpu();
                if (Get<TextBlock>("Gpu").Text != "GPU  54 °C")
                    throw new Exception("GPU live display failed.");
                await Task.Delay(300);
                window.UpdateLayout();
                if (Get<ScrollViewer>("MainScroll").ScrollableHeight > 1)
                    throw new Exception("Default compact window requires scrolling by " + Get<ScrollViewer>("MainScroll").ScrollableHeight + " DIP at height " + window.ActualHeight);
                Image("FanPilot-percentage.png");
                Get<Button>("OpenSettings").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await Task.Delay(250);
                if (!settingsWindow.IsVisible || settingsWindow.Owner != null || !settingsWindow.ShowInTaskbar || settingsWindow.WindowStyle != WindowStyle.None || WindowChrome.GetWindowChrome(settingsWindow) == null || !Get<ScrollViewer>("MainScroll").IsEnabled)
                    throw new Exception("Desktop settings window did not open.");
                foreach (string name in new[]
                {
                    "Startup",
                    "CloseToTray",
                    "Animations"
                }

                )
                    if (!Get<FrameworkElement>(name).IsVisible)
                        throw new Exception("Missing general setting: " + name);
                Image("FanPilot-settings.png");
                Get<TabControl>("SettingsTabs").SelectedIndex = 1;
                settingsWindow.UpdateLayout();
                foreach (string name in new[]
                {
                    "Shortcut",
                    "ShortcutEnabled"
                }

                )
                    if (!Get<FrameworkElement>(name).IsVisible)
                        throw new Exception("Missing keyboard setting: " + name);
                Image("FanPilot-settings-keyboard.png");
                Get<Button>("SettingsDone").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (settingsWindow.IsVisible)
                    throw new Exception("Settings window did not close.");
                ShowSettings(true);
                Get<Button>("SettingsClose").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (settingsWindow.IsVisible)
                    throw new Exception("Settings header close failed.");
                ShowSettings(true);
                settingsWindow.Close();
                if (settingsWindow.IsVisible)
                    throw new Exception("Settings close failed.");
                ShowSettings(true);
                ShowSettings(false);
                shortcut.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                KeyboardInputDriver.TestKey(119, true);
                KeyboardInputDriver.TestKey(120, true);
                KeyboardInputDriver.TestKey(120, false);
                KeyboardInputDriver.TestKey(119, false);
                await Task.Delay(250);
                if (capturing || !prefs.GetShortcutKeys().SequenceEqual(new int[] { 119, 120 }))
                    throw new Exception("Two-key recording did not save.");
                Hide();
                KeyboardInputDriver.TestKey(119, true);
                KeyboardInputDriver.TestKey(120, true);
                KeyboardInputDriver.TestKey(120, true);
                KeyboardInputDriver.TestKey(120, false);
                KeyboardInputDriver.TestKey(119, false);
                await Task.Delay(400);
                if (state.Manual || !state.ReleaseConfirmed)
                    throw new Exception("Two-key hidden shortcut failed or repeated.");
                Show();
                shortcut.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                foreach (int k in new int[]
                {
                    115,
                    116,
                    117
                }

                )
                    KeyboardInputDriver.TestKey(k, true);
                foreach (int k in new int[]
                {
                    117,
                    116,
                    115
                }

                )
                    KeyboardInputDriver.TestKey(k, false);
                await Task.Delay(250);
                if (!prefs.GetShortcutKeys().SequenceEqual(new int[] { 115, 116, 117 }))
                    throw new Exception("Three-key recording failed.");
                Hide();
                foreach (int k in new int[]
                {
                    115,
                    116,
                    117
                }

                )
                    KeyboardInputDriver.TestKey(k, true);
                foreach (int k in new int[]
                {
                    117,
                    116,
                    115
                }

                )
                    KeyboardInputDriver.TestKey(k, false);
                await Task.Delay(400);
                if (!state.Manual)
                    throw new Exception("Three-key hidden shortcut failed.");
                foreach (int k in new int[]
                {
                    115,
                    116,
                    117
                }

                )
                    KeyboardInputDriver.TestKey(k, true);
                foreach (int k in new int[]
                {
                    117,
                    116,
                    115
                }

                )
                    KeyboardInputDriver.TestKey(k, false);
                await Task.Delay(400);
                if (state.Manual || !state.ReleaseConfirmed)
                    throw new Exception("Three-key off shortcut failed.");
                Show();
                shortcut.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                KeyboardInputDriver.TestKey(118, true);
                KeyboardInputDriver.TestKey(118, false);
                await Task.Delay(250);
                if (!prefs.GetShortcutKeys().SequenceEqual(new int[] { 118 }))
                    throw new Exception("Single-key recording failed.");
                Hide();
                KeyboardInputDriver.TestKey(118, true);
                KeyboardInputDriver.TestKey(118, true);
                KeyboardInputDriver.TestKey(118, true);
                KeyboardInputDriver.TestKey(118, false);
                await Task.Delay(400);
                if (!state.Manual)
                    throw new Exception("Single-key hidden shortcut failed or repeated.");
                KeyboardInputDriver.TestKey(118, true);
                KeyboardInputDriver.TestKey(118, false);
                await Task.Delay(400);
                if (state.Manual || !state.ReleaseConfirmed)
                    throw new Exception("Single-key off shortcut failed.");
                Show();
                value = 87;
                target.Text = "87";
                ParseTarget();
                await Configure();
                await ChangeManual(true);
                await Send("POLL");
                if (state.Duties.Any(x => x != 87) || window.FindName("RpmMode") != null || window.FindName("DetectRange") != null)
                    throw new Exception("Direct PWM control failed.");
                await Task.Delay(300);
                Image("FanPilot-pwm.png");
                target.Text = "9000";
                if (ParseTarget())
                    throw new Exception("Unreachable RPM target accepted by UI.");
                await Send("DISABLE");
                if (!state.ReleaseConfirmed)
                    throw new Exception("Reset incomplete.");
                Get<Button>("Minimize").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (window.IsVisible)
                    throw new Exception("Minimize-to-tray failed.");
                Show();
                bool oldClose = prefs.CloseToTray;
                prefs.CloseToTray = true;
                Get<Button>("WindowClose").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (window.IsVisible)
                    throw new Exception("Header close-to-tray failed.");
                prefs.CloseToTray = oldClose;
                Show();
                window.Width = 540;
                window.Height = window.MinHeight;
                window.UpdateLayout();
                var scroll = Get<ScrollViewer>("MainScroll");
                var bar = (System.Windows.Controls.Primitives.ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
                if (scroll.ScrollableHeight <= 0 || bar == null || bar.ActualWidth > 10 || ((SolidColorBrush)bar.Background).Color != Color.FromRgb(9, 10, 12))
                    throw new Exception("Compact view did not use the slim dark scrollbar: range=" + scroll.ScrollableHeight + ", width=" + (bar == null ? -1 : bar.ActualWidth) + ", color=" + (bar == null ? "missing" : bar.Background.ToString()));
                scroll.ScrollToBottom();
                window.UpdateLayout();
                if (scroll.VerticalOffset <= 0)
                    throw new Exception("Themed scrollbar did not scroll.");
                Image("FanPilot-small.png");
                scroll.ScrollToTop();
                var menu = app.Tray.ContextMenu;
                menu.Show(new System.Drawing.Point((int)window.Left + 40, (int)window.Top + 80));
                await Task.Delay(120);
                if (!(menu.Renderer is TrayMenuRenderer) || menu.BackColor != TrayMenuRenderer.Surface)
                    throw new Exception("Tray menu did not use the dark renderer.");
                var trayActions = menu.Items.OfType<Forms.ToolStripMenuItem>().ToArray();
                if (trayActions.Length != 4 || trayActions.Any(item => item.Width != trayActions[0].Width || item.Height != trayActions[0].Height || item.Padding.Left != item.Padding.Right))
                    throw new Exception("Tray actions did not use matching dimensions and insets.");
                trayActions[1].Select();
                using (var bitmap = new System.Drawing.Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(System.IO.Path.Combine(smoke, "FanPilot-tray-menu.png"));
                }

                menu.Close();
                ShowSettings(true);
                settingsWindow.Width = 500;
                settingsWindow.Height = 380;
                Get<TabControl>("SettingsTabs").SelectedIndex = 0;
                await Task.Delay(150);
                foreach (string name in new[]
                {
                    "Startup",
                    "CloseToTray",
                    "Animations"
                }

                )
                    CheckSwitchAlignment(Get<CheckBox>(name));
                CheckCaptionAlignment(window, "OpenSettings", "Minimize", "WindowClose");
                CheckCaptionAlignment(settingsWindow, "SettingsMinimize", "SettingsClose");
                Image("FanPilot-settings-small.png");
                ShowSettings(false);
                await TestCurves();
                await TestModeProfiles();
                File.WriteAllText(System.IO.Path.Combine(smoke, "display-scale.txt"), VisualTreeHelper.GetDpi(window).DpiScaleX.ToString(System.Globalization.CultureInfo.InvariantCulture));
                TestOfflineDraft();
                await TestGaugeMotion();
                TestIndependentWindows();
                ShowSettings(true);
                var choice = Get<ComboBox>("ThemeChoice");
                choice.SelectedIndex = 1;
                if (Preferences.Load().ThemeIndex != 1 || ((SolidColorBrush)window.Background).Color != Color.FromRgb(8, 14, 23))
                    throw new Exception("Graphite theme failed to apply or persist.");
                ShowSettings(false);
                Image("Dial-midnight.png");
                choice.SelectedIndex = 3;
                if (Preferences.Load().ThemeIndex != 3 || ((SolidColorBrush)window.Background).Color != Color.FromRgb(16, 12, 21))
                    throw new Exception("Amethyst did not apply or persist.");
                Image("Dial-amethyst.png");
                ShowSettings(true);
                choice.SelectedIndex = 2;
                app.CurveEditor.Show();
                window.UpdateLayout();
                settingsWindow.UpdateLayout();
                if (((SolidColorBrush)window.Background).Color != Color.FromRgb(12, 19, 24) || ((SolidColorBrush)settingsWindow.Background).Color != Color.FromRgb(12, 19, 24) || ((SolidColorBrush)app.CurveEditor.Window.Background).Color != Color.FromRgb(12, 19, 24) || app.Tray.ContextMenu.BackColor.ToArgb() != System.Drawing.Color.FromArgb(19, 30, 38).ToArgb() || Preferences.Load().ThemeIndex != 2)
                    throw new Exception("Fjord did not propagate across all surfaces.");
                app.CurveEditor.Hide();
                settingsWindow.Height = 445;
                Image("Studio-theme-settings.png");
                ShowSettings(false);
                Image("Dial-fjord.png");
                choice.SelectedIndex = 4;
                Image("Dial-sandstone.png");
                choice.SelectedIndex = 5;
                Image("Dial-bordeaux.png");
                choice.SelectedIndex = 6;
                Image("Dial-graphite.png");
                for (int index = 0; index < choice.Items.Count; index++)
                {
                    choice.SelectedIndex = index;
                    var save = app.CurveEditor.Find<Button>("SaveCurve");
                    var backgroundColor = ((SolidColorBrush)save.Background).Color;
                    var foregroundColor = ((SolidColorBrush)save.Foreground).Color;
                    if (Contrast(backgroundColor, foregroundColor) < 4.5)
                        throw new Exception("Primary curve action contrast is too low for theme " + index);
                }

                choice.SelectedIndex = 0;
                await TestDisconnectRecovery();
                File.WriteAllText(System.IO.Path.Combine(smoke, "result.txt"), "PASS: integrated window headers; native caption/resize/button hit tests; minimize and close-to-tray; Settings close/reopen; compact General/Keyboard tabs; GPU display; 12 polls with zero toggle flicker; direct PWM; separate fans; native 1–3-key shortcuts; curve editor; exact point edit; shared fan control; curve apply/off; calibration start/cancel; calibrated RPM axis; independent mode profiles; preview isolation; linked mode changes; manual cooling selection; source-loss reset; source recovery remains off; mode-change calibration cancellation; independent secondary window minimize/restore; reset.\r\n");
            }
            catch (Exception ex)
            {
                File.WriteAllText(System.IO.Path.Combine(smoke, "result.txt"), "FAIL: " + ex);
            }

            await Exit();
            return File.ReadAllText(System.IO.Path.Combine(smoke, "result.txt")).StartsWith("PASS:");
        }

        static double Contrast(Color first, Color second)
        {
            double a = .2126 * LinearChannel(first.R) + .7152 * LinearChannel(first.G) + .0722 * LinearChannel(first.B);
            double b = .2126 * LinearChannel(second.R) + .7152 * LinearChannel(second.G) + .0722 * LinearChannel(second.B);
            return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
        }

        static double LinearChannel(byte channel)
        {
            double value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }

        static void CheckSwitchAlignment(CheckBox control)
        {
            bool? previous = control.IsChecked;
            try
            {
                foreach (bool isChecked in new[]
                {
                    false,
                    true
                }

                )
                {
                    control.IsChecked = isChecked;
                    control.UpdateLayout();
                    var track = (FrameworkElement)control.Template.FindName("Track", control);
                    var thumb = (FrameworkElement)control.Template.FindName("ThumbDot", control);
                    var position = thumb.TranslatePoint(new Point(), track);
                    double inset = isChecked ? track.ActualWidth - position.X - thumb.ActualWidth : position.X;
                    double verticalDifference = Math.Abs(position.Y * 2 + thumb.ActualHeight - track.ActualHeight);
                    var dpi = VisualTreeHelper.GetDpi(control);
                    if (Math.Abs(inset - 4) * dpi.DpiScaleX > 1.01 || verticalDifference * dpi.DpiScaleY > 1.01)
                        throw new Exception("Switch thumb is misaligned: " + control.Name);
                }
            }
            finally
            {
                control.IsChecked = previous;
            }
        }

        static void CheckCaptionAlignment(Window view, params string[] names)
        {
            view.UpdateLayout();
            foreach (string name in names)
            {
                var button = (Button)view.FindName(name);
                var icon = (FrameworkElement)button.Content;
                var center = icon.TranslatePoint(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2), button);
                var dpi = VisualTreeHelper.GetDpi(button);
                if (Math.Abs(center.X - button.ActualWidth / 2) * dpi.DpiScaleX > 1.01 || Math.Abs(center.Y - button.ActualHeight / 2) * dpi.DpiScaleY > 1.01)
                    throw new Exception("Caption icon is off-center: " + name);
            }
        }

        static void Click(System.Windows.Controls.Primitives.ButtonBase button)
        {
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }

        async Task TestDisconnectRecovery()
        {
            Get<ComboBox>("ControlMode").SelectedIndex = 0;
            await app.ChangeManualAsync(true);
            string marker = Preferences.FilePath + ".demo-reset-pending";
            if (!state.Manual || !File.Exists(marker))
                throw new Exception("Loss of confirmation would not survive restart.");
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var session = (FanSession)typeof(MainWindowController).GetField("session", fields).GetValue(app);
            var client = (BridgeClient)typeof(FanSession).GetField("client", fields).GetValue(session);
            // The helper restores the simulated fans but its acknowledgment is lost to the UI.
            client.Exchange("QUIT");
            await Send("POLL");
            await Exit();
            if (!window.IsVisible || state.Connected || state.ReleaseConfirmed || toggle.IsEnabled || !Get<Button>("ExitUnconfirmed").IsVisible || !File.Exists(marker))
                throw new Exception("Exit closed or cleared firmware recovery after losing the helper.");
            await app.RetryAsync();
            if (!state.Connected || !state.ReleaseConfirmed || state.Manual || File.Exists(marker))
                throw new Exception("An explicit reset was not confirmed after reconnecting.");
        }

        async Task Until(Func<bool> condition, string failure)
        {
            for (int i = 0; i < 40 && !condition(); i++)
                await Task.Delay(50);
            if (!condition())
                throw new Exception(failure);
        }

        static CurveSettings ConstantProfile(int duty)
        {
            return new CurveSettings
            {
                MappingConfirmed = true,
                FanShift = 100,
                HysteresisUp = 0,
                HysteresisDown = 0,
                Cpu = new[]
                {
                    new CurvePoint(20, duty),
                    new CurvePoint(100, duty)
                },
                Gpu = new[]
                {
                    new CurvePoint(20, duty),
                    new CurvePoint(100, duty)
                }
            };
        }

        async Task WaitForDuty(int duty)
        {
            for (int i = 0; i < 80; i++)
            {
                await Send("POLL");
                if (state.Mode == "Curve" && state.Duties.All(x => x == duty))
                    return;
                await Task.Delay(150);
            }

            throw new Exception("Mode profile did not reach duty " + duty + ": " + state.Error);
        }

        async Task TestModeProfiles()
        {
            var source = (DemoModeSource)app.ModeSource;
            var editor = app.CurveEditor;
            editor.Show();
            var personal = CurvePreset.ReadPersonalPresets();
            foreach (var referencePreset in personal)
                if (!editor.Find<ComboBox>("PresetList").Items.OfType<CurvePreset>().Any(preset => preset.Name == referencePreset.Name))
                    throw new Exception("Personal preset missing from library: " + referencePreset.Name);
            int remainingPersonal = personal.Length;
            if (personal.Length > 0)
            {
                var library = editor.Find<ComboBox>("PresetList");
                library.SelectedItem = library.Items.OfType<CurvePreset>().First(preset => preset.Name == personal[0].Name);
                if (!editor.Find<Button>("UpdatePreset").IsEnabled || !editor.Find<Button>("RenamePreset").IsEnabled || !editor.Find<Button>("DeletePreset").IsEnabled)
                    throw new Exception("Bundled preset actions are unavailable.");
                Click(editor.Find<Button>("RenamePreset"));
                const string renamedPersonal = "Renamed bundled preset";
                editor.Find<TextBox>("PresetName").Text = renamedPersonal;
                Click(editor.Find<Button>("SavePreset"));
                if (!PresetLibrary.List(Preferences.Load(), personal).Any(preset => preset.Name == renamedPersonal))
                    throw new Exception("Bundled preset rename did not survive reload.");
                Click(editor.Find<Button>("DeletePreset"));
                Click(editor.Find<Button>("SavePreset"));
                var reloaded = PresetLibrary.List(Preferences.Load(), personal).ToArray();
                remainingPersonal--;
                if (reloaded.Length != remainingPersonal || reloaded.Any(preset => preset.Name == personal[0].Name))
                    throw new Exception("Deleted bundled preset came back after reload.");
            }

            foreach (PerformanceMode mode in new[]
            {
                PerformanceMode.Silent,
                PerformanceMode.Balanced,
                PerformanceMode.Turbo
            }

            )
            {
                editor.SelectProfile(mode);
                CurveSettings profile = ConstantProfile(mode == PerformanceMode.Silent ? 60 : mode == PerformanceMode.Balanced ? 70 : 80);
                editor.Draft.Cpu = profile.Cpu;
                editor.Draft.Gpu = profile.Gpu;
                editor.Draft.HysteresisUp = editor.Draft.HysteresisDown = 0;
                editor.Draft.MappingConfirmed = true;
                editor.SaveDraft();
            }

            // A future-mode save must not enable control or change the active mode.
            editor.SelectProfile(PerformanceMode.Silent);
            editor.SelectProfile(PerformanceMode.Turbo);
            if (editor.Find<Button>("SaveCurve").Content.ToString() != "Save for Turbo")
                throw new Exception("Future profile is not marked as a preview.");
            Click(editor.Find<Button>("SaveCurve"));
            await Task.Delay(100);
            if (state.Manual || app.ActiveProfile != PerformanceMode.Balanced || source.Mode != PerformanceMode.Balanced)
                throw new Exception("Preview changed current mode or fan ownership.");
            Image("FanPilot-profile-preview.png");
            editor.SelectProfile(PerformanceMode.Balanced);
            Click(editor.Find<Button>("SaveCurve"));
            await Until(delegate
            {
                return state.Mode == "Curve";
            }, "Balanced profile did not start");
            if (!state.Duties.All(x => x == 70))
                throw new Exception("Profile startup forced full speed.");
            editor.SelectProfile(PerformanceMode.Turbo);
            Click(editor.Find<Button>("SaveCurve"));
            await Task.Delay(100);
            await Send("POLL");
            if (!state.Duties.All(x => x == 70))
                throw new Exception("Saving Turbo changed the active Balanced curve.");
            editor.Draft.Cpu = editor.Draft.Gpu = ConstantProfile(95).Cpu;
            editor.SelectProfile(PerformanceMode.Balanced);
            editor.SelectProfile(PerformanceMode.Turbo);
            if (editor.Draft.Cpu[0].Duty != 95 || prefs.Profiles.Get(PerformanceMode.Turbo).Cpu[0].Duty != 80)
                throw new Exception("Changing tabs committed a draft or lost it.");
            if (!editor.Find<Grid>("PresetPanel").IsVisible || !editor.Find<Button>("NewPreset").IsVisible)
                throw new Exception("Preset controls were hidden.");
            Click(editor.Find<Button>("NewPreset"));
            if (!editor.Find<TextBox>("PresetName").IsVisible)
                throw new Exception("Preset naming did not open.");
            Click(editor.Find<Button>("SavePreset"));
            if (!editor.Find<TextBlock>("PresetError").IsVisible || (Preferences.Load().CurvePresets ?? new List<CurvePreset>()).Count != remainingPersonal)
                throw new Exception("Blank preset name did not show an inline error.");
            Image("FanPilot-preset-save.png");
            editor.Find<TextBox>("PresetName").Text = "Test quiet";
            Click(editor.Find<Button>("SavePreset"));
            editor.Draft.Cpu = editor.Draft.Gpu = ConstantProfile(65).Cpu;
            var presetList = editor.Find<ComboBox>("PresetList");
            presetList.SelectedIndex = 0;
            presetList.SelectedItem = presetList.Items.OfType<CurvePreset>().Single(preset => preset.Name == "Test quiet");
            if (editor.Draft.Cpu[0].Duty != 95 || Preferences.Load().CurvePresets.Count != remainingPersonal + 1 || !editor.Find<Grid>("PresetPanel").IsVisible)
                throw new Exception("Named preset did not save and load independently.");
            editor.Find<Slider>("PreviewCpu").Value = 60;
            editor.Find<Slider>("PreviewGpu").Value = 85;
            int[] expected = CurveMath.Targets(editor.Draft, 60, 85, 2);
            for (int i = 0; i < 2; i++)
                if (!editor.Find<TextBlock>("PreviewFan" + (i + 1)).Text.Contains(expected[i] + "% PWM"))
                    throw new Exception("Draft preview differs from worker calculation.");
            if (!editor.Find<Button>("UpdatePreset").IsEnabled || !editor.Find<Button>("RenamePreset").IsEnabled || !editor.Find<Button>("DeletePreset").IsEnabled)
                throw new Exception("Actions are unavailable for a saved preset.");
            Image("FanPilot-preset-actions.png");
            Click(editor.Find<Button>("RenamePreset"));
            editor.Find<TextBox>("PresetName").Text = "Renamed";
            Click(editor.Find<Button>("SavePreset"));
            if (!Preferences.Load().CurvePresets.Any(preset => preset.Name == "Renamed"))
                throw new Exception("Rename did not persist.");
            editor.Draft.Cpu = editor.Draft.Gpu = ConstantProfile(85).Cpu;
            Click(editor.Find<Button>("UpdatePreset"));
            Click(editor.Find<Button>("SavePreset"));
            if (Preferences.Load().CurvePresets.Single(preset => preset.Name == "Renamed").Curve.Cpu[0].Duty != 85)
                throw new Exception("Preset update did not persist.");
            Click(editor.Find<Button>("DeletePreset"));
            Click(editor.Find<Button>("CancelPreset"));
            if (Preferences.Load().CurvePresets.Count != remainingPersonal + 1)
                throw new Exception("Cancelled deletion changed the library.");
            if (editor.Find<Border>("PointPanel").IsVisible)
                Click(editor.Find<Button>("PointToggle"));
            if (editor.Find<Border>("AdvancedPanel").IsVisible)
            {
                editor.Find<ToggleButton>("AdvancedToggle").IsChecked = false;
                await Until(() => !editor.Find<Grid>("AdvancedDrawer").IsVisible, "Advanced drawer did not close.");
            }

            editor.Window.Width = 1000;
            editor.Window.Height = 700;
            editor.Find<Slider>("PreviewGpu").Value = 75;
            Click(editor.Find<Button>("CurveDefaults"));
            Image("Studio-draft.png");
            Click(editor.Find<Button>("DeletePreset"));
            Click(editor.Find<Button>("SavePreset"));
            if (Preferences.Load().CurvePresets.Count != remainingPersonal)
                throw new Exception("Confirmed deletion did not persist.");
            await Send("POLL");
            if (!state.Manual || !state.Duties.All(x => x == 70))
                throw new Exception("Draft preview or library changed running demand.");
            source.Mode = PerformanceMode.Turbo;
            await app.RefreshModeAsync();
            await app.RefreshModeAsync();
            await WaitForDuty(80);
            if (app.ActiveProfile != PerformanceMode.Turbo || editor.Find<Button>("SaveCurve").Content.ToString() != "Apply changes")
                throw new Exception("Turbo did not become the active cooling profile.");
            Image("FanPilot-profile-turbo.png");
            editor.Hide();
            Image("FanPilot-dashboard-profiles.png");
            editor.Show();
            source.Mode = PerformanceMode.Silent;
            await app.RefreshModeAsync();
            await app.RefreshModeAsync();
            await WaitForDuty(60);
            if (editor.EditingMode != PerformanceMode.Silent)
                throw new Exception("Visible editor missed confirmed mode change.");
            editor.SelectProfile(PerformanceMode.Turbo);
            var stop = editor.Find<Button>("StopCurve");
            if (!stop.IsEnabled || editor.Find<TextBlock>("RunningStatus").Text != "Silent curve running")
                throw new Exception("Preview tab does not show running curve or allow stop.");
            Click(stop);
            await Until(delegate
            {
                return !state.Manual && state.ReleaseConfirmed;
            }, "Cannot stop from a preview tab");
            // Present a failed reset snapshot without sending a command to the worker.
            state.ReleaseConfirmed = false;
            try
            {
                app.Show();
                if (Get<TextBlock>("ControlStatus").Text != "Firmware reset unconfirmed" || !editor.Find<TextBlock>("CurveStatus").Text.Contains("reset unconfirmed"))
                    throw new Exception("Failed reset was presented as confirmed firmware control.");
            }
            finally
            {
                state.ReleaseConfirmed = true;
                app.Show();
            }

            source.Mode = PerformanceMode.Balanced;
            await app.RefreshModeAsync();
            if (state.Manual)
                throw new Exception("Mode change re-enabled stopped fan control.");
            await ChangeManual(true);
            await WaitForDuty(70);
            source.Mode = null;
            await app.RefreshModeAsync();
            await app.RefreshModeAsync();
            await app.RefreshModeAsync();
            if (state.Manual || !state.ReleaseConfirmed)
                throw new Exception("Lost mode source retained an automatic curve.");
            source.Mode = PerformanceMode.Turbo;
            await app.RefreshModeAsync();
            if (state.Manual)
                throw new Exception("Source recovery silently restarted fans.");
            await ChangeManual(true);
            await WaitForDuty(80);
            source.Fail = true;
            await app.RefreshModeAsync();
            if (state.Manual || !state.ReleaseConfirmed)
                throw new Exception("Unexpected mode-source failure retained control.");
            source.Fail = false;
            var profileSelector = Get<ComboBox>("ActiveProfile");
            if (!profileSelector.IsEnabled || profileSelector.SelectedIndex != 3)
                throw new Exception("The profile selector was locked while following G-Helper.");
            source.Mode = PerformanceMode.Balanced;
            await app.RefreshModeAsync();
            profileSelector.SelectedIndex = 2;
            await Task.Delay(100);
            if (state.Manual || app.ActiveProfile != PerformanceMode.Turbo || source.Mode != PerformanceMode.Balanced)
                throw new Exception("Selecting Turbo while off changed ownership or laptop mode.");
            profileSelector.SelectedIndex = 0;
            await Task.Delay(100);
            if (app.ActiveProfile != PerformanceMode.Silent || source.Mode != PerformanceMode.Balanced || prefs.FollowGHelper != false)
                throw new Exception("Manual profile changed the laptop mode.");
            await ChangeManual(true);
            await WaitForDuty(60);
            profileSelector.SelectedIndex = 2;
            await WaitForDuty(80);
            if (!Get<TextBlock>("ModeLinkState").Text.Contains("Balanced") || !Get<TextBlock>("CoolingLabel").Text.Contains("TURBO"))
                throw new Exception("Dashboard conflated cooling and laptop performance.");
            editor.Hide();
            window.Width = 650;
            window.Height = 475;
            window.UpdateLayout();
            var stopFill = (Border)toggle.Template.FindName("Fill", toggle);
            var editorStop = editor.Find<Button>("StopCurve");
            var stopColor = ((SolidColorBrush)stopFill.Background).Color;
            if (stopColor != Color.FromRgb(152, 43, 54) || stopColor != ((SolidColorBrush)editorStop.Background).Color)
                throw new Exception("Dashboard Stop curve did not match the red editor action.");
            Image("Studio-dashboard.png");
            editor.Show();
            source.Mode = null;
            await app.RefreshModeAsync();
            await app.RefreshModeAsync();
            await app.RefreshModeAsync();
            if (!state.Manual)
                throw new Exception("Manual profile unnecessarily depends on G-Helper.");
            await ChangeManual(false);
            source.Mode = PerformanceMode.Balanced;
            profileSelector.SelectedIndex = 3;
            await Task.Delay(100);
            await app.RefreshModeAsync();
            if (app.ActiveProfile != PerformanceMode.Balanced || prefs.FollowGHelper != true)
                throw new Exception("The profile selector did not resume following G-Helper.");
            await Send("CALIBRATE");
            source.Mode = PerformanceMode.Turbo;
            await app.RefreshModeAsync();
            await app.RefreshModeAsync();
            if (state.Calibrating || state.Manual || !state.ReleaseConfirmed)
                throw new Exception("Mode change did not cancel calibration safely.");
            source.Mode = PerformanceMode.Balanced;
            await app.RefreshModeAsync();
            editor.SelectProfile(PerformanceMode.Balanced);
            Image("FanPilot-mode-profiles.png");
            editor.Hide();
        }

        void TestOfflineDraft()
        {
            string previousPath = Environment.GetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS");
            string draftPath = System.IO.Path.Combine(smoke, "offline-draft.json");
            Environment.SetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS", draftPath);
            try
            {
                var isolated = new Preferences
                {
                    Calibration = prefs.Calibration.Clone()
                };
                int commands = 0;
                using (var editor = new CurveEditorController(window, isolated, delegate
                {
                    commands++;
                    return Task.FromResult(true);
                }, delegate
                {
                    commands++;
                    return Task.FromResult(true);
                }))
                {
                    editor.SelectProfile(PerformanceMode.Turbo);
                    editor.Draft.SharedControl = false;
                    editor.Draft.FanShift = 0;
                    editor.Draft.Fan1Source = FanTemperatureSource.Cpu;
                    editor.Draft.Fan2Source = FanTemperatureSource.Gpu;
                    editor.Find<Slider>("PreviewCpu").Value = 60;
                    editor.Find<Slider>("PreviewGpu").Value = 85;
                    int[] duties = CurveMath.Targets(editor.Draft, 60, 85, 2);
                    if (duties[0] == duties[1] || !editor.Find<TextBlock>("PreviewFan1").Text.Contains(duties[0] + "% PWM") || !editor.Find<TextBlock>("PreviewFan2").Text.Contains(duties[1] + "% PWM"))
                        throw new Exception("Independent preview mixed sensor inputs.");
                    var save = editor.Find<Button>("SaveCurve");
                    if (!save.IsEnabled || save.Content.ToString() != "Save for Turbo")
                        throw new Exception("Offline future-profile save was unavailable.");
                    Click(save);
                    if (!File.Exists(draftPath) || commands != 0)
                        throw new Exception("Offline preview/save sent a controller request or failed to save.");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("FANPILOT_DIAL_TEST_SETTINGS", previousPath);
            }
        }

        static async Task TestGaugeMotion()
        {
            var tile = new FanTile(1);
            var panel = (Grid)tile.Root.Child;
            var dial = panel.Children[0];
            var number = ((StackPanel)panel.Children[1]).Children.OfType<NumberLabel>().Single();
            var display = (DependencyProperty)dial.GetType().GetField("DisplayProperty", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            tile.Update(2000, "Automatic", false);
            tile.Update(4000, "Automatic", true);
            bool intermediate = false;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(30);
                double value = (double)dial.GetValue(display);
                intermediate |= value > 2000 && value < 4000;
            }

            if (!intermediate || (double)dial.GetValue(display) != 4000)
                throw new Exception("Gauge sweep did not interpolate and settle.");
            tile.Update(2500, "Automatic", true);
            tile.Motion(false);
            if ((double)dial.GetValue(display) != 2500 || number.HasAnimatedProperties)
                throw new Exception("Hidden gauge retained its animation clock.");
            tile.Offline();
            if (number.Text != "—")
                throw new Exception("Offline gauge retained a speed reading.");
        }

        void TestIndependentWindows()
        {
            var editor = app.CurveEditor;
            editor.Show();
            ShowSettings(true);
            Get<Button>("Minimize").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (window.IsVisible || !editor.Window.IsVisible || !settingsWindow.IsVisible || editor.Window.WindowState != WindowState.Normal || settingsWindow.WindowState != WindowState.Normal)
                throw new Exception("Dashboard minimize hid independent windows.");
            Click(Get<Button>("SettingsMinimize"));
            if (settingsWindow.WindowState != WindowState.Minimized || editor.Window.WindowState != WindowState.Normal)
                throw new Exception("Settings minimize was not independent.");
            ShowSettings(true);
            if (settingsWindow.FindName("SettingsMaximize") != null || settingsWindow.WindowState != WindowState.Normal)
                throw new Exception("Unexpected window state or maximize button.");
            Click(Get<Button>("CurvesMinimize"));
            if (editor.Window.WindowState != WindowState.Minimized || settingsWindow.WindowState != WindowState.Normal)
                throw new Exception("Curve minimize was not independent.");
            editor.Show();
            if (editor.Window.FindName("CurvesMaximize") != null || editor.Window.WindowState != WindowState.Normal)
                throw new Exception("Unexpected window state or maximize button.");
            Show();
            prefs.CloseToTray = true;
            window.Close();
            if (window.IsVisible || !editor.Window.IsVisible || !settingsWindow.IsVisible)
                throw new Exception("Dashboard close-to-tray hid secondary windows.");
            editor.Hide();
            ShowSettings(false);
            Show();
        }

        async Task TestCurves()
        {
            if (app.CurveEditor != null)
                throw new Exception("Curve editor was built before it was opened.");
            window.Width = 620;
            window.Height = 640;
            window.UpdateLayout();
            Click(Get<Button>("EditCurves"));
            await Task.Delay(150);
            var editor = app.CurveEditor;
            if (!editor.Window.IsVisible || editor.Window.Owner != null || !editor.Window.ShowInTaskbar || editor.Window.WindowStyle != WindowStyle.None)
                throw new Exception("Curve window did not open with desktop chrome.");
            if (editor.Find<ComboBoxItem>("RpmAxis").IsEnabled)
                throw new Exception("RPM axis enabled without calibration.");
            if (editor.Find<Button>("SaveCurve").IsEnabled)
                throw new Exception("RPM curve started without a measured scale.");
            await Send("CALIBRATION|" + CurveCodec.Encode(new FanCalibration { Id = "rpm-ui", CreatedUtc = DateTime.UtcNow, MaximumOnly = true, Maxima = new[] { 5600, 4800 } }));
            if (!editor.Find<Grid>("CpuPlot").IsVisible || !editor.Find<Grid>("GpuPlot").IsVisible || !editor.Find<TextBlock>("Fan1Rpm").IsVisible || !editor.Find<TextBlock>("Fan2Rpm").IsVisible || editor.Find<Border>("PointPanel").IsVisible)
                throw new Exception("The default editor did not show both charts and readings with point fields hidden.");
            Image("FanPilot-curve-editor.png");
            Click(editor.Find<Button>("PointToggle"));
            editor.Find<TextBox>("PointTemperature").Text = "30";
            editor.Find<TextBox>("PointValue").Text = "2240";
            Click(editor.Find<Button>("SetPoint"));
            if (editor.Draft.Cpu[0].Temperature != 30 || editor.Draft.Cpu[0].Duty != 40)
                throw new Exception("Point edit failed.");
            editor.Find<TextBox>("PointValue").Text = "0";
            Click(editor.Find<Button>("SetPoint"));
            if (editor.Draft.Cpu[0].Duty != 40)
                throw new Exception("Unsafe duty edit accepted.");
            editor.Find<TextBox>("PointValue").Text = "2240";
            var stopCurve = editor.Find<Button>("StopCurve");
            if (editor.Window.FindName("MappingConfirmed") != null || editor.Find<CheckBox>("SharedControl").IsChecked != true)
                throw new Exception("Fan assignment gate was not replaced by shared control.");
            if (editor.Find<Button>("SaveCurve").Content.ToString() != "Start curve")
                throw new Exception("Inactive curve has no clear start action.");
            Click(editor.Find<Button>("SaveCurve"));
            await Until(delegate
            {
                return state.Mode == "Curve";
            }, "Curve apply failed: " + state.Error);
            await Send("POLL");
            if (editor.Find<Button>("SaveCurve").Content.ToString() != "Apply changes")
                throw new Exception("Active curve does not expose its update action.");
            if (Get<Slider>("TargetSlider").IsEnabled || !toggle.IsChecked.Value)
                throw new Exception("Main mode did not reflect curve control.");
            Image("FanPilot-curves-pwm.png");
            Click(stopCurve);
            await Until(delegate
            {
                return !state.Manual;
            }, "Curve off failed.");
            if (editor.Find<Border>("AdvancedPanel").IsVisible)
                throw new Exception("Advanced tuning was open by default.");
            var drawer = editor.Find<Grid>("AdvancedDrawer");
            var advanced = editor.Find<ToggleButton>("AdvancedToggle");
            bool? previousMotion = prefs.AnimateFans;
            prefs.AnimateFans = false;
            advanced.IsChecked = true;
            double fullWidth = editor.Find<Border>("AdvancedPanel").Width;
            if (!drawer.IsVisible || drawer.HasAnimatedProperties || Math.Abs(drawer.Width - fullWidth) > .01)
                throw new Exception("Advanced drawer did not open immediately with animations disabled.");
            advanced.IsChecked = false;
            if (drawer.IsVisible || drawer.HasAnimatedProperties)
                throw new Exception("Advanced drawer did not close immediately with animations disabled.");
            prefs.AnimateFans = true;
            advanced.IsChecked = true;
            if (SystemParameters.ClientAreaAnimation && !drawer.HasAnimatedProperties)
                throw new Exception("Advanced drawer did not animate with animations enabled.");
            advanced.IsChecked = false;
            advanced.IsChecked = true;
            await Until(() => drawer.IsVisible && Math.Abs(drawer.Width - fullWidth) < .01, "Advanced drawer did not settle after rapid toggles.");
            prefs.AnimateFans = previousMotion;
            editor.Window.UpdateLayout();
            if (!editor.Find<Button>("Calibrate").IsVisible)
                throw new Exception("Advanced tuning did not expose calibration.");
            Click(editor.Find<Button>("Calibrate"));
            await Until(delegate
            {
                return state.Calibrating;
            }, "UI calibration start failed.");
            if (toggle.IsEnabled || !editor.Find<Button>("CancelCalibration").IsVisible)
                throw new Exception("Calibration controls not synchronized.");
            Image("FanPilot-calibration.png");
            Click(editor.Find<Button>("CancelCalibration"));
            await Until(delegate
            {
                return !state.Calibrating && state.ReleaseConfirmed;
            }, "UI calibration cancel failed.");
            if (editor.Find<TextBlock>("CurveStatus").Text.Contains("Calibrating"))
                throw new Exception("Cancelled calibration left a stale running status.");
            var table = new FanCalibration
            {
                Id = "ui-test",
                CreatedUtc = DateTime.UtcNow,
                Rpms = new[]
                {
                    Enumerable.Range(0, 13).Select(i => 2000 + i * 300).ToArray(),
                    Enumerable.Range(0, 13).Select(i => 1800 + i * 250).ToArray()
                }
            };
            await Send("CALIBRATION|" + CurveCodec.Encode(table));
            if (!editor.Find<ComboBoxItem>("RpmAxis").IsEnabled || prefs.Calibration == null)
                throw new Exception("Calibration not persisted/exposed.");
            editor.Find<ComboBox>("YAxis").SelectedIndex = 1;
            if (editor.Find<TextBlock>("PointUnit").Text != "RPM")
                throw new Exception("RPM chart axis failed.");
            Image("FanPilot-curves-rpm.png");
            editor.Window.Height = editor.Window.MinHeight;
            editor.Window.Width = editor.Window.MinWidth;
            CheckCaptionAlignment(editor.Window, "CurvesMinimize", "CurvesClose");
            Point corner = editor.Find<Button>("SaveCurve").TranslatePoint(new Point(0, editor.Find<Button>("SaveCurve").ActualHeight), editor.Window);
            if (corner.Y > editor.Window.ActualHeight - 8)
                throw new Exception("Curve editor clipped at minimum size.");
            foreach (string name in new[]
            {
                "CpuPlot",
                "GpuPlot",
                "UpdatePreset",
                "RenamePreset",
                "DeletePreset",
                "NewPreset",
                "PointTemperature",
                "PointValue",
                "AdvancedToggle",
                "SaveCurve"
            }

            )
            {
                FrameworkElement control = editor.Find<FrameworkElement>(name);
                Point edge = control.TranslatePoint(new Point(control.ActualWidth, control.ActualHeight), editor.Window);
                if (edge.X > editor.Window.ActualWidth || edge.Y > editor.Window.ActualHeight)
                    throw new Exception("Redesigned editor overflow: " + name);
            }

            if (editor.Find<Grid>("CpuPlot").ActualHeight < 100 || editor.Find<Grid>("GpuPlot").ActualHeight < 100)
                throw new Exception("Chart grid too short to separate numeric tick labels.");
            double chartDifference = Math.Abs(editor.Find<Grid>("CpuPlot").ActualWidth - editor.Find<Grid>("GpuPlot").ActualWidth);
            foreach (string id in new[]
            {
                "UpdatePreset",
                "RenamePreset",
                "DeletePreset",
                "NewPreset",
                "PointToggle",
                "AdvancedToggle"
            }

            )
            {
                var button = editor.Find<ButtonBase>(id);
                var label = new TextBlock
                {
                    Text = button.Content.ToString(),
                    FontFamily = button.FontFamily,
                    FontSize = button.FontSize,
                    FontWeight = button.FontWeight
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (label.DesiredSize.Width + button.Padding.Left + button.Padding.Right + (id == "AdvancedToggle" ? 16 : 2) > button.ActualWidth)
                    throw new Exception("Curve workspace button text is clipped: " + id);
            }

            double dpiScale = VisualTreeHelper.GetDpi(editor.Window).DpiScaleX;
            if (chartDifference * dpiScale > 2.01)
                throw new Exception("Asymmetrical chart layout. Width difference: " + chartDifference);
            foreach (var entry in new[]
            {
                Tuple.Create("PointTemperature", "100"),
                Tuple.Create("PointValue", "10000")
            }

            )
            {
                var field = editor.Find<TextBox>(entry.Item1);
                string previous = field.Text;
                field.Text = entry.Item2;
                editor.Window.UpdateLayout();
                Rect first = field.GetRectFromCharacterIndex(0);
                Rect last = field.GetRectFromCharacterIndex(field.Text.Length - 1, true);
                if (first.IsEmpty || last.IsEmpty || first.Left < 0 || last.Right > field.ActualWidth)
                    throw new Exception("Point field clipped its digits: " + entry.Item1 + " (" + first + ", " + last + ").");
                field.Text = previous;
            }

            if (editor.Find<ComboBox>("YAxis").Visibility != Visibility.Collapsed)
                throw new Exception("RPM chart exposed a PWM axis selector.");
            Image("FanPilot-curves-small.png");
            editor.Window.Close();
            if (editor.Window.IsVisible || state.Manual)
                throw new Exception("Curve editor close/reset failed.");
        }
    }
}
