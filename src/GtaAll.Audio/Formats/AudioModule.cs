using GtaAll.Core.Modules;
using GtaAll.Core.Settings;
using GtaAll.Core.Sources;

namespace GtaAll.Core.Formats;

public sealed class AudioModule : IFormatModule
{
    public ModuleId Id => ModuleId.Audio;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".awc", ".wav", ".ogg" };
    public SourceClassification Classify(string path, string extension) => new(SourceType.Audio, "Audio : inspection de l'en-tête disponible (pas de lecture)");
    public InspectionResult? Inspect(SourceFile file) => AudioInspector.Inspect(ByteReader.Head(file.Path, 4096), file.Size);
}
