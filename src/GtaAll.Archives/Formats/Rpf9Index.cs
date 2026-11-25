using System.Text.Json;
using System.Text.Json.Serialization;

namespace GtaAll.Core.Formats;

/// <summary>Une entrée « carvée » : un ou plusieurs flux zlib contigus. Pas de nom (TOC chiffrée).</summary>
public sealed class Rpf9Entry
{
    public long Offset { get; set; }
    public long EndOffset { get; set; }
    public long InflatedSize { get; set; }
    public int StreamCount { get; set; }
    public EntryKind Kind { get; set; }
    public byte[] Head { get; set; } = Array.Empty<byte>();
    [JsonIgnore] public long CompressedSize => EndOffset - Offset;
    [JsonIgnore] public string Id => $"{Offset:X10}";
}

public sealed class Rpf9Index
{
    public string ArchivePath { get; set; } = "";
    public long ArchiveLength { get; set; }
    public long LastWriteTicks { get; set; }
    public long StreamCount { get; set; }
    public long CoveredBytes { get; set; }
    public List<Rpf9Entry> Entries { get; set; } = new();
    public List<HeaderFieldDto> HeaderFields { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public sealed record HeaderFieldDto(int Offset, int Size, string Name, string Value, Confidence Confidence);
}

/// <summary>Cache JSON dans un dossier choisi par l'utilisateur (jamais à côté des sources).</summary>
public static class IndexCache
{
    private static string FileFor(string cacheDir, string archivePath)
    {
        var fi = new FileInfo(archivePath);
        var key = $"{fi.FullName.ToLowerInvariant()}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(cacheDir, "index", $"{Path.GetFileNameWithoutExtension(fi.Name)}_{hash}.rpf9index.json");
    }

    public static Rpf9Index? TryLoad(string cacheDir, string archivePath)
    {
        var f = FileFor(cacheDir, archivePath);
        if (!File.Exists(f)) return null;
        try { using var s = File.OpenRead(f); return JsonSerializer.Deserialize<Rpf9Index>(s); }
        catch (JsonException) { return null; }
    }

    public static void Save(string cacheDir, Rpf9Index idx)
    {
        var f = FileFor(cacheDir, idx.ArchivePath);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        var tmp = f + ".tmp";
        using (var s = File.Create(tmp)) JsonSerializer.Serialize(s, idx);
        File.Move(tmp, f, true);
    }
}
