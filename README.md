# GTAALL-tool

Application Windows (C# / .NET 8 / WPF) pour explorer, rechercher et exporter les ressources
d'**une copie du jeu que vous possédez**, en **lecture seule**. Aucune dépendance Python, aucun lanceur
d'outils tiers : les fonctions sont réimplémentées dans `GtaAll.Core`.

## État réel (bilan honnête — projet INCOMPLET)

| Fonction | État |
|---|---|
| Ouvrir un dossier, lister les fichiers reconnus, filtrer | Implémenté (Core testé ; interface **non compilée**, voir plus bas) |
| En-tête RPF9 avec niveau de certitude par champ | Implémenté, testé sur archive synthétique |
| Indexation RPF9 par flux zlib (carving), jointure des flux d'une entrée | Implémenté, testé sur archive **synthétique** uniquement |
| Recherche (type, offset hex) | Implémenté |
| Export brut d'une entrée (flux décompressés concaténés) | Implémenté, testé ; refuse un dossier dans les sources |
| Cache d'index dans un dossier choisi | Implémenté |
| Noms de fichiers RPF9 | **Impossible** : la TOC est chiffrée, la clé n'est pas dans les données du jeu. Aucun déchiffrement n'est tenté. |
| Passe de noms `name.#xx` dans les pages système | Non fait |
| TTPF / RDS5, textures dictionary, tiling Xbox, BCPack | Non fait (BCPack : décodage encore incertain même dans la doc) |
| Audio (AWC), vidéo (Bink), modèles, PSO/RBF structurés | Non fait (reconnus dans la liste, pas lus) |
| Réassemblage des pages de ressources RAGE | Non fait |

## Limites connues
- **Jamais testé sur une vraie archive** : aucune donnée du jeu n'était disponible. Les performances sur de gros fichiers (> 50 Go) sont inconnues ; l'indexeur est monothread.
- L'heuristique de jointure (doc RPF9) colle un fichier « bin » à l'entrée précédente ; la classification est approximative.
- Connaissance des formats : notes communautaires GTA6-Asset-Info (GPL-3.0), « rien n'est confirmé par le code du jeu ». Une seule version de build documentée.
- Seuls 2 des 19 dépôts de référence ont été lus (GTA6-Asset-Info, rpf9-research : clonés, GPL-3.0). Les 17 autres restent à étudier, licences comprises, avant toute reprise de code. Le code actuel est original.

## Prérequis et lancement (Windows)
- Windows 10/11, .NET 8 SDK
```
dotnet build GtaAllTool.sln
dotnet run --project src/GtaAll.App
```
Tests du cœur (multiplateforme) :
```
dotnet run --project tests/GtaAll.Core.SmokeTests
```

## Architecture
- `src/GtaAll.Core` : `Formats` (zlib, RPF9, index, export), `Sources` (scan), `Search` (filtre).
- `src/GtaAll.App` : WPF, code-behind simple. Prochains modules prévus : Textures, Audio, Video, Models, Structured.

## Usage légitime
Travaillez uniquement avec vos propres fichiers. Ne redistribuez pas les ressources extraites.
