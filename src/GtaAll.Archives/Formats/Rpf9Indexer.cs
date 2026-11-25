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
        return idx;
    }

    private static long Align16(long v) => (v + 15) & ~15L;
}
