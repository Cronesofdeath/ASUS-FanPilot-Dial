using System.IO;
using System.Windows.Media;
using System.Windows;
using System.Windows.Markup;

namespace FanPilot
{
    // All windows use one control library; the embedded markup is trusted application code.
    internal static class DesktopTheme
    {
        static readonly string[] Roles =
        {
            "Canvas",
            "Panel",
            "Control",
            "Selected",
            "Line",
            "Accent",
            "FanTwo",
            "OnAccent"
        };
        static readonly string[][] Palettes =
        {
            new[]
            {
                "#090A0C",
                "#121316",
                "#1D1E23",
                "#302C2B",
                "#35363B",
                "#FFB676",
                "#B8CADF",
                "#221306"
            },
            new[]
            {
                "#080E17",
                "#0E1725",
                "#17253A",
                "#26394F",
                "#344761",
                "#86C7FF",
                "#B5A8F8",
                "#091C2D"
            },
            new[]
            {
                "#0C1318",
                "#131E26",
                "#1C2D36",
                "#293F46",
                "#3C505A",
                "#9EDAD9",
                "#DABEA9",
                "#122729"
            },
            new[]
            {
                "#100C15",
                "#191320",
                "#241C2D",
                "#372B40",
                "#493E52",
                "#DBB2F3",
                "#F2B5A7",
                "#26122F"
            },
            new[]
            {
                "#120F0D",
                "#1D1815",
                "#29221D",
                "#3C3027",
                "#4E423A",
                "#E8CC99",
                "#BBBDDC",
                "#281F11"
            },
            new[]
            {
                "#140B10",
                "#20131B",
                "#2C1C26",
                "#422937",
                "#533B48",
                "#F0A6B4",
                "#E0C392",
                "#2C1420"
            },
            new[]
            {
                "#0C0D10",
                "#15171A",
                "#202226",
                "#32363C",
                "#41454D",
                "#E5E7EA",
                "#B1BED0",
                "#202124"
            }
        };
        internal static int Index(int value)
        {
            return value >= 0 && value < Palettes.Length ? value : 0;
        }

        public static void Apply(int index)
        {
            string[] colors = Palettes[Index(index)];
            for (int i = 0; i < Roles.Length; i++)
                SetBrush(Roles[i], colors[i]);
            SetBrush("Text", "#E8EBF2");
            SetBrush("MutedText", "#A7B2C4");
            SetBrush("WarningText", "#F3B4A7");
            SetBrush("SwitchThumb", "#F4F6FA");
            SetBrush("NoticeSurface", "#30281E");
            SetBrush("NoticeBorder", "#645035");
            SetBrush("NoticeText", "#E8C694");
            SetBrush("Focus", "#E8EBF2");
            SetBrush("CaptionHover", "#525459");
        }

        static void SetBrush(string role, string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            Application.Current.Resources["Studio" + role] = brush;
        }

        internal static SolidColorBrush Brush(string role)
        {
            return (SolidColorBrush)Application.Current.Resources["Studio" + role];
        }

        static ResourceDictionary controls;
        public static Window LoadWindow(string name)
        {
            if (controls == null)
            {
                controls = (ResourceDictionary)LoadMarkup("Theme.xaml");
                Application.Current.Resources.MergedDictionaries.Add(controls);
            }

            return (Window)LoadMarkup(name);
        }

        static object LoadMarkup(string resource)
        {
            using (Stream stream = typeof(DesktopTheme).Assembly.GetManifestResourceStream(resource))
                return XamlReader.Load(stream);
        }
    }
}
