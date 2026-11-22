using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace FanPilot
{
    internal sealed class TrayController : IDisposable
    {
        readonly Forms.NotifyIcon icon;
        readonly Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
        readonly System.Drawing.Icon image;
        readonly System.Drawing.Font font = new System.Drawing.Font("Segoe UI", 9F);
        internal Forms.ContextMenuStrip ContextMenu
        {
            get
            {
                return menu;
            }
        }

        public event Action Open, Toggle, Automatic, Exit;
        public TrayController(System.Drawing.Icon image)
        {
            this.image = image;
            menu.Renderer = new TrayMenuRenderer();
            menu.Font = font;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.Padding = new Forms.Padding(8);
            menu.MinimumSize = new Size(254, 0);
            icon = new Forms.NotifyIcon
            {
                Icon = image,
                Text = "ASUS FanPilot Dial",
                Visible = true
            };
            menu.Items.Add(new Forms.ToolStripLabel("FanPilot Dial") { Padding = new Forms.Padding(13, 8, 13, 10), TextAlign = ContentAlignment.MiddleLeft });
            menu.Items.Add("Open dashboard", null, delegate
            {
                Raise(Open);
            });
            menu.Items.Add("Toggle fan control", null, delegate
            {
                Raise(Toggle);
            });
            menu.Items.Add("Restore firmware control", null, delegate
            {
                Raise(Automatic);
            });
            var exitItem = menu.Items.Add("Exit FanPilot", null, delegate
            {
                Raise(Exit);
            });
            foreach (Forms.ToolStripItem item in menu.Items)
                if (item is Forms.ToolStripMenuItem)
                    item.Padding = new Forms.Padding(13, 8, 13, 8);
            exitItem.Margin = new Forms.Padding(0, 5, 0, 0);
            Action updateShape = delegate
            {
                int diameter = TrayMenuRenderer.CornerDiameter;
                if (menu.Width <= diameter || menu.Height <= diameter)
                    return;
                var previous = menu.Region;
                using (var shape = TrayMenuRenderer.Outline(menu.ClientRectangle, diameter))
                    menu.Region = new Region(shape);
                if (previous != null)
                    previous.Dispose();
            };
            menu.SizeChanged += delegate
            {
                updateShape();
            };
            menu.Opening += delegate
            {
                updateShape();
            };
            RefreshTheme();
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += delegate
            {
                Raise(Open);
            };
        }

        public void RefreshTheme()
        {
            menu.BackColor = TrayMenuRenderer.Surface;
            menu.ForeColor = TrayMenuRenderer.Text;
            menu.Items[0].ForeColor = TrayMenuRenderer.ColorFor("Accent");
            menu.Invalidate();
        }

        static void Raise(Action action)
        {
            if (action != null)
                action();
        }

        public void Update(Snapshot state)
        {
            var status = (Forms.ToolStripLabel)menu.Items[0];
            string owner = !state.Manual && !state.ReleaseConfirmed ? "Reset unconfirmed" : !state.Connected ? "Hardware unavailable" : state.Calibrating ? "Calibrating" : state.Mode == "Curve" ? "Curve running" : state.Manual ? "Fixed PWM" : "Firmware control";
            if (status.Text != owner)
                status.Text = owner;
            if (!state.Manual && !state.ReleaseConfirmed)
            {
                if (icon.Text != "ASUS FanPilot · firmware reset unconfirmed")
                    icon.Text = "ASUS FanPilot · firmware reset unconfirmed";
                return;
            }

            string tooltip = "ASUS FanPilot · " + (state.Connected && state.Valid ? (state.Manual ? "Manual" : "Automatic") + " · " + state.Temperature + "°C" : "Hardware unavailable");
            if (icon.Text != tooltip)
                icon.Text = tooltip;
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
            font.Dispose();
            image.Dispose();
        }
    }
}
