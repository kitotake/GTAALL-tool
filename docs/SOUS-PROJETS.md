# Rapport des sous-projets (`sous-tool/`)

Le dépôt ne contient que des **pointeurs Git (gitlinks, mode 160000)** sans `.gitmodules` ni code. Aucun code n'a été copié dans `sous-tool/`.
Méthode : recherche GitHub par nom exact, puis `GET /repos/{dépôt}/commits/{SHA}` avec le SHA du gitlink. Un dépôt n'est « vérifié » que si ce commit exact y existe.

| Sous-projet | SHA du gitlink | Dépôt candidat | Commit vérifié | Code récupéré | Intégré | Statut |
|---|---|---|---|---|---|---|
| 0x52504639 | `625e2eb272` | SentinelInteractive/0x52504639 | Non | Non | Non | Origine non confirmée |
| 6stars | `480d9a4d00` | meetox80/6stars, ViNguyenThanh/6stars, Druxys/6stars, ThanhViViolet/6stars | Non | Non | Non | Origine non confirmée |
| AWC_from_RPF9_Extractor | `2e5ac99faa` | Albotterin/AWC_from_RPF9_Extractor | Non | Non | Non | Origine non confirmée |
| GTA6-Asset-Info | `bad5bc2180` | TrinitronClient/GTA6-Asset-Info, walkthestars/GTA6-Asset-Info | Non | Non | Non | Origine non confirmée |
| PercyStar | `6a64e53f6d` | rpatty-dev/PercyStar | Non | Non | Non | Origine non confirmée |
| RageForge | `39a9145313` | zsy-miao/RageForge, SimpleX-T/RageForge, AshishSMehra/RageForge, SommerChase/rageforge | Non | Non | Non | Origine non confirmée |
| SixNG | `4a8da271d2` | holdmysocks/SixNG | **Oui** | Oui (hors dépôt, lecture) | Oui, comme référence de format (TTPF) | Vérifié |
| XBOX-Inspector | `0a3d067f71` | kry-vex-gta/XBOX-Inspector | Non | Non | Non | Origine non confirmée |
| awesome-gta6-reverse-engineering | `fa51a4a421` | Ahmet-Dedeler/awesome-gta6-reverse-engineering | Non | Non | Non | Origine non confirmée |
| bcpack-rs | `c51ec521cb` | Ahmet-Dedeler/bcpack-rs | Non | Non | Non | Origine non confirmée |
| gta6-explorer | `d78a9a5b7f` | HolyRoses/gta6-explorer | **Oui** | Oui (hors dépôt, lecture) | Non (référence) | Vérifié |
| gta6-pc-port-hub | `3a7d8d18f6` | fishingpvalues/gta6-pc-port-hub | Non | Non | Non | Origine non confirmée |
| rage-tools-vi | `51c4bd69c6` | lynxnathan/rage-tools-vi | Non | Non | Non | Origine non confirmée |
| rage9-re | `ddf30a3d14` | Ahmet-Dedeler/rage9-re | Non | Non | Non | Origine non confirmée |
| rpf9-explorer | `af54933662` | breadbbbb/rpf9-explorer | **Oui** | Oui (hors dépôt, lecture) | Non (voir ci-dessous) | Vérifié |
| rpf9-research | `1de30d73a9` | JoshRob297/rpf9-research | Non | Non | Non | Origine non confirmée |
| ttpf-studio | `a2fe110d8e` | woreksami/ttpf-studio | Non | Non | Non | Origine non confirmée |
| vice-lens | `8b8c8a847e` | Librett0/vice-lens | Non | Non | Non | Origine non confirmée |
| xbox-hle-lab | `3f9db5566a` | sub-north/xbox-hle-lab | Non | Non | Non | Origine non confirmée |

## Pourquoi « non confirmée »
Pour 16 sous-projets, un dépôt du même nom existe, mais le SHA du gitlink n'y figure pas (commit supprimé, dépôt privé, fork non relié, ou projet sans rapport). Conformément à la consigne, aucun n'a été substitué ni utilisé. **Action requise** : donner l'URL exacte de chaque dépôt, ou lancer sur ton poste `git submodule status` / lire `.git/modules` pour retrouver les URL d'origine.

## Analyse des 3 projets vérifiés
- **SixNG** (Rust, GPL-3.0) : Extracteur RPF9 + docs de format (TTPF/RDS5, BCPack, PSO, AWC, Bink). Ne déchiffre pas la TOC.
- **gta6-explorer** (Python, MIT) : Explorateur de paquet : textures DDS, AWC, Bink2, PSIN. Ne déchiffre pas l'index.
- **rpf9-explorer** (C# / WPF, (voir dépôt)) : Visionneuse Windows. Mode « indexé » via un script externe qui lit l'exécutable du jeu : NON intégré.

## Décisions d'intégration
- **Module TTPF** : `Formats/TtpfInspector.cs` lit l'en-tête RDS5, la table de blocs et l'index ETPF (textures, hash, dimensions, format DXGI). Disposition d'après les notes de SixNG ; code C# original. **Aucun pixel décodé** (ni BCPack, ni Gfx10).
- **Non intégré** : le pont « décodeur local » de rpf9-explorer, conçu pour lire l'index chiffré à partir de l'exécutable du jeu, contredit le choix de GTAALL-tool (aucun déchiffrement). Même raison pour toute extraction de clés.
- **Non intégré** : décodeurs BCPack (SixNG/bcpack-rs), désentrelacement Gfx10, export de modèles : gros portages Rust→C#, non faits, donc **Textures = index seulement** et **Modèles = À venir**.
- **Licences** : SixNG est en GPL-3.0 ; si du code en est porté un jour, GTAALL-tool devra être distribué sous GPL-3.0 compatible et conserver l'attribution. Voir `THIRD_PARTY_NOTICES.md`.
