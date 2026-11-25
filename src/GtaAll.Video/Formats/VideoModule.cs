using GtaAll.Core.Modules;
using GtaAll.Core.Settings;
using GtaAll.Core.Sources;

namespace GtaAll.Core.Formats;

public sealed class VideoModule : IFormatModule
{
    public ModuleId Id => ModuleId.Video;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".bik", ".bk2" };
    public SourceClassification Classify(string path, string extension) => new(SourceType.Video, "Vidéo : inspection de l'en-tête disponible (pas de lecture)");
    public InspectionResult? Inspect(SourceFile file)
    {
        var head = ByteReader.Head(file.Path, 4096);
        if (BinkInspector.IsBink(head)) return BinkInspector.Inspect(head, file.Size);
        return new(new[] { new HeaderField(0, Math.Min(4, head.Length), "premiers octets", Convert.ToHexString(head.AsSpan(0, Math.Min(4, head.Length))), Confidence.Unknown) },
                   new[] { "Vidéo non Bink : non analysée." });
    }
}
