using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace FanPilot
{
    internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        internal static Color ColorFor(string role)
        {
            var color = DesktopTheme.Brush(role).Color;
            return Color.FromArgb(color.R, color.G, color.B);
        }

        internal static Color Surface
        {
            get
            {
                return ColorFor("Panel");
            }
        }

        internal const int CornerDiameter = 20;
        const int HoverInset = 14;
        internal static Color Text
        {
            get
            {
                return ColorFor("Text");
            }
        }

        static Color Edge
        {
            get
            {
                return ColorFor("Line");
            }
        }

        public TrayMenuRenderer() : base(new Palette())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs args)
        {
            if (!args.Item.Selected)
                return;
            var bounds = new Rectangle(HoverInset - args.Item.Bounds.Left, 2, args.ToolStrip.ClientSize.Width - 2 * HoverInset, args.Item.Height - 4);
            using (var outline = Outline(bounds, 10))
            using (var brush = new SolidBrush(ColorFor("Selected")))
            {
                SmoothingMode smoothing = args.Graphics.SmoothingMode;
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                args.Graphics.FillPath(brush, outline);
                args.Graphics.SmoothingMode = smoothing;
            }
        }

        internal static GraphicsPath Outline(Rectangle bounds, int diameter)
        {
            var path = new GraphicsPath();
            int right = bounds.Right - diameter, bottom = bounds.Bottom - diameter;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(right, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(right, bottom, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bottom, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs args)
        {
            args.TextColor = args.Item.Enabled ? args.Item.ForeColor : ColorFor("MutedText");
            if (args.Item is Forms.ToolStripMenuItem || args.Item is Forms.ToolStripLabel)
            {
                int left = HoverInset + 8 - args.Item.Bounds.Left;
                args.TextRectangle = new Rectangle(left, 0, args.Item.Width - left - 8, args.Item.Height);
                args.TextFormat = (args.TextFormat & ~Forms.TextFormatFlags.Bottom) | Forms.TextFormatFlags.VerticalCenter;
            }

            base.OnRenderItemText(args);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs args)
        {
            var bounds = args.ToolStrip.ClientRectangle;
            if (bounds.Width <= CornerDiameter || bounds.Height <= CornerDiameter)
                return;
            bounds.Width--;
            bounds.Height--;
            using (var outline = Outline(bounds, CornerDiameter))
            using (var pen = new Pen(Edge))
            {
                SmoothingMode smoothing = args.Graphics.SmoothingMode;
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                args.Graphics.DrawPath(pen, outline);
                args.Graphics.SmoothingMode = smoothing;
            }
        }

        sealed class Palette : Forms.ProfessionalColorTable
        {
            static Color Selection
            {
                get
                {
                    return ColorFor("Selected");
                }
            }

            public Palette()
            {
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground
            {
                get
                {
                    return Surface;
                }
            }

            public override Color MenuBorder
            {
                get
                {
                    return Edge;
                }
            }

            public override Color MenuItemSelected
            {
                get
                {
                    return Selection;
                }
            }

            public override Color MenuItemBorder
            {
                get
                {
                    return Color.FromArgb(78, 100, 133);
                }
            }

            public override Color MenuItemSelectedGradientBegin
            {
                get
                {
                    return Selection;
                }
            }

            public override Color MenuItemSelectedGradientEnd
            {
                get
                {
                    return Selection;
                }
            }

            public override Color MenuItemPressedGradientBegin
            {
                get
                {
                    return Selection;
                }
            }

            public override Color MenuItemPressedGradientMiddle
            {
                get
                {
                    return Selection;
                }
            }

            public override Color MenuItemPressedGradientEnd
            {
                get
                {
                    return Selection;
                }
            }

            public override Color CheckBackground
            {
                get
                {
                    return Selection;
                }
            }

            public override Color ImageMarginGradientBegin
            {
                get
                {
                    return Surface;
                }
            }

            public override Color ImageMarginGradientMiddle
            {
                get
                {
                    return Surface;
                }
            }

            public override Color ImageMarginGradientEnd
            {
                get
                {
                    return Surface;
                }
            }
        }
    }
}
