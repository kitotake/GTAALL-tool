# Sources de référence

- **SixNG** (holdmysocks/SixNG, GPL-3.0, commit 4a8da271d21305d19388cbc1091e9d6d83a2db24) : description du format TTPF/RDS5 (crates/sixng-core/src/ttpf.rs). `TtpfInspector.cs` est une réécriture C# d'après cette description ; aucun code Rust n'est copié.
- **gta6-explorer** (HolyRoses/gta6-explorer, MIT, commit d78a9a5b7f26739b54c56e198c00204a341ed113) : recoupement des notes de format (RDS5/TTPF).
- **GTA6-Asset-Info** (GPL-3.0) : notes RPF9 citées dans le README ; origine du dépôt non vérifiée (voir docs/SOUS-PROJETS.md).
- **texture2ddecoder** (Python, utilisé uniquement pour valider les décodeurs BC pendant le développement ; non distribué avec GTAALL-tool). Les tables de partition BC7 de `BcDecoder.cs` en ont été dérivées par sondage de blocs ; les autres tables suivent la spécification publique Direct3D.
- **SixNG / gfx10.rs** (GPL-3.0) : description de la disposition Gfx10 (équations de bits, mip tail) réécrite en C# dans `Gfx10Layout.cs`.
