using System.IO.Compression;
using System.Text;
using GtaAll.Core.Formats;
using GtaAll.Core.Search;
using GtaAll.Core.Sources;

int fails = 0;
void Check(bool ok, string name) { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}"); if (!ok) fails++; }

byte[] Zlib(byte[] d, CompressionLevel lvl = CompressionLevel.SmallestSize)
{ using var ms = new MemoryStream(); using (var z = new ZLibStream(ms, lvl, true)) z.Write(d); return ms.ToArray(); }

var tmp = Path.Combine(Path.GetTempPath(), "gtaall_smoke_" + Guid.NewGuid().ToString("N"));
var src = Path.Combine(tmp, "src"); var outDir = Path.Combine(tmp, "out"); var cache = Path.Combine(tmp, "cache");
Directory.CreateDirectory(src);

// Archive synthétique : en-tête + fausse TOC chiffrée + 3 fichiers
var xml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><root>" + new string('a', 3000) + "</root>");
var pso = new byte[200]; "PSIN"u8.CopyTo(pso); new Random(1).NextBytes(pso.AsSpan(4));
var big = new byte[65536 + 1000]; for (int i = 0; i < big.Length; i++) big[i] = (byte)((i * 7) % 13 + 0x80); big[0] = (byte)0x3C; // fichier xml-like en 2 flux (un "bin" pur serait collé au précédent, limite documentée de l heuristique) // "bin" en deux morceaux
var ms = new MemoryStream();
var hdr = new byte[0x40]; Rpf9Header.Magic.CopyTo(hdr, 0);
BitConverter.GetBytes(0x1000u).CopyTo(hdr, 4); BitConverter.GetBytes(3u).CopyTo(hdr, 0x24);
ms.Write(hdr); var toc = new byte[0x1000 - 0x40]; new Random(3).NextBytes(toc); for (int i = 0; i < toc.Length; i++) if (toc[i] == 0x78) toc[i] = 0; ms.Write(toc);
void Align() { while (ms.Length % 16 != 0) ms.WriteByte(0); }
long offXml = ms.Position; ms.Write(Zlib(xml)); Align();
long offPso = ms.Position; ms.Write(Zlib(pso)); Align();
long offBig = ms.Position; ms.Write(Zlib(big.AsSpan(0, 65536).ToArray())); Align(); ms.Write(Zlib(big.AsSpan(65536).ToArray())); Align();
var rpf = Path.Combine(src, "test.rpf"); File.WriteAllBytes(rpf, ms.ToArray());
File.WriteAllBytes(Path.Combine(src, "other.rpf"), new byte[100]);
File.WriteAllBytes(Path.Combine(src, "clip.bik"), new byte[10]);

// Inflater vs ZLibStream (niveaux variés)
foreach (var lvl in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize })
{
    var z = Zlib(xml, lvl);
    bool ok = ZlibInflater.TryInflate(z, 0, z.Length, 1 << 20, out var o, out int n, out int used) && n == xml.Length && o.AsSpan(0, n).SequenceEqual(xml) && used == z.Length;
    Check(ok, $"inflate {lvl}");
}

var files = SourceScanner.Scan(src);
Check(files.Count(f => f.Type == SourceType.Rpf9) == 1, "scan : 1 RPF9 reconnu");
Check(files.Count(f => f.Type == SourceType.RpfOther) == 1, "scan : RPF non-RPF9 signalé");

var hdrParsed = Rpf9Header.Parse(File.ReadAllBytes(rpf).AsSpan(0, 0x40), new FileInfo(rpf).Length);
Check(hdrParsed.TocEnd == 0x1000 && hdrParsed.EntryCountHint == 3, "en-tête");

var idx = Rpf9Indexer.Build(rpf, null, default);
Check(idx.Entries.Count == 3, $"3 entrées (obtenu {idx.Entries.Count})");
Check(idx.Entries[0].Kind == EntryKind.Xml && idx.Entries[0].Offset == offXml, "entrée xml");
Check(idx.Entries[1].Kind == EntryKind.Pso && idx.Entries[1].Offset == offPso, "entrée pso");
Check(idx.Entries[2].Kind == EntryKind.Xml && idx.Entries[2].StreamCount == 2 && idx.Entries[2].Offset == offBig, "entrée de 2 flux jointe");

var before = File.ReadAllBytes(rpf);
var p = Rpf9Exporter.Export(rpf, idx.Entries[2], outDir);
Check(File.ReadAllBytes(p).AsSpan().SequenceEqual(big), "export identique aux données d'origine");
Check(File.ReadAllBytes(rpf).AsSpan().SequenceEqual(before), "archive source inchangée");
try { Rpf9Exporter.Export(rpf, idx.Entries[0], Path.Combine(src, "sub")); Check(false, "refus export dans les sources"); }
catch (InvalidOperationException) { Check(true, "refus export dans les sources"); }

IndexCache.Save(cache, idx);
var again = IndexCache.TryLoad(cache, rpf);
Check(again != null && again.Entries.Count == 3 && again.Entries[0].Head.Length > 0, "cache aller-retour");

Check(EntryFilter.Apply(idx.Entries, new EntryQuery("xml")).Count == 2, "filtre texte");
Check(EntryFilter.Apply(idx.Entries, new EntryQuery("", EntryKind.Pso)).Count == 1, "filtre type");
Check(EntryFilter.Apply(idx.Entries, new EntryQuery(offPso.ToString("X"))).Count >= 1, "filtre offset hex");

Directory.Delete(tmp, true);
Console.WriteLine(fails == 0 ? "TOUS LES TESTS PASSENT" : $"{fails} ECHEC(S)");
return fails;
