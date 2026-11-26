# GTAALL-tool

Application Windows (C# / .NET 8 / WPF) pour explorer, rechercher et exporter les ressources d'**une copie du jeu que vous possédez**, en **lecture seule**. Aucune dépendance Python ; aucun lanceur d'outils tiers : les fonctions sont réimplémentées dans `GtaAll.Core`.

> **Projet incomplet.** Cinq modules sont implémentés (Archives RPF9, Textures TTPF, Audio, Vidéo, Données structurées), les quatre derniers en **inspection d'en-tête/index uniquement** (pas de lecture ni de décodage).

## Prérequis et lancement (Windows 10/11)
- .NET 8 SDK (avec le workload « .NET desktop »).
```
dotnet build GtaAllTool.sln
dotnet run --project src/GtaAll.App
dotnet run --project tests/GtaAll.Core.SmokeTests
```
La solution contient 3 projets : `GtaAll.Core`, `GtaAll.App` (WPF), `GtaAll.Core.SmokeTests`.

## État de la vérification
| Élément | État |
|---|---|
| `GtaAll.Core` + tests (61 vérifications) | **Compilé et exécuté** : 0 avertissement, 0 erreur, 61/61 OK (Linux, SDK .NET 8.0.134, restauration hors ligne) |
| `GtaAll.App` (WPF, thème, assistant, paramètres) | **Compilé sous Windows** (poste du propriétaire, après correction de `CS0104` `GradientStop` et de `using System.IO` manquants) : les artefacts versionnés (`9f7b82c`) contiennent un DLL avec `SettingsWindow`, `WelcomeWindow`, `ThemeManager`, et le BAML de tous les écrans et thèmes. **Non reproduit dans l'environnement d'analyse** (Linux : `NU1101 Microsoft.WindowsDesktop.App.Ref`, NuGet inaccessible). **Jamais lancé ni vérifié visuellement** à notre connaissance. |
| `GtaAll.App` : contrôles statiques | Analyse statique, **pas une compilation WPF** : XAML bien formé ; toutes les clés `StaticResource`/`DynamicResource` résolues ; gestionnaires et `x:Name` cohérents avec les codes-behind ; le code-behind, compilé contre des types WPF *simulés*, ne montre plus d'erreur (ce contrôle avait manqué `System.IO`, que seule la compilation Windows a révélée : il est peu fiable). |
| Test sur de vraies archives du jeu | Jamais fait (archives synthétiques uniquement) |
| Lancement et contrôle visuel (accueil, principal, Paramètres) | **Jamais fait** : à faire sous Windows (voir ci-dessous) |

## Fonctions
| Fonction | État |
|---|---|
| 4 thèmes stables : Charbon + turquoise (thème d'origine, couleurs inchangées), Charbon + bleu pétrole, Noir OLED, Blanc | Palettes testées (contrastes WCAG) ; rendu non vérifié visuellement |
| Style « Liquid Glass (Bêta) » : surfaces translucides, reflets, fond de fenêtre Mica (Windows 11 22H2+) | Écrit, **jamais affiché** ; voir « Apparence » ci-dessous |
| Section Apparence (Paramètres) : thème, style, accent, transparence, suivi de Windows, mode sans transparence, retour à l'apparence précédente | Logique testée (Core) ; fenêtre non lancée |
| Assistant de bienvenue au premier démarrage (3 étapes : modules, ouverture/indexation, cache et exports), ignorable | Écrit, non lancé |
| Paramètres : modules, dossier cache/export, relance de l'assistant | Écrit, non lancé |
| Préférences locales (`%APPDATA%\GTAALL-tool\settings.json`) | Implémenté et testé (Core), y compris listes de modules inconnues/`null`/doublons (qui faisaient planter le chargement) et enregistrement transactionnel (`ApplyAndSave` : écriture impossible → réglages en mémoire rétablis) |
| Modules affichés selon les préférences ; modules non implémentés marqués « À venir », case désactivée | Logique testée (Core) ; interface non lancée |
| Dossier cache/export par défaut `%LOCALAPPDATA%\GTAALL-tool\work` ; refusé s'il est dans les sources | Implémenté et testé (Core) |
| Liste des fichiers reconnus, filtre, en-tête RPF9 avec certitude, indexation (progression, annulation), cache d'index, recherche, export brut | Implémenté ; Core testé |

## Apparence
- **Deux axes indépendants** : le *thème* (couleurs seules) et le *style* (surfaces : Standard ou Liquid Glass). L'*accent* se surcharge par-dessus le thème (7 préréglages ou `#RRGGBB`, ajusté automatiquement s'il est illisible).
- **Architecture** : `GtaAll.Core/Appearance` (catalogue, préférences, résolveur, testés) → `ThemeManager` (WPF) remplace les brosses de `Application.Resources` ; `Themes/Controls.xaml` ne contient plus aucune couleur et n'utilise que `DynamicResource` : le changement est immédiat, sans redémarrage.
- **Liquid Glass (Bêta)** : badge BÊTA et avertissement dans les Paramètres ; jamais activé automatiquement ; incompatible avec Noir OLED (désactivé dans la liste). Transparence réglable de 0 à 50 % (les zones de données restent quasi opaques, les listes déroulantes opaques).
- **Flou** : fond Mica de Windows 11 22H2+ (build ≥ 22621) uniquement. Ailleurs : surfaces translucides **sans flou** sur un dégradé. Pas de flou par panneau (impossible nativement en WPF).
- **Sans transparence** : option manuelle, et repli automatique si Windows désactive les effets de transparence ; le contraste élevé de Windows impose le style Standard.
- **Retrait de la bêta** : thème et style sont enregistrés sous forme de clés texte. Si le style n'existe plus, il est remplacé par Standard au chargement (message affiché), le reste des préférences est conservé.
- **Retour en arrière** : aperçu en direct dans les Paramètres, « Annuler » (ou fermer) restaure l'apparence enregistrée ; « Revenir à l'apparence précédente » recharge la précédente.
- **Suivre Windows** : sombre → thème sombre choisi, clair → Blanc ; réévalué quand Windows change (événement système, non testé ici).

## À vérifier sous Windows (non fait)
1. `dotnet build GtaAllTool.sln` : recompiler avec ces correctifs (ils n'ont été compilés que sous Linux pour le cœur).
2. Lancer l'application : accueil (assistant), fenêtre principale, Paramètres.
3. Paramètres : changer thème/style/accent, **Annuler** et **croix** doivent restaurer l'apparence enregistrée ; Liquid Glass désactivé par défaut, marqué BÊTA, grisé avec Noir OLED ; Mica sur Windows 11 22H2+, message de repli ailleurs.
4. `dotnet run --project tests/GtaAll.Core.SmokeTests` : doit afficher `TOUS LES TESTS PASSENT`.

## Modules

| Module                | État                                                                                                        |
| --------------------- | ----------------------------------------------------------------------------------------------------------- |
| Archives RPF9         | Disponible (indexation, recherche, export brut)                                                             |
| Vidéo                 | Disponible : en-tête Bink 1/2 (dimensions, images, cadence, pistes). Pas de lecture. Testé sur données synthétiques |
| Audio                 | Disponible : en-tête WAV/Ogg/FLAC ; AWC reconnu par signature seulement (certitude « Inferred »). Pas de lecture |
| Données structurées   | Disponible : XML analysé (DTD refusée) ; PSO/RBF reconnus par signature, export brut                        |
| Textures TTPF         | Disponible : index RDS5/ETPF + **aperçu et export PNG/DDS (mip 0)** pour BC1, BC3, BC4, BC5, BC7, RGBA8, BGRA8, R8. Non géré : blocs BCPack, BC2, BC6H, formats 16 bits, cubes/tableaux/3D, autres mips |
| Modèles               | **À venir** : aucun format documenté, non activable                                                         |

## Limites
- Noms de fichiers RPF9 indisponibles : la table des matières est chiffrée, aucun déchiffrement n'est tenté.
- Les ressources RAGE ne sont pas réassemblées ; l'export est brut. Heuristique de regroupement approximative (un fichier « bin » pur est collé à l'entrée précédente).
- Indexeur monothread, performances sur de grosses archives inconnues.
- Formats d'après des notes communautaires (GTA6-Asset-Info, GPL-3.0), non confirmées par le code du jeu. Seuls 2 des 19 dépôts de référence ont été lus ; le code est original.

## Aperçu des textures

- Dans l'application : sélectionner un `.ttpf` puis **Aperçu des textures** (60 premières textures décodées, planche ouverte dans le navigateur ; fichier écrit dans `%TEMP%\GTAALL-tool`).
- En ligne de commande : `gtaall-diag fichier.ttpf --apercu page.html [--max N]` ou `--export dossier [--max N] [--hash HEX]` (PNG + DDS).
- Validation : décodeurs BC4/BC5/BC7 identiques à un décodeur de référence indépendant ; disposition Gfx10 vérifiée sur un vrai magasin (taille calculée = écart avec la texture suivante pour 1 741 textures sur 1 741 ; mip 1 = mip 0 réduite). Les blocs BCPack (11 textures sur 1 742 dans le fichier testé) ne sont pas décodés : la texture est alors refusée, jamais affichée fausse.

## Architecture
- `src/GtaAll.Core` : `Formats`, `Sources`, `Search`, `Settings` (catalogue de modules, préférences).
- `src/GtaAll.App` : `MainWindow`, `WelcomeWindow`, `SettingsWindow`, `ModuleUi`, `ThemeManager`, `WindowBackdrop`, `SystemInfo`, `Themes/Controls.xaml`. `Themes/Dark.xaml` est l'ancien thème (couleurs d'origine), **plus référencé** : à supprimer après une première compilation Windows réussie.

## Usage légitime
Travaillez uniquement avec vos propres fichiers. Ne redistribuez pas les ressources extraites.

## Architecture

Voir `docs/ARCHITECTURE.md` (projets par module, registre `IFormatModule`, journal).

## Sous-projets

Voir `docs/SOUS-PROJETS.md` (état de vérification des 19 entrées de `sous-tool/`) et `THIRD_PARTY_NOTICES.md`.
