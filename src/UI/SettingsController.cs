using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace FanPilot
{
    internal sealed class SettingsController : IDisposable
    {
        readonly Window owner;
        readonly Preferences preferences;
        readonly Action<string> reportError;
        readonly bool testInput;
        Button shortcut;
        KeyboardShortcut keyboard;
        string shortcutError;
        bool disposed;
        public Window Window { get; private set; }
        public bool IsRecording { get; private set; }

        public bool ShortcutAvailable
        {
            get
            {
                return keyboard != null;
            }
        }

        public event Action ToggleRequested;
        public event Action Changed;
        public SettingsController(Window owner, Preferences preferences, bool testInput, Action<string> reportError)
        {
            this.owner = owner;
            this.preferences = preferences;
            this.testInput = testInput;
            this.reportError = reportError;
            owner.PreviewKeyDown += HandleKey;
        }

        void CreateWindow()
        {
            Window = DesktopTheme.LoadWindow("Settings.xaml");
            Window.Icon = owner.Icon;
            WindowFrame.Apply(Window);
            WindowFrame.WireSecondaryControls(Window, "Settings");
            Window.StateChanged += delegate
            {
                if (Window.WindowState == WindowState.Minimized)
                    CancelRecording();
            };
            var themeChoice = Find<ComboBox>("ThemeChoice");
            themeChoice.SelectedIndex = DesktopTheme.Index(preferences.ThemeIndex);
            themeChoice.SelectionChanged += delegate
            {
                preferences.ThemeIndex = DesktopTheme.Index(themeChoice.SelectedIndex);
                DesktopTheme.Apply(preferences.ThemeIndex);
                Save();
            };
            shortcut = Find<Button>("Shortcut");
            Find<CheckBox>("Startup").IsChecked = StartupTask.StartupEnabled;
            Find<CheckBox>("CloseToTray").IsChecked = preferences.CloseToTray;
            Find<CheckBox>("Animations").IsChecked = preferences.AnimateFans ?? true;
            Find<CheckBox>("ShortcutEnabled").IsChecked = preferences.HotkeyEnabled;
            Find<CheckBox>("Startup").Click += async delegate
            {
                await ChangeStartupAsync();
            };
            Find<CheckBox>("CloseToTray").Click += delegate
            {
                preferences.CloseToTray = Find<CheckBox>("CloseToTray").IsChecked == true;
                Save();
            };
            Find<CheckBox>("Animations").Click += delegate
            {
                preferences.AnimateFans = Find<CheckBox>("Animations").IsChecked == true;
                Save();
            };
            Find<CheckBox>("ShortcutEnabled").Click += delegate
            {
                preferences.HotkeyEnabled = Find<CheckBox>("ShortcutEnabled").IsChecked == true;
                if (keyboard != null)
                    keyboard.Keys.Enabled = preferences.HotkeyEnabled;
                Save();
            };
            shortcut.Click += delegate
            {
                RecordShortcut();
            };
            Find<Button>("SettingsDone").Click += delegate
            {
                Hide();
            };
            Find<Button>("SettingsClose").Click += delegate
            {
                Window.Close();
            };
            WindowFrame.EnableButton(Find<Button>("SettingsClose"));
            Window.Closing += delegate (object sender, System.ComponentModel.CancelEventArgs args)
            {
                if (!disposed)
                {
                    args.Cancel = true;
                    Hide();
                }
            };
            Window.PreviewKeyDown += HandleKey;
            RefreshShortcut();
        }

        public T Find<T>(string name)
            where T : class
        {
            return Window == null ? null : Window.FindName(name) as T;
        }

        public void InitializeShortcut()
        {
            try
            {
                keyboard = new KeyboardShortcut(testInput);
                keyboard.Keys.Binding = preferences.GetShortcutKeys();
                keyboard.Keys.Enabled = preferences.HotkeyEnabled;
                keyboard.Keys.Activated += delegate
                {
                    Dispatch(delegate
                    {
                        if (ToggleRequested != null)
                            ToggleRequested();
                    });
                };
                keyboard.Keys.Captured += delegate (int[] keys)
                {
                    Dispatch(delegate
                    {
                        preferences.ShortcutKeys = keys;
                        preferences.HotkeyEnabled = true;
                        keyboard.Keys.Binding = keys;
                        keyboard.Keys.Enabled = true;
                        IsRecording = false;
                        shortcut.Content = KeyLabel();
                        Find<CheckBox>("ShortcutEnabled").IsChecked = true;
                        Save();
                        SetHint("Shortcut saved");
                    });
                };
                keyboard.Keys.CaptureRejected += delegate
                {
                    Dispatch(delegate
                    {
                        IsRecording = false;
                        shortcut.Content = KeyLabel();
                        SetHint("Maximum: 3 keys. Shortcut unchanged.");
                    });
                };
            }
            catch (Exception exception)
            {
                if (keyboard != null)
                {
                    keyboard.Dispose();
                    keyboard = null;
                }

                shortcutError = "Shortcut unavailable: " + exception.Message;
            }

            RefreshShortcut();
        }

        void RefreshShortcut()
        {
            if (shortcut == null)
                return;
            shortcut.IsEnabled = keyboard != null;
            if (keyboard != null)
                shortcut.Content = KeyLabel();
            if (shortcutError != null)
                SetHint(shortcutError);
        }

        void Dispatch(Action action)
        {
            if (disposed)
                return;
            owner.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!disposed)
                    action();
            }));
        }

        internal void RefreshStartup()
        {
            if (Window != null)
                Find<CheckBox>("Startup").IsChecked = StartupTask.StartupEnabled;
        }

        async Task ChangeStartupAsync()
        {
            CheckBox control = Find<CheckBox>("Startup");
            bool enabled = control.IsChecked == true;
            control.IsEnabled = false;
            try
            {
                await Task.Run(delegate
                {
                    StartupTask.SetStartup(enabled);
                });
            }
            catch (Exception exception)
            {
                reportError(exception.Message);
                control.IsChecked = StartupTask.StartupEnabled;
            }
            finally
            {
                control.IsEnabled = true;
            }
        }

        void RecordShortcut()
        {
            if (keyboard == null)
                return;
            if (IsRecording)
            {
                CancelRecording();
                return;
            }

            IsRecording = true;
            keyboard.Keys.BeginCapture();
            shortcut.Content = "Recording…";
            shortcut.Focus();
            SetHint("Hold 1–3 keys, then release. Select again to cancel.");
        }

        void CancelRecording()
        {
            if (!IsRecording)
                return;
            keyboard.Keys.CancelCapture();
            IsRecording = false;
            shortcut.Content = KeyLabel();
            SetHint("Recording cancelled");
        }

        void HandleKey(object sender, KeyEventArgs args)
        {
            if (IsRecording)
                args.Handled = true;
            else if (args.Key == Key.Escape && Window != null && Window.IsVisible)
            {
                Hide();
                args.Handled = true;
            }
        }

        string KeyLabel()
        {
            return String.Join(" + ", preferences.GetShortcutKeys().Select(delegate (int key)
            {
                if (key == 17)
                    return "Ctrl";
                if (key == 18)
                    return "Alt";
                if (key == 16)
                    return "Shift";
                if (key == 91)
                    return "Win";
                return ((System.Windows.Forms.Keys)key).ToString();
            }));
        }

        void SetHint(string text)
        {
            Find<TextBlock>("ShortcutHint").Text = text;
        }

        void NotifyChanged()
        {
            if (Changed != null)
                Changed();
        }

        void Save()
        {
            try
            {
                preferences.Save();
            }
            catch (Exception exception)
            {
                reportError("Settings save failed: " + exception.Message);
            }

            NotifyChanged();
        }

        public void Show()
        {
            if (Window == null)
                CreateWindow();
            Window.Show();
            Window.WindowState = WindowState.Normal;
            Window.Activate();
            NotifyChanged();
        }

        public void Hide()
        {
            if (Window == null)
                return;
            CancelRecording();
            Window.Hide();
            NotifyChanged();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            owner.PreviewKeyDown -= HandleKey;
            if (keyboard != null)
                keyboard.Dispose();
            if (Window != null)
                Window.Close();
        }
    }
}
