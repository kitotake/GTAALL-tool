using System.Globalization;

namespace GtaAll.Core.Appearance;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static bool TryParse(string? s, out Rgb c)
    {
        c = default;
        if (s is null) return false;
        s = s.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        c = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgb Parse(string s) => TryParse(s, out var c) ? c : throw new FormatException($"Couleur invalide : {s}");
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string Argb(double alpha) => $"#{(int)Math.Round(Math.Clamp(alpha, 0, 1) * 255):X2}{R:X2}{G:X2}{B:X2}";
}

public static class ColorMath
{
    private static double Lin(byte c) { var v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
    public static double Luminance(Rgb c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);

    /// <summary>Rapport de contraste WCAG (1 à 21).</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Mélange : <paramref name="t"/> = poids de <paramref name="top"/>.</summary>
    public static Rgb Blend(Rgb top, Rgb bottom, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte M(byte x, byte y) => (byte)Math.Round(x * t + y * (1 - t));
        return new Rgb(M(top.R, bottom.R), M(top.G, bottom.G), M(top.B, bottom.B));
    }

    /// <summary>Éloigne la couleur du fond (vers noir ou blanc) jusqu'au contraste demandé.</summary>
    public static Rgb EnsureContrast(Rgb fg, Rgb bg, double min, out bool adjusted)
    {
        adjusted = false;
        if (Contrast(fg, bg) >= min) return fg;
        var target = Luminance(bg) > 0.5 ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255);
        for (double t = 0.05; t <= 1.0001; t += 0.05)
        {
            var c = Blend(target, fg, t);
            if (Contrast(c, bg) >= min) { adjusted = true; return c; }
        }
        adjusted = true;
        return target;
    }

    public static Rgb PickOn(Rgb bg, Rgb dark, Rgb light) => Contrast(dark, bg) >= Contrast(light, bg) ? dark : light;
}
