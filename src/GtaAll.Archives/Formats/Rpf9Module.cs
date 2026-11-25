using GtaAll.Core.Modules;
using GtaAll.Core.Settings;
using GtaAll.Core.Sources;

namespace GtaAll.Core.Formats;

public sealed class Rpf9Module : IFormatModule
{
    public ModuleId Id => ModuleId.Rpf9;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".rpf" };

    public SourceClassification Classify(string path, string extension)
    {
        var b = new byte[4];
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int n = s.Read(b, 0, 4);
            return n == 4 && Rpf9Header.HasMagic(b)
                ? new(SourceType.Rpf9, "RPF9 : inspection et indexation disponibles")
                : new(SourceType.RpfOther, "RPF d'une autre version ou illisible : non pris en charge");
        }
        catch (IOException) { return new(SourceType.RpfOther, "Illisible"); }
    }

    /// <summary>L'en-tête et l'index RPF9 passent par <see cref="Rpf9Header"/> / <see cref="Rpf9Indexer"/>, pas par l'inspection générique.</summary>
    public InspectionResult? Inspect(SourceFile file) => null;
}
