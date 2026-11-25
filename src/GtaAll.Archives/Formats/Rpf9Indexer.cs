namespace GtaAll.Core.Formats;

public sealed record IndexProgress(long Position, long Length, long Streams, long Entries);

/// <summary>
/// Indexation par « carving » zlib d'une archive RPF9, en lecture seule
/// (algorithme décrit dans GTA6-Asset-Info/formats/01-rpf9.md ; implémentation originale).
/// Monothread : les performances sur de très gros fichiers ne sont pas mesurées.
/// </summary>
public static class Rpf9Indexer
{
    private const int ScanChunk = 1 << 20;
    private const int DecodeWindow = 16 << 20;
    private const int MaxInflated = 256 << 20;
    private const int HeadSize = 32;

    public static Rpf9Index Build(string path, IProgress<IndexProgress>? progress, CancellationToken ct)
    {
        var fi = new FileInfo(path);
        using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = RandomAccess.GetLength(h);

        var hdrBytes = new byte[Rpf9Header.Size];
        if (RandomAccess.Read(h, hdrBytes, 0) < Rpf9Header.Size) throw new InvalidDataException("Fichier trop court.");
        var header = Rpf9Header.Parse(hdrBytes, length);

        var idx = new Rpf9Index
        {
            ArchivePath = fi.FullName, ArchiveLength = length, LastWriteTicks = fi.LastWriteTimeUtc.Ticks,
            HeaderFields = header.Fields.Select(f => new Rpf9Index.HeaderFieldDto(f.Offset, f.Size, f.Name, f.Value, f.Confidence)).ToList(),
            Warnings = header.Warnings.ToList()
        };
        if (header.TocEnd < Rpf9Header.Size || header.TocEnd > length) return idx;

        var scan = new byte[ScanChunk];
        var win = new byte[DecodeWindow];
        long pos = Align16(header.TocEnd);
        Rpf9Entry? cur = null;
        bool prevFull = false;
        var lastReport = Environment.TickCount64;

        while (pos + 2 <= length)
        {
            ct.ThrowIfCancellationRequested();
            int n = RandomAccess.Read(h, scan.AsSpan(0, (int)Math.Min(ScanChunk, length - pos)), pos);
            if (n < 2) break;
            bool advanced = false;
            for (int i = 0; i + 2 <= n; i += 16)
            {
                if (scan[i] != 0x78 || scan[i + 1] != 0xDA) continue;
                long at = pos + i;
                int wn = RandomAccess.Read(h, win.AsSpan(0, (int)Math.Min(DecodeWindow, length - at)), at);
                if (!ZlibInflater.TryInflate(win, 0, wn, MaxInflated, out var data, out int outLen, out int used)) continue;

                var head = data.AsSpan(0, Math.Min(HeadSize, outLen)).ToArray();
                var kind = EntryClassifier.Classify(head);
                long end = at + used;
                bool looksLikeStart = kind != EntryKind.Bin;
                bool resourceMisaligned = (kind == EntryKind.Resource || kind == EntryKind.RageObj) && (at & 0xFFF) != 0x10;
                bool join = cur != null && (prevFull || !looksLikeStart || resourceMisaligned);

                if (join)
                {
                    cur!.EndOffset = end; cur.InflatedSize += outLen; cur.StreamCount++;
                }
                else
                {
                    cur = new Rpf9Entry { Offset = at, EndOffset = end, InflatedSize = outLen, StreamCount = 1, Kind = kind, Head = head };
                    idx.Entries.Add(cur);
                }
                prevFull = outLen == 65536;
                idx.StreamCount++; idx.CoveredBytes += used;
                pos = Align16(end);
                advanced = true;
                break;
            }
            if (!advanced) pos += n - (n % 16 == 0 ? 0 : n % 16) + (n % 16 == 0 ? 0 : 0);
            if (!advanced && n % 16 != 0) pos = Align16(pos);

            long now = Environment.TickCount64;
            if (now - lastReport > 500)
            {
                lastReport = now;
                progress?.Report(new IndexProgress(pos, length, idx.StreamCount, idx.Entries.Count));
            }
        }
        progress?.Report(new IndexProgress(length, length, idx.StreamCount, idx.Entries.Count));
        if (idx.StreamCount == 0 && length > header.TocEnd)
        {
            double e = DataEntropy(h, header.TocEnd, length);
            idx.Warnings.Add(e >= 7.5
                ? $"Aucun flux zlib trouvé dans les {length - header.TocEnd:N0} octets de données (entropie {e:0.00}/8) : contenu probablement chiffré ou compressé autrement. L'indexation par flux zlib ne peut rien en tirer."
                : $"Aucun flux zlib trouvé dans les {length - header.TocEnd:N0} octets de données (entropie {e:0.00}/8) : contenu probablement stocké sans compression zlib ; il n'est pas indexé.");
        }
        return idx;
    }

    private static long Align16(long v) => (v + 15) & ~15L;

    /// <summary>Entropie de Shannon (0–8 bits/octet) d'un échantillon des données ; sert uniquement à qualifier un avertissement.</summary>
    private static double DataEntropy(Microsoft.Win32.SafeHandles.SafeFileHandle h, long from, long length)
    {
        int n = (int)Math.Min(1 << 20, length - from);
        if (n <= 0) return 0;
        var b = new byte[n];
        int got = RandomAccess.Read(h, b, from);
        if (got <= 0) return 0;
        var c = new long[256];
        long total = 0;
        // les plages de zéros (remplissage d'alignement) ne disent rien du contenu : on les ignore
        for (int i = 0; i < got;)
        {
            if (b[i] == 0)
            {
                int j = i; while (j < got && b[j] == 0) j++;
                if (j - i >= 32) { i = j; continue; }
            }
            c[b[i]]++; total++; i++;
        }
        if (total == 0) return 0;
        double ent = 0;
        foreach (var v in c) if (v > 0) { double p = (double)v / total; ent -= p * Math.Log2(p); }
        return ent;
    }
}
