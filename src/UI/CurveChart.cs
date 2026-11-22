using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FanPilot
{
    internal sealed class CurveChart : FrameworkElement
    {
        public CurvePoint[] Points;
        public FanCalibration Calibration;
        public int FanIndex, SelectedIndex;
        public bool SelectionActive;
        public bool RpmAxis, Snap = true;
        public double? LiveTemperature;
        public double PreviewInput;
        public int PreviewDuty;
        public event Action<CurveChart> Edited;
        bool dragging;
        Brush accent
        {
            get
            {
                return DesktopTheme.Brush(FanIndex == 0 ? "Accent" : "FanTwo");
            }
        }

        Brush muted
        {
            get
            {
                return DesktopTheme.Brush("MutedText");
            }
        }

        public CurveChart()
        {
            Focusable = true;
            MouseLeftButtonDown += delegate (object sender, MouseButtonEventArgs args)
            {
                if (Points == null)
                    return;
                Point mouse = args.GetPosition(this);
                double closest = Double.MaxValue;
                for (int i = 0; i < Points.Length; i++)
                {
                    Vector distance = Position(Points[i]) - mouse;
                    if (distance.LengthSquared < closest)
                    {
                        closest = distance.LengthSquared;
                        SelectedIndex = i;
                    }
                }

                dragging = true;
                CaptureMouse();
                Focus();
                Notify();
                args.Handled = true;
            };
            MouseMove += delegate (object sender, MouseEventArgs args)
            {
                if (!dragging || args.LeftButton != MouseButtonState.Pressed)
                    return;
                Point mouse = args.GetPosition(this);
                Rect plot = Plot;
                int temperature = (int)Math.Round(20 + 80 * (mouse.X - plot.Left) / plot.Width);
                double displayed = Minimum + (Maximum - Minimum) * (plot.Bottom - mouse.Y) / plot.Height;
                int duty = RpmAxis && Calibration != null ? Calibration.ToDuty(FanIndex, displayed) : (int)Math.Round(displayed);
                if (Snap)
                {
                    temperature = (int)Math.Round(temperature / 5.0) * 5;
                    duty = (int)Math.Round(duty / 5.0) * 5;
                }

                CurvePoint point = Points[SelectedIndex];
                point.Temperature = Math.Max(SelectedIndex == 0 ? 20 : Points[SelectedIndex - 1].Temperature + 1, Math.Min(SelectedIndex == Points.Length - 1 ? 100 : Points[SelectedIndex + 1].Temperature - 1, temperature));
                point.Duty = Math.Max(SelectedIndex == 0 ? 40 : Points[SelectedIndex - 1].Duty, Math.Min(SelectedIndex == Points.Length - 1 ? 100 : Points[SelectedIndex + 1].Duty, duty));
                Notify();
            };
            MouseLeftButtonUp += delegate
            {
                dragging = false;
                ReleaseMouseCapture();
            };
            LostMouseCapture += delegate
            {
                dragging = false;
            };
            KeyDown += delegate (object sender, KeyEventArgs args)
            {
                if (Points == null)
                    return;
                if (args.Key == Key.Left || args.Key == Key.Right)
                {
                    SelectedIndex = Math.Max(0, Math.Min(Points.Length - 1, SelectedIndex + (args.Key == Key.Left ? -1 : 1)));
                    Notify();
                    args.Handled = true;
                    return;
                }

                int direction = args.Key == Key.Up ? 1 : args.Key == Key.Down ? -1 : 0;
                if (direction != 0)
                {
                    CurvePoint point = Points[SelectedIndex];
                    point.Duty = Math.Max(SelectedIndex == 0 ? 40 : Points[SelectedIndex - 1].Duty, Math.Min(SelectedIndex == Points.Length - 1 ? 100 : Points[SelectedIndex + 1].Duty, point.Duty + direction));
                    Notify();
                    args.Handled = true;
                }
            };
        }

        Rect Plot
        {
            get
            {
                return new Rect(62, 22, Math.Max(30, ActualWidth - 82), Math.Max(30, ActualHeight - 50));
            }
        }

        double Minimum
        {
            get
            {
                return RpmAxis && Calibration != null ? Calibration.ToRpm(FanIndex, 40) : 40;
            }
        }

        double Maximum
        {
            get
            {
                return RpmAxis && Calibration != null ? Math.Max(Minimum + 1, Calibration.ToRpm(FanIndex, 100)) : 100;
            }
        }

        Point Position(CurvePoint point)
        {
            Rect plot = Plot;
            double y = RpmAxis && Calibration != null ? Calibration.ToRpm(FanIndex, point.Duty) : point.Duty;
            return new Point(plot.Left + (point.Temperature - 20) / 80.0 * plot.Width, plot.Bottom - (y - Minimum) / (Maximum - Minimum) * plot.Height);
        }

        void Notify()
        {
            InvalidateVisual();
            if (Edited != null)
                Edited(this);
        }

        void Text(DrawingContext context, string value, double x, double y, Brush brush, TextAlignment alignment = TextAlignment.Left)
        {
            var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            text.TextAlignment = alignment;
            context.DrawText(text, new Point(x, y));
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            if (RpmAxis && Calibration == null)
            {
                Text(context, "CALIBRATION REQUIRED", 18, Math.Max(30, ActualHeight / 2 - 14), muted);
                Text(context, "Measure maximum RPM to set this scale.", 18, Math.Max(48, ActualHeight / 2 + 6), muted);
                return;
            }

            Rect plot = Plot;
            var grid = new Pen(DesktopTheme.Brush("Line"), 0.7);
            for (int i = 0; i <= 8; i++)
            {
                double x = plot.Left + i * plot.Width / 8;
                context.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
                if (i % 2 == 0)
                    Text(context, (20 + i * 10).ToString(), x, plot.Bottom + 7, muted, TextAlignment.Center);
            }

            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Bottom - i * plot.Height / 4;
                context.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
                string tick = (Minimum + (Maximum - Minimum) * i / 4).ToString("N0");
                Text(context, tick, plot.Left - 9, y - 8, muted, TextAlignment.Right);
            }

            if (Points != null && Points.Length > 1)
            {
                var area = new StreamGeometry();
                using (StreamGeometryContext path = area.Open())
                {
                    path.BeginFigure(new Point(Position(Points[0]).X, plot.Bottom), true, true);
                    foreach (CurvePoint point in Points)
                        path.LineTo(Position(point), true, false);
                    path.LineTo(new Point(Position(Points[Points.Length - 1]).X, plot.Bottom), true, false);
                }

                var gradient = new LinearGradientBrush(((SolidColorBrush)accent).Color, Colors.Transparent, new Point(0, 0), new Point(0, 1));
                gradient.Opacity = 0.13;
                context.DrawGeometry(gradient, null, area);
            }

            if (LiveTemperature.HasValue)
            {
                double x = plot.Left + Math.Max(0, Math.Min(80, LiveTemperature.Value - 20)) / 80 * plot.Width;
                var marker = new Pen(new SolidColorBrush(Color.FromRgb(143, 152, 169)), 1)
                {
                    DashStyle = DashStyles.Dash
                };
                context.DrawLine(marker, new Point(x, plot.Top), new Point(x, plot.Bottom));
                Text(context, Math.Round(LiveTemperature.Value) + "°", x, 0, DesktopTheme.Brush("MutedText"), TextAlignment.Center);
            }

            if (PreviewDuty >= 40 && PreviewDuty <= 100)
            {
                double x = plot.Left + (PreviewInput - 20) / 80.0 * plot.Width;
                double estimate = RpmAxis && Calibration != null ? Calibration.ToRpm(FanIndex, PreviewDuty) : PreviewDuty;
                double y = plot.Bottom - (estimate - Minimum) / (Maximum - Minimum) * plot.Height;
                var pen = new Pen(accent, 1)
                {
                    DashStyle = DashStyles.Dot
                };
                context.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                context.DrawLine(pen, new Point(plot.Left, y), new Point(x, y));
                context.DrawEllipse(null, new Pen(DesktopTheme.Brush("Text"), 1.5), new Point(x, y), 6, 6);
            }

            if (Points == null)
                return;
            for (int i = 1; i < Points.Length; i++)
                context.DrawLine(new Pen(accent, 2), Position(Points[i - 1]), Position(Points[i]));
            for (int i = 0; i < Points.Length; i++)
            {
                if (SelectionActive && i == SelectedIndex)
                    context.DrawEllipse(new SolidColorBrush(Color.FromArgb(40, 204, 234, 164)), null, Position(Points[i]), 10, 10);
                context.DrawEllipse(SelectionActive && i == SelectedIndex ? accent : DesktopTheme.Brush("Panel"), new Pen(accent, 1.7), Position(Points[i]), 4, 4);
            }
        }
    }
}
