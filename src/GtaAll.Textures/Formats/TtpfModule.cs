using GtaAll.Core.Modules;
using GtaAll.Core.Settings;
using GtaAll.Core.Sources;

namespace GtaAll.Core.Formats;

public sealed class TtpfModule : IFormatModule
{
    public ModuleId Id => ModuleId.Ttpf;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".ttpf", ".dds" };
    public SourceClassification Classify(string path, string extension) => extension.Equals(".dds", StringComparison.OrdinalIgnoreCase)
        ? new(SourceType.Texture, "Reconnu — prévisualisation pas encore implémentée")
        : new(SourceType.Ttpf, "TTPF : lecture de l'index disponible (pas de décodage des pixels)");
    public InspectionResult? Inspect(SourceFile file) => file.Type == SourceType.Ttpf ? TtpfInspector.Inspect(file.Path, file.Size) : null;
}
