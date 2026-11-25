using GtaAll.Core.Formats;

namespace GtaAll.Core.Search;

public sealed record EntryQuery(string Text = "", EntryKind? Kind = null, long MinInflated = 0);

public static class EntryFilter
{
    /// <summary>Texte : sous-chaîne du type, ou offset hexadécimal (préfixe 0x accepté).</summary>
    public static List<Rpf9Entry> Apply(IReadOnlyList<Rpf9Entry> all, EntryQuery q, CancellationToken ct = default)
    {
        var text = q.Text.Trim();
        long? hex = null;
        var t = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (t.Length > 0 && long.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var hv)) hex = hv;

        var res = new List<Rpf9Entry>();
        foreach (var e in all)
        {
            ct.ThrowIfCancellationRequested();
            if (q.Kind != null && e.Kind != q.Kind) continue;
            if (e.InflatedSize < q.MinInflated) continue;
            if (text.Length > 0)
            {
                bool ok = e.Kind.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
                          || (hex != null && e.Offset.ToString("X").Contains(t, StringComparison.OrdinalIgnoreCase));
                if (!ok) continue;
            }
            res.Add(e);
        }
        return res;
    }
}
