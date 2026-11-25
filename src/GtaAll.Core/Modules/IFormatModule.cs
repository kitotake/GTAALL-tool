using GtaAll.Core.Formats;
using GtaAll.Core.Settings;
using GtaAll.Core.Sources;

namespace GtaAll.Core.Modules;

/// <summary>Résultat de la reconnaissance d'un fichier par un module.</summary>
public sealed record SourceClassification(SourceType Type, string Status);

/// <summary>
/// Contrat commun des modules de format. Un module déclare les extensions qu'il reconnaît, classe un fichier
/// (lecture de quelques octets autorisée) et l'inspecte en lecture seule. Il ne doit jamais modifier une source.
/// </summary>
public interface IFormatModule
{
    ModuleId Id { get; }
    IReadOnlyCollection<string> Extensions { get; }
    SourceClassification Classify(string path, string extension);
    /// <summary>Inspecte un fichier déjà classé ; null si ce module ne l'inspecte pas.</summary>
    InspectionResult? Inspect(SourceFile file);
}

/// <summary>Registre des modules. L'application l'assemble au démarrage ; Core ne connaît aucun module.</summary>
public sealed class ModuleRegistry
{
    public static ModuleRegistry Default { get; } = new();

    private readonly List<IFormatModule> _modules = new();
    private readonly Dictionary<string, IFormatModule> _byExt = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public IReadOnlyList<IFormatModule> Modules { get { lock (_lock) return _modules.ToArray(); } }

    /// <summary>Enregistre un module. Idempotent par identifiant ; un conflit d'extension est refusé (pas de remplacement silencieux).</summary>
    public void Register(IFormatModule m)
    {
        lock (_lock)
        {
            if (_modules.Any(x => x.Id == m.Id)) return;
            foreach (var e in m.Extensions)
                if (_byExt.TryGetValue(e, out var other))
                    throw new InvalidOperationException($"Extension {e} déjà prise par le module {other.Id}.");
            _modules.Add(m);
            foreach (var e in m.Extensions) _byExt[e] = m;
        }
    }

    public IFormatModule? ForExtension(string ext) { lock (_lock) return _byExt.TryGetValue(ext, out var m) ? m : null; }
    public IFormatModule? ForType(SourceType t) { lock (_lock) return _modules.FirstOrDefault(m => ModuleCatalog.ForSourceType(t) == m.Id); }
}
