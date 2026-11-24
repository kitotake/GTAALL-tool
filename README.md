# GTAALL-tool

Application Windows (C# / .NET 8 / WPF) pour explorer, rechercher et exporter les ressources d'**une copie du jeu que vous possédez**, en **lecture seule**. Aucune dépendance Python ; aucun lanceur d'outils tiers : les fonctions sont réimplémentées dans `GtaAll.Core`.

> **Projet incomplet.** Un seul module est implémenté (Archives RPF9).

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
| `GtaAll.Core` + tests (56 vérifications) | Compilé et exécuté : tout passe (sous Linux, .NET 8) |
| `GtaAll.App` (WPF, thème, assistant, paramètres) | **Jamais compilé ni lancé** : l'environnement de développement n'a pas le SDK Windows Desktop. Vérifié seulement : XAML bien formé, gestionnaires et ressources référencés présents, aucune erreur de syntaxe C#. Des erreurs de compilation ou d'affichage restent possibles : à signaler. |
| Test sur de vraies archives du jeu | Jamais fait (archives synthétiques uniquement) |

## Fonctions
| Fonction | État |
|---|---|
| 4 thèmes stables : Charbon + turquoise (thème d'origine, couleurs inchangées), Charbon + bleu pétrole, Noir OLED, Blanc | Palettes testées (contrastes WCAG) ; rendu non vérifié visuellement |
| Style « Liquid Glass (Bêta) » : surfaces translucides, reflets, fond de fenêtre Mica (Windows 11 22H2+) | Écrit, **jamais affiché** ; voir « Apparence » ci-dessous |
| Section Apparence (Paramètres) : thème, style, accent, transparence, suivi de Windows, mode sans transparence, retour à l'apparence précédente | Logique testée (Core) ; fenêtre non lancée |
| Assistant de bienvenue au premier démarrage (3 étapes : modules, ouverture/indexation, cache et exports), ignorable | Écrit, non lancé |
| Paramètres : modules, dossier cache/export, relance de l'assistant | Écrit, non lancé |
| Préférences locales (`%APPDATA%\GTAALL-tool\settings.json`) | Implémenté et testé (Core) |
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

## Modules
| Module | État |
|---|---|
| Archives RPF9 | Disponible |
| Textures TTPF, Audio, Vidéo, Modèles, Données structurées | **À venir** : non implémentés, non activables |

## Limites
- Noms de fichiers RPF9 indisponibles : la table des matières est chiffrée, aucun déchiffrement n'est tenté.
- Les ressources RAGE ne sont pas réassemblées ; l'export est brut. Heuristique de regroupement approximative (un fichier « bin » pur est collé à l'entrée précédente).
- Indexeur monothread, performances sur de grosses archives inconnues.
- Formats d'après des notes communautaires (GTA6-Asset-Info, GPL-3.0), non confirmées par le code du jeu. Seuls 2 des 19 dépôts de référence ont été lus ; le code est original.

## Architecture
- `src/GtaAll.Core` : `Formats`, `Sources`, `Search`, `Settings` (catalogue de modules, préférences).
- `src/GtaAll.App` : `MainWindow`, `WelcomeWindow`, `SettingsWindow`, `ModuleUi`, `Themes/Dark.xaml`.

## Usage légitime
Travaillez uniquement avec vos propres fichiers. Ne redistribuez pas les ressources extraites.
