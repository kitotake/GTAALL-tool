using GtaAll.Core.Modules;
using GtaAll.Core.Settings;
using GtaAll.Core.Sources;

namespace GtaAll.Core.Formats;

public sealed class StructuredDataModule : IFormatModule
{
    public ModuleId Id => ModuleId.Structured;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".xml", ".pso", ".rbf" };
    public SourceClassification Classify(string path, string extension) => new(SourceType.Structured, "Données structurées : analyse disponible");
    public InspectionResult? Inspect(SourceFile file) => StructuredInspector.Inspect(file.Path, ByteReader.Head(file.Path, 4096), file.Size);
}
