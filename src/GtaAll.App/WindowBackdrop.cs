using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GtaAll.Core.Appearance;

namespace GtaAll.App;

/// <summary>Barre de titre claire/sombre et fond Mica (Windows 11 22H2+) pour une fenêtre. Sans effet ailleurs.</summary>
internal static class WindowBackdrop
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private const int ImmersiveDarkMode = 20, ImmersiveDarkModeOld = 19, SystemBackdropType = 38;
    private const int BackdropNone = 1, BackdropMica = 2;

    public static void Apply(IntPtr hwnd, ResolvedAppearance r)
    {
        try
        {
            int dark = r.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, ImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, ImmersiveDarkModeOld, ref dark, sizeof(int));

            var target = HwndSource.FromHwnd(hwnd)?.CompositionTarget;
            if (r.UseBackdrop)
            {
                int type = BackdropMica;
                DwmSetWindowAttribute(hwnd, SystemBackdropType, ref type, sizeof(int));
                if (target != null) target.BackgroundColor = Colors.Transparent;
                var all = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref all);
            }
            else
            {
                int type = BackdropNone;
                DwmSetWindowAttribute(hwnd, SystemBackdropType, ref type, sizeof(int));
                var none = new Margins();
                DwmExtendFrameIntoClientArea(hwnd, ref none);
                if (target != null)
                {
                    var bg = Rgb.Parse(r.Theme.Palette.Bg);
                    target.BackgroundColor = Color.FromRgb(bg.R, bg.G, bg.B);
                }
            }
        }
        catch (Exception) { /* purement cosmétique : on ignore */ }
    }
}
