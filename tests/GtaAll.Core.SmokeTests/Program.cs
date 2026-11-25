using System.IO.Compression;
using System.Text;
using GtaAll.Core.Formats;
using GtaAll.Core.Search;
using GtaAll.Core.Appearance;
using GtaAll.Core.Settings;
using GtaAll.Core.Sources;

int fails = 0;
void Check(bool ok, string name) { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}"); if (!ok) fails++; }

byte[] Zlib(byte[] d, CompressionLevel lvl = CompressionLevel.SmallestSize)
{ using var ms = new MemoryStream(); using (var z = new ZLibStream(ms, lvl, true)) z.Write(d); return ms.ToArray(); }

var tmp = Path.Combine(Path.GetTempPath(), "gtaall_smoke_" + Guid.NewGuid().ToString("N"));
var src = Path.Combine(tmp, "src"); var outDir = Path.Combine(tmp, "out"); var cache = Path.Combine(tmp, "cache");
Directory.CreateDirectory(src);

// Archive synthétique : en-tête + fausse TOC chiffrée + 3 fichiers
var xml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><root>" + new string('a', 3000) + "</root>");
var pso = new byte[200]; "PSIN"u8.CopyTo(pso); new Random(1).NextBytes(pso.AsSpan(4));
var big = new byte[65536 + 1000]; for (int i = 0; i < big.Length; i++) big[i] = (byte)((i * 7) % 13 + 0x80); big[0] = (byte)0x3C; // fichier xml-like en 2 flux (un "bin" pur serait collé au précédent, limite documentée de l heuristique) // "bin" en deux morceaux
var ms = new MemoryStream();
var hdr = new byte[0x40]; Rpf9Header.Magic.CopyTo(hdr, 0);
BitConverter.GetBytes(0x1000u).CopyTo(hdr, 4); BitConverter.GetBytes(3u).CopyTo(hdr, 0x24);
ms.Write(hdr); var toc = new byte[0x1000 - 0x40]; new Random(3).NextBytes(toc); for (int i = 0; i < toc.Length; i++) if (toc[i] == 0x78) toc[i] = 0; ms.Write(toc);
void Align() { while (ms.Length % 16 != 0) ms.WriteByte(0); }
long offXml = ms.Position; ms.Write(Zlib(xml)); Align();
long offPso = ms.Position; ms.Write(Zlib(pso)); Align();
long offBig = ms.Position; ms.Write(Zlib(big.AsSpan(0, 65536).ToArray())); Align(); ms.Write(Zlib(big.AsSpan(65536).ToArray())); Align();
var rpf = Path.Combine(src, "test.rpf"); File.WriteAllBytes(rpf, ms.ToArray());
File.WriteAllBytes(Path.Combine(src, "other.rpf"), new byte[100]);
File.WriteAllBytes(Path.Combine(src, "clip.bik"), new byte[10]);

// Inflater vs ZLibStream (niveaux variés)
foreach (var lvl in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize })
{
    var z = Zlib(xml, lvl);
    bool ok = ZlibInflater.TryInflate(z, 0, z.Length, 1 << 20, out var o, out int n, out int used) && n == xml.Length && o.AsSpan(0, n).SequenceEqual(xml) && used == z.Length;
    Check(ok, $"inflate {lvl}");
}

var files = SourceScanner.Scan(src);
Check(files.Count(f => f.Type == SourceType.Rpf9) == 1, "scan : 1 RPF9 reconnu");
Check(files.Count(f => f.Type == SourceType.RpfOther) == 1, "scan : RPF non-RPF9 signalé");

var hdrParsed = Rpf9Header.Parse(File.ReadAllBytes(rpf).AsSpan(0, 0x40), new FileInfo(rpf).Length);
Check(hdrParsed.TocEnd == 0x1000 && hdrParsed.EntryCountHint == 3, "en-tête");

var idx = Rpf9Indexer.Build(rpf, null, default);
Check(idx.Entries.Count == 3, $"3 entrées (obtenu {idx.Entries.Count})");
Check(idx.Entries[0].Kind == EntryKind.Xml && idx.Entries[0].Offset == offXml, "entrée xml");
Check(idx.Entries[1].Kind == EntryKind.Pso && idx.Entries[1].Offset == offPso, "entrée pso");
Check(idx.Entries[2].Kind == EntryKind.Xml && idx.Entries[2].StreamCount == 2 && idx.Entries[2].Offset == offBig, "entrée de 2 flux jointe");

var before = File.ReadAllBytes(rpf);
var p = Rpf9Exporter.Export(rpf, idx.Entries[2], outDir);
Check(File.ReadAllBytes(p).AsSpan().SequenceEqual(big), "export identique aux données d'origine");
Check(File.ReadAllBytes(rpf).AsSpan().SequenceEqual(before), "archive source inchangée");
try { Rpf9Exporter.Export(rpf, idx.Entries[0], Path.Combine(src, "sub")); Check(false, "refus export dans les sources"); }
catch (InvalidOperationException) { Check(true, "refus export dans les sources"); }

IndexCache.Save(cache, idx);
var again = IndexCache.TryLoad(cache, rpf);
Check(again != null && again.Entries.Count == 3 && again.Entries[0].Head.Length > 0, "cache aller-retour");

Check(EntryFilter.Apply(idx.Entries, new EntryQuery("xml")).Count == 2, "filtre texte");
Check(EntryFilter.Apply(idx.Entries, new EntryQuery("", EntryKind.Pso)).Count == 1, "filtre type");
Check(EntryFilter.Apply(idx.Entries, new EntryQuery(offPso.ToString("X"))).Count >= 1, "filtre offset hex");


// --- Paramètres et modules
var setPath = Path.Combine(tmp, "cfg", "settings.json");
var store = new SettingsStore(setPath);
var fresh = store.Load();
Check(!fresh.FirstRunCompleted && fresh.IsEnabled(ModuleId.Rpf9), "premier démarrage : assistant requis, RPF9 activé");
fresh.FirstRunCompleted = true; fresh.EnabledModules = new() { ModuleId.Rpf9, ModuleId.Models, ModuleId.Rpf9 }; fresh.WorkDirectory = Path.Combine(tmp, "w");
store.Save(fresh);
var back = store.Load();
Check(back.FirstRunCompleted && back.EnabledModules.SequenceEqual(new[] { ModuleId.Rpf9 }), "préférences : aller-retour, module non implémenté et doublon retirés");
Check(!back.IsEnabled(ModuleId.Models), "un module non implémenté n'est jamais activé");
File.WriteAllText(setPath, "{ corrompu");
Check(!store.Load().FirstRunCompleted, "fichier corrompu : valeurs par défaut");
Check(ModuleCatalog.All.Count == 6 && ModuleCatalog.All.Count(m => m.Implemented) == 5, "catalogue : 6 modules, 5 implémentés (RPF9, TTPF, Audio, Vidéo, Structurées)");
Check(ModuleCatalog.ForSourceType(SourceType.Rpf9) == ModuleId.Rpf9 && ModuleCatalog.ForSourceType(SourceType.Unknown) == null, "type de source -> module");
var inside = new AppSettings { WorkDirectory = Path.Combine(src, "cache") };
Check(inside.WorkDirIsInside(src) && !back.WorkDirIsInside(src), "détection d'un dossier de travail dans les sources");
Check(new AppSettings().EffectiveWorkDirectory.Contains("GTAALL-tool"), "dossier de travail par défaut");

// --- Apparence
var cat = AppearanceCatalog.Default;
Check(cat.Themes.Count == 4 && cat.Styles.Count == 2, "catalogue d'apparence : 4 thèmes, 2 styles");
Check(cat.FindStyle(AppearanceCatalog.GlassStyle)!.IsBeta && !cat.FindStyle(AppearanceCatalog.StandardStyle)!.IsBeta, "Liquid Glass marqué bêta, Standard stable");
Check(new AppearanceSettings().StyleKey == AppearanceCatalog.StandardStyle && new AppSettings().Appearance.StyleKey == AppearanceCatalog.StandardStyle, "la bêta n'est jamais activée par défaut");
Check(new AppearanceSettings().ThemeKey == "charcoal-teal", "thème par défaut = thème actuel");

// Lisibilité de chaque thème (WCAG)
foreach (var t in cat.Themes)
{
    var pal = t.Palette; Rgb C(string h) => Rgb.Parse(h);
    bool ok = true; string why = "";
    void Min(string n, string fg, string bgc, double min) { var r = ColorMath.Contrast(C(fg), C(bgc)); if (r < min) { ok = false; why += $" {n}={r:0.0}"; } }
    foreach (var bgc in new[] { pal.Bg, pal.Panel, pal.Panel2, pal.Input }) { Min("texte/" + bgc, pal.Text, bgc, 7); Min("muted/" + bgc, pal.Muted, bgc, 4.5); }
    foreach (var bgc in new[] { pal.Bg, pal.Panel, pal.Panel2, pal.Input }) { Min("accent", pal.Accent, bgc, 3); Min("erreur", pal.Error, bgc, 3); Min("alerte", pal.Warn, bgc, 3); Min("ok", pal.Ok, bgc, 3); }
    Min("surAccent", pal.OnAccent, pal.Accent, 4.5); Min("selection", pal.OnSelection, pal.AccentDim, 7);
    Min("texte/survol", pal.Text, pal.Hover, 4.5); Min("muted/survol", pal.Muted, pal.Hover, 4.5);
    Min("texte/ligne sélectionnée", pal.Text, pal.AccentDim, 4.5); Min("texte/ligne alternée", pal.Text, pal.PanelAlt, 7);
    Check(ok, $"contraste du thème {t.Key}{why}");
}

// Préréglages d'accent : lisibles sur tous les thèmes, texte lisible sur l'accent
bool accentsOk = true;
foreach (var t in cat.Themes) foreach (var a in AppearanceCatalog.AccentPresets)
{
    var r = AppearanceResolver.Resolve(new AppearanceSettings { ThemeKey = t.Key, AccentHex = a.Hex }, true, GlassCapabilities.Full);
    Rgb Col(string k) => Rgb.Parse(((SolidBrushSpec)r.Brushes[k]).Argb[3..]);
    if (ColorMath.Contrast(Col("AccentBrush"), Col("PanelSolidBrush")) < 2.99) accentsOk = false;
    if (ColorMath.Contrast(Col("OnAccentBrush"), Col("AccentBrush")) < 4.5) accentsOk = false;
    if (ColorMath.Contrast(Col("OnSelectionBrush"), Col("AccentDimBrush")) < 4.5) accentsOk = false;
}
Check(accentsOk, "accents prédéfinis lisibles sur les 4 thèmes");

// Normalisation et compatibilité
var bad = new AppearanceSettings { ThemeKey = "inconnu", StyleKey = "xyz", AccentHex = "pas-une-couleur", TransparencyPercent = 999 };
var notes = bad.Normalize();
Check(bad.ThemeKey == "charcoal-teal" && bad.StyleKey == "standard" && bad.AccentHex == null && bad.TransparencyPercent == 50 && notes.Count == 1, "valeurs invalides corrigées");
var oled = new AppearanceSettings { ThemeKey = "oled-black", StyleKey = AppearanceCatalog.GlassStyle };
oled.Normalize();
Check(oled.StyleKey == "standard" && oled.ThemeKey == "oled-black", "OLED + Liquid Glass incompatibles : style Standard, thème conservé");
Check(cat.Compatible("white", AppearanceCatalog.GlassStyle) && cat.Compatible("charcoal-petrol", AppearanceCatalog.GlassStyle), "Liquid Glass compatible avec les autres thèmes");

// Retrait propre de la bêta : style supprimé du catalogue -> Standard, le reste est conservé
var noGlass = new AppearanceCatalog(cat.Themes, cat.Styles.Where(x => x.Key == "standard"));
var kept = new AppearanceSettings { ThemeKey = "charcoal-petrol", StyleKey = AppearanceCatalog.GlassStyle, AccentHex = "#9B7BEA", TransparencyPercent = 20, FollowWindowsTheme = false };
var n2 = kept.Normalize(noGlass);
Check(kept.StyleKey == "standard" && kept.ThemeKey == "charcoal-petrol" && kept.AccentHex == "#9B7BEA" && kept.TransparencyPercent == 20 && n2.Count == 1, "retrait de la bêta : style Standard, autres préférences conservées + message");
var rr = AppearanceResolver.Resolve(new AppearanceSettings { StyleKey = AppearanceCatalog.GlassStyle }, true, GlassCapabilities.Full, noGlass);
Check(rr.Style.Key == "standard" && !rr.Translucent, "résolution avec bêta retirée : rendu opaque stable");

// Un settings.json existant (version 1, sans apparence) ou avec style inconnu reste lisible
var oldPath = Path.Combine(tmp, "old", "settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
File.WriteAllText(oldPath, "{\"Version\":1,\"FirstRunCompleted\":true,\"EnabledModules\":[0],\"WorkDirectory\":\"C:\\\\w\"}");
var oldS = new SettingsStore(oldPath).Load();
Check(oldS.FirstRunCompleted && oldS.Appearance.ThemeKey == "charcoal-teal" && oldS.Appearance.StyleKey == "standard", "ancien settings.json : préférences conservées, apparence par défaut");
File.WriteAllText(oldPath, "{\"FirstRunCompleted\":true,\"Appearance\":{\"ThemeKey\":\"white\",\"StyleKey\":\"style-supprime\",\"AccentHex\":\"#4C8DF6\"}}");
var oldS2 = new SettingsStore(oldPath).Load();
Check(oldS2.Appearance.ThemeKey == "white" && oldS2.Appearance.StyleKey == "standard" && oldS2.Appearance.AccentHex == "#4C8DF6" && oldS2.StartupNotices.Count == 1, "style inconnu dans le fichier : repli Standard + message, thème et accent conservés");

// Liste de modules invalide (valeur inconnue venant d'une version plus récente ou d'une édition manuelle, négative, doublon,
// non implémentée, ou null) : le chargement ne doit jamais échouer ni perdre le reste des préférences.
File.WriteAllText(oldPath, "{\"FirstRunCompleted\":true,\"EnabledModules\":[0,99,-5,0,4],\"WorkDirectory\":\"C:\\\\w\"}");
bool modulesRobust;
try { var badMods = new SettingsStore(oldPath).Load(); modulesRobust = badMods.FirstRunCompleted && badMods.WorkDirectory == "C:\\w" && badMods.EnabledModules.SequenceEqual(new[] { ModuleId.Rpf9 }); }
catch (Exception) { modulesRobust = false; }
Check(modulesRobust, "settings.json avec modules inconnus/doublons : chargé sans erreur, préférences conservées");
File.WriteAllText(oldPath, "{\"FirstRunCompleted\":true,\"EnabledModules\":null}");
bool nullRobust;
try { var nullMods = new SettingsStore(oldPath).Load(); nullRobust = nullMods.FirstRunCompleted && nullMods.EnabledModules.SequenceEqual(new[] { ModuleId.Rpf9 }); }
catch (Exception) { nullRobust = false; }
Check(nullRobust, "settings.json avec liste de modules null : valeur par défaut, sans erreur");
File.WriteAllText(oldPath, "{\"FirstRunCompleted\":true,\"EnabledModules\":[]}");
Check(new SettingsStore(oldPath).Load().EnabledModules.Count == 0, "liste de modules vide : respectée (aucun module activé)");

// Apparence précédente
var hist = new AppSettings();
hist.CommitAppearance(new AppearanceSettings { ThemeKey = "white" });
Check(hist.Appearance.ThemeKey == "white" && hist.PreviousAppearance?.ThemeKey == "charcoal-teal", "l'apparence précédente est mémorisée");
Check(hist.SwapWithPreviousAppearance() && hist.Appearance.ThemeKey == "charcoal-teal" && hist.PreviousAppearance!.ThemeKey == "white", "retour immédiat à l'apparence précédente");
Check(!new AppSettings().SwapWithPreviousAppearance(), "pas de précédente : retour impossible, sans erreur");
var same = new AppSettings(); same.CommitAppearance(new AppearanceSettings());
Check(same.PreviousAppearance == null, "pas de précédente si rien n'a changé");

// Enregistrement transactionnel (fenêtre Paramètres) : si l'écriture échoue, les réglages en mémoire reviennent à leur état d'origine,
// sinon « Annuler » appliquerait ensuite des réglages qui n'ont jamais été enregistrés.
var okPath = Path.Combine(tmp, "ok", "settings.json");
var okStore = new SettingsStore(okPath); var okSettings = new AppSettings();
okStore.ApplyAndSave(okSettings, cfg => { cfg.WorkDirectory = "C:\\w"; cfg.CommitAppearance(new AppearanceSettings { ThemeKey = "white", AccentHex = "#4C8DF6" }); });
var okBack = okStore.Load();
Check(okBack.WorkDirectory == "C:\\w" && okBack.Appearance.ThemeKey == "white" && okBack.Appearance.AccentHex == "#4C8DF6" && okBack.PreviousAppearance?.ThemeKey == "charcoal-teal",
      "ApplyAndSave : changements enregistrés puis relus");
var blocker = Path.Combine(tmp, "blocker"); File.WriteAllText(blocker, "x");      // un fichier là où il faudrait un dossier : l'écriture échoue
var failStore = new SettingsStore(Path.Combine(blocker, "settings.json"));
var live = new AppSettings { WorkDirectory = "C:\\avant" };
bool threw = false;
try { failStore.ApplyAndSave(live, cfg => { cfg.WorkDirectory = "C:\\apres"; cfg.EnabledModules = new(); cfg.CommitAppearance(new AppearanceSettings { ThemeKey = "white" }); }); }
catch (IOException) { threw = true; }
catch (UnauthorizedAccessException) { threw = true; }
Check(threw && live.WorkDirectory == "C:\\avant" && live.EnabledModules.SequenceEqual(new[] { ModuleId.Rpf9 })
      && live.Appearance.ThemeKey == "charcoal-teal" && live.PreviousAppearance == null,
      "ApplyAndSave : écriture impossible -> réglages en mémoire rétablis, erreur propagée");

// Résolution : clés, thèmes et styles
bool keysOk = true;
foreach (var t in cat.Themes) foreach (var st in cat.Styles) foreach (var caps in new[] { GlassCapabilities.Full, GlassCapabilities.NoBackdrop, GlassCapabilities.Opaque })
{
    var r = AppearanceResolver.Resolve(new AppearanceSettings { ThemeKey = t.Key, StyleKey = st.Key }, true, caps);
    if (AppearanceResolver.RequiredBrushKeys.Any(k => !r.Brushes.ContainsKey(k))) keysOk = false;
}
Check(keysOk, "toutes les clés de ressources fournies pour chaque thème, style et capacité");

var std = AppearanceResolver.Resolve(new AppearanceSettings(), true, GlassCapabilities.Full);
Check(std.Brushes["PanelBrush"] is SolidBrushSpec { Argb: "#FF1C2024" } && std.Brushes["AccentBrush"] is SolidBrushSpec { Argb: "#FF2CC4B6" } && std.Brushes["AccentDimBrush"] is SolidBrushSpec { Argb: "#FF17524D" }, "thème actuel inchangé (couleurs identiques)");

var glass = new AppearanceSettings { StyleKey = AppearanceCatalog.GlassStyle, TransparencyPercent = 50 };
var g1 = AppearanceResolver.Resolve(glass, true, GlassCapabilities.Full);
Check(g1.Translucent && g1.UseBackdrop && g1.Brushes["WindowBackgroundBrush"] is SolidBrushSpec w1 && w1.Argb.StartsWith("#00"), "Liquid Glass avec flou système : fond de fenêtre transparent");
var g2 = AppearanceResolver.Resolve(glass, true, GlassCapabilities.NoBackdrop);
Check(g2.Translucent && !g2.UseBackdrop && g2.Brushes["WindowBackgroundBrush"] is LinearBrushSpec && g2.Notes.Any(x => x.Contains("sans flou")), "sans flou système : translucide + dégradé de fond + message");
var g3 = AppearanceResolver.Resolve(glass, true, GlassCapabilities.Opaque);
Check(!g3.Translucent && g3.Brushes["PanelBrush"] is SolidBrushSpec { Argb: "#FF1C2024" }, "transparences désactivées dans Windows : rendu opaque");
var g4 = AppearanceResolver.Resolve(new AppearanceSettings { StyleKey = AppearanceCatalog.GlassStyle, ReduceTransparency = true }, true, GlassCapabilities.Full);
Check(!g4.Translucent && !g4.UseBackdrop, "mode sans transparence respecté");
var g5 = AppearanceResolver.Resolve(glass, true, new GlassCapabilities(true, true, true));
Check(g5.Style.Key == "standard" && !g5.Translucent, "contraste élevé de Windows : Standard imposé");

// Lisibilité du verre au pire niveau de transparence
bool glassReadable = true;
foreach (var t in cat.Themes.Where(x => x.AllowsTranslucency))
{
    var r = AppearanceResolver.Resolve(new AppearanceSettings { ThemeKey = t.Key, StyleKey = AppearanceCatalog.GlassStyle, TransparencyPercent = 50 }, true, GlassCapabilities.Full);
    var worstBackdrop = t.IsDark ? new Rgb(0x50, 0x50, 0x50) : new Rgb(0xD0, 0xD0, 0xD0);
    var spec = (SolidBrushSpec)r.Brushes["PanelBrush"];
    double alpha = Convert.ToInt32(spec.Argb.Substring(1, 2), 16) / 255.0;
    var panelOver = ColorMath.Blend(Rgb.Parse(spec.Argb[3..]), worstBackdrop, alpha);
    if (ColorMath.Contrast(Rgb.Parse(t.Palette.Text), panelOver) < 7 || ColorMath.Contrast(Rgb.Parse(t.Palette.Muted), panelOver) < 4.5) glassReadable = false;
    var inp = (SolidBrushSpec)r.Brushes["InputBrush"];
    if (Convert.ToInt32(inp.Argb.Substring(1, 2), 16) / 255.0 < 0.84) glassReadable = false;
}
Check(glassReadable, "Liquid Glass lisible à 50 % de transparence (texte, zones de données)");

// Suivi de Windows
var follow = new AppearanceSettings { FollowWindowsTheme = true, WindowsDarkThemeKey = "charcoal-petrol", ThemeKey = "oled-black" };
Check(AppearanceResolver.Resolve(follow, true, GlassCapabilities.Full).Theme.Key == "charcoal-petrol" && AppearanceResolver.Resolve(follow, false, GlassCapabilities.Full).Theme.Key == "white", "suivi de Windows : sombre -> thème choisi, clair -> Blanc");
var badDark = new AppearanceSettings { WindowsDarkThemeKey = "white" }; badDark.Normalize();
Check(badDark.WindowsDarkThemeKey == "charcoal-teal", "le thème 'sombre' de Windows doit être sombre");

// ---- Modules Vidéo / Audio / Données structurées ----
{
    var d = Path.Combine(tmp, "mods"); Directory.CreateDirectory(d);
    // Bink 2 synthétique
    var bk = new byte[0x40]; "KB2i"u8.CopyTo(bk);
    BitConverter.GetBytes((uint)(bk.Length - 8)).CopyTo(bk, 4); BitConverter.GetBytes(120u).CopyTo(bk, 8);
    BitConverter.GetBytes(4096u).CopyTo(bk, 0xC); BitConverter.GetBytes(1920u).CopyTo(bk, 0x14); BitConverter.GetBytes(1080u).CopyTo(bk, 0x18);
    BitConverter.GetBytes(30000u).CopyTo(bk, 0x1C); BitConverter.GetBytes(1001u).CopyTo(bk, 0x20); BitConverter.GetBytes(2u).CopyTo(bk, 0x28);
    var bkp = Path.Combine(d, "a.bk2"); File.WriteAllBytes(bkp, bk);
    var vr = FileInspector.Inspect(SourceScanner.Scan(d).First(x => x.Type == SourceType.Video))!;
    Check(vr.Fields.Any(x => x.Name == "largeur" && x.Value == "1920") && vr.Fields.Any(x => x.Name == "hauteur" && x.Value == "1080"), "Bink : dimensions lues");
    Check(vr.Fields.Any(x => x.Name.StartsWith("cadence") && x.Value.Contains("29.97")), "Bink : cadence 30000/1001");
    Check(vr.Fields.Any(x => x.Name == "images" && x.Value == "120") && vr.Notes.All(x => !x.Contains("≠")), "Bink : images, taille cohérente");
    var trunc = Path.Combine(d, "t.bik"); File.WriteAllBytes(trunc, bk.AsSpan(0, 0x20).ToArray()); File.Delete(bkp);
    var tr = FileInspector.Inspect(SourceScanner.Scan(d).First(x => x.Type == SourceType.Video))!;
    Check(tr.Fields.Count == 0 && tr.Notes.Any(x => x.Contains("tronqué") || x.Contains("absente")), "Bink tronqué : pas d'exception, note explicite");
    // Audio WAV
    var wav = new byte[44]; "RIFF"u8.CopyTo(wav.AsSpan(0)); BitConverter.GetBytes(36u).CopyTo(wav, 4); "WAVE"u8.CopyTo(wav.AsSpan(8)); "fmt "u8.CopyTo(wav.AsSpan(12));
    BitConverter.GetBytes(16u).CopyTo(wav, 16); BitConverter.GetBytes((ushort)1).CopyTo(wav, 20); BitConverter.GetBytes((ushort)2).CopyTo(wav, 22);
    BitConverter.GetBytes(48000u).CopyTo(wav, 24); BitConverter.GetBytes((ushort)16).CopyTo(wav, 34);
    File.WriteAllBytes(Path.Combine(d, "s.wav"), wav);
    var ar = FileInspector.Inspect(SourceScanner.Scan(d).First(x => x.Type == SourceType.Audio))!;
    Check(ar.Fields.Any(x => x.Name == "fréquence (Hz)" && x.Value == "48000") && ar.Fields.Any(x => x.Name == "canaux" && x.Value == "2"), "WAV : fréquence et canaux");
    var awc = new byte[16]; "ADAT"u8.CopyTo(awc);
    var awr = AudioInspector.Inspect(awc, 16);
    Check(awr.Fields.Any(x => x.Confidence == Confidence.Inferred && x.Value.Contains("AWC")), "AWC : reconnu avec certitude 'Inferred' seulement");
    Check(AudioInspector.Inspect(new byte[2], 2).Notes.Any(x => x.Contains("non reconnu")), "audio minuscule/inconnu sans exception");
    // Structurées
    File.WriteAllText(Path.Combine(d, "ok.xml"), "<?xml version=\"1.0\"?><root a=\"1\"><x/><y><z b=\"2\"/></y></root>");
    File.WriteAllText(Path.Combine(d, "bad.xml"), "<root><x></root>");
    File.WriteAllText(Path.Combine(d, "dtd.xml"), "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><r>&e;</r>");
    var all = SourceScanner.Scan(d).Where(x => x.Type == SourceType.Structured).ToDictionary(x => x.RelativePath);
    var ok = FileInspector.Inspect(all["ok.xml"])!;
    Check(ok.Fields.Any(x => x.Name == "élément racine" && x.Value == "root") && ok.Fields.Any(x => x.Name == "éléments" && x.Value == "4") && ok.Fields.Any(x => x.Name == "profondeur max" && x.Value == "3"), "XML valide : racine, 4 éléments, profondeur 3");
    var badXml = FileInspector.Inspect(all["bad.xml"])!;
    Check(badXml.Fields.Any(x => x.Name == "XML bien formé" && x.Value == "non") && badXml.Notes.Count > 0, "XML invalide signalé");
    var dtd = FileInspector.Inspect(all["dtd.xml"])!;
    Check(dtd.Fields.Any(x => x.Value == "non"), "XML avec DTD/entité externe refusé (pas de lecture de fichier local)");
    File.WriteAllBytes(Path.Combine(d, "p.pso"), "PSIN\0\0\0\0"u8.ToArray());
    var pso2 = FileInspector.Inspect(SourceScanner.Scan(d).First(x => x.RelativePath == "p.pso"))!;
    Check(pso2.Fields.Any(x => x.Value.Contains("PSIN")) && pso2.Notes.Any(x => x.Contains("non analysée")), "PSO : signature seule, limite annoncée");
    Check(FileInspector.Inspect(new SourceFile("x", "x", 0, SourceType.Rpf9, "")) == null, "RPF9 non traité par FileInspector");
}

Check(!ModuleCatalog.Get(ModuleId.Models).Implemented, "Modèles : non implémenté, donc non activable");

// ---- TTPF (index RDS5/ETPF, lecture seule) ----
{
    byte[] MakeTtpf(int textures, bool goodMagic = true, uint chunkCount = 3, uint etpfCount = 0xFFFFFFFF)
    {
        var b = new byte[0x100 + 16 + 24 * textures + 64];
        (goodMagic ? "RDS5"u8 : "XXXX"u8).CopyTo(b.AsSpan(0));
        BitConverter.GetBytes(chunkCount).CopyTo(b, 4); BitConverter.GetBytes(0x20000u).CopyTo(b, 8);
        // bloc 0 : zlib (0x20), fichier 0x1000, virtuel page 0 ; bloc 1 : BC7 (0x1C) ; bloc 2 : terminateur
        BitConverter.GetBytes((0x20u << 24) | (0x1000u >> 8)).CopyTo(b, 0x18); BitConverter.GetBytes(0u).CopyTo(b, 0x1C);
        BitConverter.GetBytes((0x1Cu << 24) | (0x2000u >> 8)).CopyTo(b, 0x20); BitConverter.GetBytes(1u).CopyTo(b, 0x24);
        "ETPF"u8.CopyTo(b.AsSpan(0x100)); BitConverter.GetBytes(0x27Au).CopyTo(b, 0x104);
        BitConverter.GetBytes(etpfCount == 0xFFFFFFFF ? (uint)textures : etpfCount).CopyTo(b, 0x108);
        for (int i = 0; i < textures; i++)
        {
            uint d0 = (uint)(255) | ((uint)(127) << 14) | (3u << 28);                  // 256x128, champ mips 3
            uint d1 = (98u << 3) | (2u << 20) | (0u << 24);                            // BC7, 2D, tile mode 0
            BitConverter.GetBytes(d0).CopyTo(b, 0x110 + 16 * i); BitConverter.GetBytes(d1).CopyTo(b, 0x114 + 16 * i);
            BitConverter.GetBytes(0x1000UL * (ulong)i).CopyTo(b, 0x118 + 16 * i);
            BitConverter.GetBytes(0x1111UL * (ulong)(i + 1)).CopyTo(b, 0x110 + 16 * textures + 8 * i);
        }
        return b;
    }
    var tt = Path.Combine(tmp, "ttpf"); Directory.CreateDirectory(tt);
    var good = Path.Combine(tt, "g.ttpf"); File.WriteAllBytes(good, MakeTtpf(3));
    var st = TtpfInspector.Read(good);
    Check(st.Textures.Count == 3 && st.Textures[0].Width == 256 && st.Textures[0].Height == 128 && st.Textures[0].FormatName == "BC7" && st.Textures[0].KindName == "2D" && st.Textures[0].MipField == 3, "TTPF : dimensions, format BC7, type 2D, champ mips");
    Check(st.Textures[2].Hash == 0x1111UL * 3 && st.EtpfVersion == 0x27A, "TTPF : hash 64 bits et version ETPF");
    Check(st.Chunks.Count == 2 && st.Chunks[0].Codec == "zlib" && st.Chunks[1].Codec == "BCPack BC7" && st.Chunks[1].FileOffset == 0x2000 && st.Chunks[1].VirtualOffset == 0x1000 + 0x4000, "TTPF : table de blocs (codec, offset fichier, offset virtuel)");
    var ir = FileInspector.Inspect(SourceScanner.Scan(tt).First(x => x.Type == SourceType.Ttpf))!;
    Check(ir.Fields.Any(x => x.Name == "textures" && x.Value == "3") && ir.Notes.Any(x => x.Contains("aucun pixel")), "TTPF : résumé + limite annoncée (aucun décodage)");
    // robustesse : ne doit jamais lever autre chose qu'une note
    foreach (var (name, data) in new (string, byte[])[] {
        ("magic", MakeTtpf(2, goodMagic: false)), ("chunks", MakeTtpf(2, chunkCount: 0xFFFFFFF0)),
        ("etpf", MakeTtpf(2, etpfCount: 0x7FFFFFFF)), ("court", new byte[10]), ("tronque", MakeTtpf(3)[..0x120]) })
    {
        var pth = Path.Combine(tt, name + ".ttpf"); File.WriteAllBytes(pth, data);
        InspectionResult? r2 = null; bool tthrew = false;
        try { r2 = TtpfInspector.Inspect(pth, data.Length); } catch (Exception) { tthrew = true; }
        Check(!tthrew && r2 != null && r2.Fields.Count == 0 && r2.Notes.Any(x => x.Contains("illisible")), $"TTPF invalide ({name}) : note explicite, pas d'exception");
    }
}

Directory.Delete(tmp, true);
Console.WriteLine(fails == 0 ? "TOUS LES TESTS PASSENT" : $"{fails} ECHEC(S)");
return fails;
