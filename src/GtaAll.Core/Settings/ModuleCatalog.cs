using GtaAll.Core.Sources;

namespace GtaAll.Core.Settings;

public enum ModuleId { Rpf9, Ttpf, Audio, Video, Models, Structured }

/// <summary>Un module fonctionnel. <see cref="Implemented"/> ne passe à true que lorsque le code existe réellement.</summary>
public sealed record ModuleInfo(ModuleId Id, string Name, string Description, bool Implemented);

public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleInfo> All = new[]
    {
        new ModuleInfo(ModuleId.Rpf9, "Archives RPF9",
            "Inspection de l'en-tête, indexation par flux zlib, recherche, export brut.", true),
        new ModuleInfo(ModuleId.Ttpf, "Textures TTPF",
            "Analyse TTPF/RDS5 et prévisualisation des textures (BCPack inclus).", false),
        new ModuleInfo(ModuleId.Audio, "Audio", "Lecture et export des flux audio (AWC).", false),
        new ModuleInfo(ModuleId.Video, "Vidéo", "Lecture et export des vidéos (Bink).", false),
        new ModuleInfo(ModuleId.Models, "Modèles", "Prévisualisation et export des modèles 3D.", false),
        new ModuleInfo(ModuleId.Structured, "Données structurées", "PSO, RBF et autres données structurées.", false),
    };

    public static ModuleInfo Get(ModuleId id) => All.First(m => m.Id == id);

    /// <summary>Module qui « possède » un type de fichier source ; null si aucun.</summary>
    public static ModuleId? ForSourceType(SourceType t) => t switch
    {
        SourceType.Rpf9 or SourceType.RpfOther => ModuleId.Rpf9,
        SourceType.Ttpf or SourceType.Texture => ModuleId.Ttpf,
        SourceType.Audio => ModuleId.Audio,
        SourceType.Video => ModuleId.Video,
        SourceType.Structured => ModuleId.Structured,
        _ => null
    };
}
