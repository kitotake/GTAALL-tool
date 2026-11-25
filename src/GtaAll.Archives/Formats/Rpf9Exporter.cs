namespace GtaAll.Core.Formats;

/// <summary>
/// Export brut d'une entrée : concaténation des flux zlib décompressés.
/// Les ressources RAGE (pages système/graphiques) ne sont PAS réassemblées ni converties à ce stade.
/// </summary>
public static class Rpf9Exporter
{
    public static string Export(string archivePath, Rpf9Entry e, string outputDir)
    {
        GtaAll.Core.IO.SafeFileAccess.EnsureOutputOutsideSources(Path.GetDirectoryName(Path.GetFullPath(archivePath))!, outputDir);
        Directory.CreateDirectory(outputDir);

        var name = $"{Path.GetFileNameWithoutExtension(archivePath)}_{e.Id}.{EntryClassifier.Extension(e.Kind)}";
        var outPath = Path.Combine(outputDir, name);

        using var h = File.OpenHandle(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buf = new byte[(int)Math.Min(e.CompressedSize + 16, 64 << 20)];
        using var o = File.Create(outPath);
        long pos = e.Offset;
        while (pos < e.EndOffset)
        {
            int n = RandomAccess.Read(h, buf.AsSpan(0, (int)Math.Min(buf.Length, e.EndOffset - pos + 16)), pos);
            if (!ZlibInflater.TryInflate(buf, 0, n, 256 << 20, out var data, out int len, out int used))
                throw new InvalidDataException($"Flux zlib invalide à 0x{pos:X}.");
            o.Write(data, 0, len);
            pos = (pos + used + 15) & ~15L;
        }
        return outPath;
    }
}
