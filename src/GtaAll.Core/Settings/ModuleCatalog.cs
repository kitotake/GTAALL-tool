using GtaAll.Core.Sources;

namespace GtaAll.Core.Settings;

/// <summary>
/// Identifiants de modules. Les valeurs sont enregistrées EN NOMBRE dans settings.json : n'ajouter qu'à la fin,
/// ne jamais réordonner ni renuméroter, sinon les anciens fichiers activeraient un autre module.
/// </summary>
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
            "Lecture de l'index des magasins TTPF/RDS5 (blocs, textures, formats). Pas de décodage ni de prévisualisation.", true),
        new ModuleInfo(ModuleId.Audio, "Audio", "Inspection des en-têtes audio (AWC, WAV, Ogg). Pas de lecture.", true),
        new ModuleInfo(ModuleId.Video, "Vidéo", "Inspection des en-têtes vidéo Bink (BIK/KB2). Pas de lecture.", true),
        new ModuleInfo(ModuleId.Models, "Modèles", "Prévisualisation et export des modèles 3D.", false),
        new ModuleInfo(ModuleId.Structured, "Données structurées", "Analyse XML ; PSO et RBF reconnus par signature (export brut).", true),
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
