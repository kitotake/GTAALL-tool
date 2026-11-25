# Rapport des sous-projets (`sous-tool/`)

Le dépôt ne contient que des **pointeurs Git (gitlinks, mode 160000)** sans `.gitmodules` ni code. Aucun code n'a été copié dans `sous-tool/`.
Méthode : recherche par nom et par SHA sur GitHub, puis **clone du dépôt candidat et contrôle que l'objet commit du gitlink y existe** (`git cat-file -e <sha>^{commit}`). Date de vérification : 2026-11-25.

| Sous-projet | SHA du gitlink | Dépôt(s) contenant ce commit | Commit vérifié | Statut |
|---|---|---|---|---|
| 0x52504639 | `625e2eb272` | SentinelInteractive/0x52504639 | Oui | Vérifié |
| 6stars | `480d9a4d00` | meetox80/6stars, idvwork/6stars-gta-vi-engine | Oui | **Ambigu** : même commit dans plusieurs dépôts (fork/copie), original non déterminé |
| AWC_from_RPF9_Extractor | `2e5ac99faa` | Albotterin/AWC_from_RPF9_Extractor | Oui | Vérifié |
| GTA6-Asset-Info | `bad5bc2180` | walkthestars/GTA6-Asset-Info, TrinitronClient/GTA6-Asset-Info | Oui | **Ambigu** : même commit dans plusieurs dépôts (fork/copie), original non déterminé |
| PercyStar | `6a64e53f6d` | rpatty-dev/PercyStar | Oui | Vérifié |
| RageForge | `39a9145313` | — | Non | **Introuvable** : commit absent des dépôts candidats |
| SixNG | `4a8da271d2` | holdmysocks/SixNG | Oui | Vérifié |
| XBOX-Inspector | `0a3d067f71` | kry-vex-gta/XBOX-Inspector | Oui | Vérifié |
| awesome-gta6-reverse-engineering | `fa51a4a421` | Ahmet-Dedeler/awesome-gta6-reverse-engineering | Oui | Vérifié |
| bcpack-rs | `c51ec521cb` | Ahmet-Dedeler/bcpack-rs | Oui | Vérifié |
| gta6-explorer | `d78a9a5b7f` | HolyRoses/gta6-explorer | Oui | Vérifié |
| gta6-pc-port-hub | `3a7d8d18f6` | fishingpvalues/gta6-pc-port-hub | Oui | Vérifié |
| rage-tools-vi | `51c4bd69c6` | — | Non | **Introuvable** : commit absent des dépôts candidats |
| rage9-re | `ddf30a3d14` | Ahmet-Dedeler/rage9-re | Oui | Vérifié |
| rpf9-explorer | `af54933662` | breadbbbb/rpf9-explorer | Oui | Vérifié |
| rpf9-research | `1de30d73a9` | JoshRob297/rpf9-research | Oui | Vérifié |
| ttpf-studio | `a2fe110d8e` | woreksami/ttpf-studio | Oui | Vérifié |
| vice-lens | `8b8c8a847e` | Librett0/vice-lens | Oui | Vérifié |
| xbox-hle-lab | `3f9db5566a` | sub-north/xbox-hle-lab | Oui | Vérifié |

Bilan : 15 vérifiés sans ambiguïté, 2 ambigus (6stars, GTA6-Asset-Info), 2 introuvables (RageForge, rage-tools-vi).
Seuls SixNG, gta6-explorer et rpf9-explorer ont été lus en détail (code récupéré hors dépôt). Les autres sont **vérifiés d'origine mais pas encore analysés**.

## Décisions d'intégration
- **Module TTPF** : `TtpfInspector.cs` lit l'en-tête RDS5, la table de blocs et l'index ETPF, d'après les notes de SixNG ; code C# original, aucun pixel décodé.
- **Non intégré** : le pont « décodeur local » de rpf9-explorer (lecture de l'index chiffré via l'exécutable du jeu) : contraire au choix de GTAALL-tool de ne rien déchiffrer.
- **Non fait** : décodage BCPack/Gfx10, export de modèles.
- **Licences** : SixNG est en GPL-3.0 ; porter du code impose une distribution compatible et l'attribution. Voir `THIRD_PARTY_NOTICES.md`.
