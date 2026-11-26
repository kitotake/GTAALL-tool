# Architecture

```
GtaAll.Core            noyau : Confidence, HeaderField, InspectionResult, ByteReader, SourceScanner/SourceFile,
                       ModuleCatalog, AppSettings, apparence, IFormatModule + ModuleRegistry, ILog/FileLog, SafeFileAccess
GtaAll.Archives        RPF9 : en-tête, indexeur, cache, recherche (EntryFilter), export brut, inflate zlib      -> Core
GtaAll.Textures        TTPF/RDS5 : index ETPF, disposition Gfx10, décodeurs BC1/3/4/5/7, PNG/DDS, planche-contact       -> Core
GtaAll.Audio           en-têtes WAV/Ogg/FLAC, AWC reconnu par signature                                         -> Core
GtaAll.Video           en-têtes Bink 1/2                                                                        -> Core
GtaAll.StructuredData  XML analysé, PSO/RBF reconnus par signature                                              -> Core
GtaAll.Diag            outil en ligne de commande (lecture seule) : rapport texte pour un fichier ou un dossier -> tous les modules
GtaAll.App (WPF)       interface unique ; racine de composition (App.xaml.cs) : enregistre les modules, installe le journal
```

Règles
- **Core ne dépend d'aucun module** (testé). Un module ne dépend que de Core.
- Un module implémente `IFormatModule` (extensions, `Classify`, `Inspect`) et s'enregistre dans `ModuleRegistry`.
  `SourceScanner` et `FileInspector` n'ont aucune liste de formats codée en dur.
- Sources en lecture seule ; sorties hors du dossier des sources (`SafeFileAccess`).
- Erreurs d'E/S d'inspection : converties en note affichable et écrites dans `%LOCALAPPDATA%\GTAALL-tool\logs`.
- Les namespaces n'ont pas changé (`GtaAll.Core.Formats`, `GtaAll.Core.Search`…) pour ne rien casser ; renommage possible plus tard.

Volontairement absents : `GtaAll.Models` (aucun format documenté, il serait vide) et `GtaAll.Search` (33 lignes, propres aux archives, donc dans Archives).
Ajouter un module = un projet + une classe `IFormatModule` + une ligne dans `ComposeModules()` et dans le test de composition.
