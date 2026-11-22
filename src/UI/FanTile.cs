using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FanPilot
{
    public sealed class FanTile
    {
        public readonly Border Root;
        readonly Tachometer dial;
        readonly NumberLabel reading;
        readonly TextBlock detail;
        bool motion;
        public FanTile(int index)
        {
            var panel = new Grid
            {
                Height = 176
            };
            Root = new Border
            {
                Child = panel
            };
            dial = new Tachometer(index);
            panel.Children.Add(dial);
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 0)
            };
            panel.Children.Add(stack);
            var title = new TextBlock
            {
                Text = "FAN  " + index,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, index == 1 ? "StudioAccent" : "StudioFanTwo");
            stack.Children.Add(title);
            reading = new NumberLabel
            {
                Text = "—",
                FontFamily = new FontFamily("Bahnschrift"),
                FontSize = 37,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            dial.SetBinding(Tachometer.DisplayProperty, new Binding("Reading") { Source = reading, Mode = BindingMode.OneWay });
            stack.Children.Add(reading);
            stack.Children.Add(new TextBlock { Text = "RPM", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            detail = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                FontSize = 11
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "StudioMutedText");
            panel.Children.Add(detail);
        }

        public void Update(int rpm, string status, bool animate)
        {
            if (dial.Rpm != rpm || motion != animate)
                reading.Set(rpm, animate && dial.Rpm >= 0);
            dial.Rpm = rpm;
            motion = animate;
            detail.Text = status;
        }

        public void Motion(bool enabled)
        {
            if (!enabled && motion && dial.Rpm >= 0)
                reading.Set(dial.Rpm, false);
            motion = enabled;
        }

        public void Offline()
        {
            Motion(false);
            dial.Rpm = -1;
            reading.Text = "—";
            detail.Text = "Sensor unavailable";
        }

        sealed class Tachometer : FrameworkElement
        {
            static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(Tachometer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
            int rpm = -1;
            public int Rpm
            {
                get
                {
                    return rpm;
                }

                set
                {
                    if (rpm != value)
                    {
                        rpm = value;
                        InvalidateVisual();
                    }
                }
            }

            public Tachometer(int index)
            {
                SetResourceReference(AccentProperty, index == 1 ? "StudioAccent" : "StudioFanTwo");
            }

            internal static readonly DependencyProperty DisplayProperty = DependencyProperty.Register("Display", typeof(double), typeof(Tachometer), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            DrawingGroup face;
            double cachedRadius, cachedDpi;
            int cachedScale;
            Brush cachedEdge;
            static Vector Direction(double fraction)
            {
                double radians = (145 + 250 * fraction) * Math.PI / 180;
                return new Vector(Math.Cos(radians), Math.Sin(radians));
            }

            static Geometry Sweep(double radius, double fraction)
            {
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure((Point)(Direction(0) * radius), false, false);
                    path.ArcTo((Point)(Direction(fraction) * radius), new Size(radius, radius), 0, fraction * 250 > 180, SweepDirection.Clockwise, true, false);
                }

                geometry.Freeze();
                return geometry;
            }

            DrawingGroup Face(double radius, int scale, Brush edge, double dpi)
            {
                if (face != null && radius == cachedRadius && scale == cachedScale && edge == cachedEdge && dpi == cachedDpi)
                    return face;
                cachedRadius = radius;
                cachedScale = scale;
                cachedEdge = edge;
                cachedDpi = dpi;
                face = new DrawingGroup();
                using (var dc = face.Open())
                {
                    dc.DrawGeometry(null, new Pen(edge, 3), Sweep(radius, 1));
                    dc.PushOpacity(.45);
                    dc.DrawGeometry(null, new Pen(edge, 1), Sweep(radius - 13, 1));
                    dc.Pop();
                    var fine = new Pen(DesktopTheme.Brush("MutedText"), .8);
                    var major = new Pen(DesktopTheme.Brush("MutedText"), 1.5);
                    for (int tick = 0; tick <= 24; tick++)
                    {
                        var ray = Direction(tick / 24.0);
                        bool longTick = tick % 4 == 0;
                        dc.DrawLine(longTick ? major : fine, (Point)(ray * (radius - 3)), (Point)(ray * (radius - (longTick ? 12 : 7))));
                    }

                    for (int end = 0; end <= 1; end++)
                    {
                        var label = new FormattedText(end == 0 ? "0" : scale.ToString("N0"), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, DesktopTheme.Brush("MutedText"), dpi);
                        dc.DrawText(label, new Point(Direction(end).X * radius - label.Width / 2, 50));
                    }
                }

                face.Freeze();
                return face;
            }

            protected override void OnRender(DrawingContext dc)
            {
                double radius = Math.Min(81, ActualWidth / 2 - 18);
                if (radius < 1)
                    return;
                int scale = Math.Max(6000, (int)Math.Ceiling(rpm / 1000.0) * 1000);
                var accent = (Brush)GetValue(AccentProperty);
                dc.PushTransform(new TranslateTransform(ActualWidth / 2, 93));
                dc.DrawDrawing(Face(radius, scale, DesktopTheme.Brush("Line"), VisualTreeHelper.GetDpi(this).PixelsPerDip));
                if (rpm >= 0)
                {
                    double fraction = Math.Max(0, Math.Min(1, (double)GetValue(DisplayProperty) / scale));
                    if (fraction > 0)
                    {
                        var arc = Sweep(radius, fraction);
                        dc.PushOpacity(.14);
                        dc.DrawGeometry(null, new Pen(accent, 7), arc);
                        dc.Pop();
                        dc.DrawGeometry(null, new Pen(accent, 3), arc);
                    }

                    var ray = Direction(fraction);
                    var tangent = new Vector(-ray.Y, ray.X);
                    var pointer = new StreamGeometry();
                    using (var path = pointer.Open())
                    {
                        path.BeginFigure((Point)(ray * (radius - 13)), true, true);
                        path.LineTo((Point)(ray * (radius + 4) - tangent * 2.1), true, false);
                        path.LineTo((Point)(ray * (radius + 4) + tangent * 2.1), true, false);
                    }

                    dc.DrawGeometry(DesktopTheme.Brush("Text"), null, pointer);
                }

                dc.Pop();
            }
        }
    }
}
