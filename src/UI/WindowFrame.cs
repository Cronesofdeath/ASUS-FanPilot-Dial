using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace FanPilot
{
    internal static class WindowFrame
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        public static void Apply(Window view)
        {
            view.WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(view, new WindowChrome { CaptionHeight = 60, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false });
            view.SourceInitialized += delegate
            {
                int dark = 1;
                try
                {
                    DwmSetWindowAttribute(new WindowInteropHelper(view).Handle, 20, ref dark, 4);
                    int rounded = 2;
                    DwmSetWindowAttribute(new WindowInteropHelper(view).Handle, 33, ref rounded, 4);
                }
                catch
                {
                }
            };
        }

        public static void EnableButton(Button button)
        {
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            string name = button.Name;
            if (name == "OpenSettings")
                SetIcon(button, "M 11.69,5.27 L 11.89,5.95 L 13.40,5.88 L 13.40,8.12 L 11.89,8.05 L 11.54,9.09 L 11.20,9.71 L 12.32,10.74 L 10.74,12.32 L 9.71,11.20 L 8.73,11.69 L 8.05,11.89 L 8.12,13.40 L 5.88,13.40 L 5.95,11.89 L 4.91,11.54 L 4.29,11.20 L 3.26,12.32 L 1.68,10.74 L 2.80,9.71 L 2.31,8.73 L 2.11,8.05 L 0.60,8.12 L 0.60,5.88 L 2.11,5.95 L 2.46,4.91 L 2.80,4.29 L 1.68,3.26 L 3.26,1.68 L 4.29,2.80 L 5.27,2.31 L 5.95,2.11 L 5.88,0.60 L 8.12,0.60 L 8.05,2.11 L 9.09,2.46 L 9.71,2.80 L 10.74,1.68 L 12.32,3.26 L 11.20,4.29 Z M 9,7 A 2,2 0 1 1 5,7 A 2,2 0 1 1 9,7 Z", 14, 14);
            else if (name.EndsWith("Minimize"))
                SetIcon(button, "M 0,0 H 8", 8, 1);
            else if (name.EndsWith("Close"))
                SetIcon(button, "M 0,0 L 8,8 M 8,0 L 0,8", 8, 8);
        }

        static void SetIcon(Button button, string geometry, double width, double height)
        {
            var icon = new Path
            {
                Data = Geometry.Parse(geometry),
                StrokeThickness = 1.2,
                Width = width + 2,
                Height = height + 2,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            icon.SetBinding(Path.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            button.Content = icon;
        }

        public static void WireSecondaryControls(Window view, string prefix)
        {
            var minimize = (Button)view.FindName(prefix + "Minimize");
            EnableButton(minimize);
            minimize.Click += delegate
            {
                view.WindowState = WindowState.Minimized;
            };
        }
    }
}
