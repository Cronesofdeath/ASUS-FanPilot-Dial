using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace FanPilot
{
    public sealed class NumberLabel : TextBlock
    {
        public static readonly DependencyProperty ReadingProperty = DependencyProperty.Register("Reading", typeof(double), typeof(NumberLabel), new PropertyMetadata(0.0, delegate (DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((NumberLabel)d).Text = ((double)e.NewValue).ToString("N0");
        }));
        public void Set(int value, bool animate)
        {
            double from = (double)GetValue(ReadingProperty);
            BeginAnimation(ReadingProperty, null);
            SetValue(ReadingProperty, (double)value);
            if (animate && from != value)
            {
                var sweep = new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction = new QuadraticEase
                    {
                        EasingMode = EasingMode.EaseOut
                    },
                    FillBehavior = FillBehavior.Stop
                };
                Timeline.SetDesiredFrameRate(sweep, 30);
                BeginAnimation(ReadingProperty, sweep);
            }
            else
                Text = value.ToString("N0");
        }
    }
}
