using System.IO.Compression;
using System.Text;
using GtaAll.Core.Formats;
using GtaAll.Core.Search;
using GtaAll.Core.Appearance;
using GtaAll.Core.Settings;
using GtaAll.Core.Modules;
using GtaAll.Textures.Decoding;
using GtaAll.Core.Sources;

// Composition : Core ne connaît aucun module, le registre est rempli ici (comme le fait l'application).
foreach (var m in new IFormatModule[] { new Rpf9Module(), new TtpfModule(), new AudioModule(), new VideoModule(), new StructuredDataModule() })
    ModuleRegistry.Default.Register(m);

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
// Les valeurs affichées ne doivent pas dépendre de la langue de Windows (virgule décimale en français).
var savedCulture = System.Globalization.CultureInfo.CurrentCulture;
try { System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR"); } catch (System.Globalization.CultureNotFoundException) { }
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

System.Globalization.CultureInfo.CurrentCulture = savedCulture;

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

// ---- Architecture : registre, journal, accès fichiers ----
{
    var reg = new ModuleRegistry();
    reg.Register(new VideoModule()); reg.Register(new VideoModule());
    Check(reg.Modules.Count == 1, "registre : enregistrement idempotent par identifiant");
    bool refused = false;
    try { reg.Register(new ConflictModule()); } catch (InvalidOperationException) { refused = true; }
    Check(refused && reg.Modules.Count == 1, "registre : conflit d'extension refusé sans remplacement silencieux");
    Check(reg.ForExtension(".BK2") is VideoModule && reg.ForExtension(".xml") == null, "registre : extensions insensibles à la casse, inconnues ignorées");
    var dscan = Path.Combine(tmp, "reg"); Directory.CreateDirectory(dscan);
    File.WriteAllText(Path.Combine(dscan, "a.xml"), "<r/>"); File.WriteAllBytes(Path.Combine(dscan, "b.bk2"), new byte[8]);
    Check(SourceScanner.Scan(dscan, default, reg).Count == 1, "scanner : n'expose que les formats des modules enregistrés");
    var empty = new ModuleRegistry();
    Check(SourceScanner.Scan(dscan, default, empty).Count == 0, "scanner : registre vide -> aucun fichier reconnu");
    Check(FileInspector.Inspect(new SourceFile("x", "x", 0, SourceType.Video, ""), empty) == null, "inspecteur : sans module -> null");

    var logDir = Path.Combine(tmp, "logs"); var fl = new GtaAll.Core.Logging.FileLog(logDir);
    fl.Write(GtaAll.Core.Logging.LogLevel.Error, "test", "ligne1\nligne2", new IOException("boum"));
    var logTxt = File.ReadAllText(fl.PathForToday);
    Check(logTxt.Contains("ERROR") && logTxt.Contains("boum") && logTxt.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 1, "journal : une ligne par événement, erreur incluse");
    var badLog = new GtaAll.Core.Logging.FileLog(Path.Combine(tmp, "reg", "a.xml", "impossible"));
    bool logThrew = false; try { badLog.Write(GtaAll.Core.Logging.LogLevel.Info, "t", "m"); } catch { logThrew = true; }
    Check(!logThrew, "journal : une erreur d'E/S n'interrompt jamais l'application");
    var fbad = new SourceFile(Path.Combine(tmp, "absent.bk2"), "absent.bk2", 0, SourceType.Video, "");
    var rb = FileInspector.Inspect(fbad, reg);
    Check(rb != null && rb.Notes.Any(x => x.Contains("illisible")), "inspecteur : fichier disparu -> note, pas d'exception");

    bool outsideRefused = false;
    try { GtaAll.Core.IO.SafeFileAccess.EnsureOutputOutsideSources(dscan, Path.Combine(dscan, "out")); } catch (InvalidOperationException) { outsideRefused = true; }
    GtaAll.Core.IO.SafeFileAccess.EnsureOutputOutsideSources(dscan, Path.Combine(tmp, "ailleurs"));
    Check(outsideRefused, "SafeFileAccess : sortie dans les sources refusée, hors sources acceptée");
    Check(!typeof(ModuleRegistry).Assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("GtaAll.") && a.Name != "GtaAll.Core"), "Core ne dépend d'aucun module");
}

// ---- RPF9 sans flux zlib (cas réel : petite archive aux données non zlib) ----
{
    var dz = Path.Combine(tmp, "nozlib"); Directory.CreateDirectory(dz);
    var raw = new byte[0x400 + 4096]; "9FPR"u8.CopyTo(raw.AsSpan(0)); BitConverter.GetBytes(0x400u).CopyTo(raw, 4);
    var rnz = new Random(3); for (int i = 0x40; i < raw.Length; i++) { byte x; do x = (byte)rnz.Next(256); while (x == 0x78); raw[i] = x; }
    var pz = Path.Combine(dz, "enc.rpf"); File.WriteAllBytes(pz, raw);
    var iz = Rpf9Indexer.Build(pz, null, default);
    Check(iz.Entries.Count == 0 && iz.Warnings.Any(w => w.Contains("chiffré ou compressé autrement")), "RPF9 données aléatoires : 0 entrée + avertissement explicite (chiffré/autre compression)");
    var padded = (byte[])raw.Clone(); Array.Clear(padded, 0x400 + 600, 2700);
    var pz3 = Path.Combine(dz, "padded.rpf"); File.WriteAllBytes(pz3, padded);
    Check(Rpf9Indexer.Build(pz3, null, default).Warnings.Any(w => w.Contains("chiffré ou compressé autrement")), "RPF9 données aléatoires + remplissage de zéros : toujours qualifié chiffré/autre compression");
    var zeros = new byte[0x400 + 4096]; "9FPR"u8.CopyTo(zeros.AsSpan(0)); BitConverter.GetBytes(0x400u).CopyTo(zeros, 4);
    var pz2 = Path.Combine(dz, "zero.rpf"); File.WriteAllBytes(pz2, zeros);
    Check(Rpf9Indexer.Build(pz2, null, default).Warnings.Any(w => w.Contains("sans compression zlib")), "RPF9 données non aléatoires : avertissement 'stocké sans compression zlib'");
}

// ---- Décodage des textures : blocs BCn, disposition Gfx10, lecture du magasin, PNG/DDS ----
{
    var bcVectors = new (string fmt, string block, string rgba)[]
    {
        ("Bc7", "9d79b1a37f31801cd11a6706fb40d6bd", "c6d642ffc6e488ff92928bffb5b584ffc6d642ffa4a487ff8c8c8cff8a119cffb5b584ff9d9d89ff6c0fadff500dbeff92928bff500dbeff1808deffa6148bff"),
        ("Bc7", "56526846903bb13ede562439e9c1b823", "5151f6ff5962f2ff5962f2ff7ca8e4ff8d5aa7ff5151f6ff7497e8ff85b9e1ff994cb5ff994cb5ff6273efff6b86ebff75778cff44b154ff994cb5ff5151f6ff"),
        ("Bc7", "ac6089bca71f3d1a6d2d3cadb3669cbd", "84ff5aff298c5aff47b25aff8cf7c6ff298c5aff84ff5aff298c5aff8cf7c6ffef18b5ff4a6b73ffb9339fffc97daaffb9339fff4a6b73ff4a6b73ffc97daaff"),
        ("Bc7", "58e165e434249d8b829f411669842a97", "f020c0ffc25ab5ff64d29efff020c0ffcf9366fff020c0fff020c0ffc25ab5ffcc8275ffcf9366ff9298a9fff020c0ffd2a258ffcc8275ffcc8275ff9298a9ff"),
        ("Bc7", "9011036cf3e822086ecaa0075a69fc17", "8c00b58eae72c38ebeaaca9ca454c08e8c00b58eae72c39cbeaaca8e9c38bc8e941cb9bab68ec79c941cb9babeaaca8ec6c6ce9cc6c6ce9cb68ec7ac8c00b59c"),
        ("Bc7", "a0a8f83718aa8f3bd1f65e8144e61d9a", "50e342bfb3b2b497b3e3b49780b279abe37feb83b3b2b497e37feb83804e79abe3b2eb83e34eeb83b3b2b497b3e3b497507f42bf507f42bf50b242bf807f79ab"),
        ("Bc7", "c00fcb06a6c1ad8f2906e732b10f4db7", "4683898743797a994a8f9a733f6d69ad4b94a16a57bcda2843797a99447e8190417271a452abc14559c1e11f3f6d69ad55b5d033468389874b94a16a52abc145"),
        ("Bc7", "80d35ea68c088ab3f648818ba4a6656b", "b23f88693000b2c32d0d8a902b1b605b86679f3db23f88692b1b605b2b1b605bb23f8869b23f886986679f3d2d0d8a905d8eb61486679f3d86679f3db23f8869"),
        ("Bc1", "1ff8e0071b1b1b1b", "55aa55ffaa55aaff00ff00ffff00ffff55aa55ffaa55aaff00ff00ffff00ffff55aa55ffaa55aaff00ff00ffff00ffff55aa55ffaa55aaff00ff00ffff00ffff"),
        ("Bc3", "c828e0cb6e38ff7200f81f00e4e4e4e4", "ff0000c80000ff83aa00553e5500aa6cff0000830000ff6caa00559a5500aa9aff0000c80000ff3eaa0055835500aa3eff00003e0000ff6caa0055835500aa9a"),
        ("Bc4", "fa0aac1dda963747", "930000ff700000ff4e0000ff4e0000ff0a0000ff930000ff4e0000ff4e0000ff4e0000ffd70000ff4e0000ffb50000ffb50000ff4e0000ff0a0000ffd70000ff"),
        ("Bc5", "fa0a8bd536cf8ade1edc1fe7a90141c2", "b5ff00ff0a6a00ff4e9000ffd76a00ff700000ff706a00ff704400ff0ab600ff2cdc00ff0a1e00ffb59000ff701e00fffa9000ff709000ff2c1e00ff4e0000ff"),
    };
    foreach (var (fmt, block, expected) in bcVectors)
    {
        var f = Enum.Parse<BcDecoder.Format>(fmt);
        var got = new byte[64]; BcDecoder.DecodeBlock(f, Convert.FromHexString(block), got);
        var exp = Convert.FromHexString(expected);
        int[] chans = fmt == "Bc4" ? new[] { 0 } : fmt == "Bc5" ? new[] { 0, 1 } : new[] { 0, 1, 2, 3 };
        bool bcSame = true; for (int pi = 0; pi < 16; pi++) foreach (var c in chans) if (got[pi * 4 + c] != exp[pi * 4 + c]) bcSame = false;
        Check(bcSame, $"décodage {fmt} : bloc identique à la référence ({block[..8]}…)");
    }
    var bc1t = new byte[] { 0x1F, 0x00, 0xE0, 0x07, 0x1B, 0x1B, 0x1B, 0x1B };   // c0 < c1 : mode 3 couleurs
    var o1 = new byte[64]; BcDecoder.DecodeBlock(BcDecoder.Format.Bc1, new byte[] { 0x1F, 0x00, 0xE0, 0x07, 0xFF, 0xFF, 0xFF, 0xFF }, o1);
    Check(o1[3] == 0 && o1[0] == 0 && o1[1] == 0 && o1[2] == 0, "BC1 mode 3 couleurs : 4e couleur = noir transparent (spécification D3D)");
    var o3 = new byte[64]; BcDecoder.DecodeBlock(BcDecoder.Format.Bc3, new byte[] { 255, 255, 0, 0, 0, 0, 0, 0, 0x1F, 0x00, 0xE0, 0x07, 0xFF, 0xFF, 0xFF, 0xFF }, o3);
    Check(o3[3] == 255 && o3[0] != 0 || o3[1] != 0, "BC3 : toujours 4 couleurs, même si c0 < c1 (spécification D3D)");
    var o7 = new byte[64]; BcDecoder.DecodeBlock(BcDecoder.Format.Bc7, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, o7);
    Check(o7.All(x => x == 0), "BC7 : bloc réservé (mode > 7) -> transparent noir, sans exception");
    Check(BcDecoder.Decode(BcDecoder.Format.Bc7, new byte[16], 3, 5).Length == 3 * 5 * 4, "décodage d'une image non multiple de 4 : taille w*h*4");
    Check(BcDecoder.Decode(BcDecoder.Format.Bc1, new byte[4], 8, 8).Length == 8 * 8 * 4, "données trop courtes : pas d'exception");

    // disposition Gfx10 : valeurs mesurées sur le vrai fichier (2048x2048 BC7, SW_64KB_D, 8 mips)
    var lay = Gfx10Layout.Create(true, 512, 512, 8, 16)!;
    Check(lay.SliceBytes == 5636096 && lay.MipSpan(0) == (0x160000L, 0x560000L) && lay.MipSpan(1) == (0x60000L, 0x160000L) && lay.MipSpan(4) == (0L, 0x10000L),
        "Gfx10 : 2048x2048 BC7 -> 5 636 096 octets, mip0 0x160000-0x560000 (mesuré sur le fichier réel)");
    foreach (var (d64, w, h, mips, bpe) in new[] { (true, 64, 64, 1, 16), (false, 100, 36, 4, 16), (true, 33, 17, 3, 8), (false, 20, 20, 1, 4), (true, 128, 128, 5, 1) })
    {
        var l = Gfx10Layout.Create(d64, w, h, mips, bpe)!;
        var seen = new HashSet<long>(); bool inRange = true, uniq = true;
        var (a, b) = l.MipSpan(0);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { long ad = l.Addr(0, x, y); if (ad < a || ad + bpe > b) inRange = false; if (!seen.Add(ad)) uniq = false; }
        Check(inRange && uniq, $"Gfx10 {(d64 ? "64K" : "4K")} {w}x{h} bpe{bpe} : adresses de la mip 0 uniques et dans sa plage");
    }
    Check(Gfx10Layout.Create(true, 8, 8, 1, 3) == null, "Gfx10 : taille d'élément non gérée -> null (pas d'exception)");

    // PNG / DDS
    var px = new byte[4 * 3 * 4]; for (int i = 0; i < px.Length; i++) px[i] = (byte)(i * 7);
    var png = ImageFiles.PngBytes(px, 4, 3);
    Check(png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "PNG : signature");
    {   // relecture : on extrait IDAT, on décompresse et on retrouve les pixels
        int pp = 8; var idat = new MemoryStream(); bool crcOk = true;
        while (pp < png.Length)
        {
            int len = (png[pp] << 24) | (png[pp + 1] << 16) | (png[pp + 2] << 8) | png[pp + 3];
            var type = System.Text.Encoding.ASCII.GetString(png, pp + 4, 4);
            uint crc = 0xFFFFFFFF; for (int i = pp + 4; i < pp + 8 + len; i++) { crc ^= png[i]; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1; } crc ^= 0xFFFFFFFF;
            uint stored = (uint)((png[pp + 8 + len] << 24) | (png[pp + 9 + len] << 16) | (png[pp + 10 + len] << 8) | png[pp + 11 + len]);
            if (crc != stored) crcOk = false;
            if (type == "IDAT") idat.Write(png, pp + 8, len);
            pp += 12 + len;
        }
        idat.Position = 0; var raw = new MemoryStream(); new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress).CopyTo(raw);
        var rb = raw.ToArray(); bool pix = rb.Length == 3 * (1 + 16);
        for (int y = 0; pix && y < 3; y++) { if (rb[y * 17] != 0) pix = false; for (int x = 0; x < 16; x++) if (rb[y * 17 + 1 + x] != px[y * 16 + x]) pix = false; }
        Check(crcOk && pix, "PNG : CRC de chaque bloc valides et pixels relus identiques");
    }
    var dds = ImageFiles.DdsBytes(8, 4, 98, new byte[32 * 1]);
    Check(dds.AsSpan(0, 4).SequenceEqual("DDS "u8) && BitConverter.ToUInt32(dds, 12) == 4 && BitConverter.ToUInt32(dds, 16) == 8 && BitConverter.ToUInt32(dds, 128) == 98 && dds.Length == 148 + 32, "DDS : en-tête DX10, dimensions, format DXGI, taille");

    // magasin TTPF synthétique : 2 blocs zlib + 1 bloc BCPack, une texture RGBA8 4x4 + une R8 en tuilage 0
    {
        byte[] Z(byte[] d) { using var ms = new MemoryStream(); using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) z.Write(d); return ms.ToArray(); }
        var t0 = new TtpfTexture(0x11, 8, 8, 61 /*R8*/, 2, 1, 1, 0, 0, 0x1000);       // virt 0x1000, tile 1 (SW_4KB_S), R8, 1 mip
        var l0 = TextureLayouts.LayoutFor(t0, 1)!;
        var tiled = new byte[0x4000]; var grad = new byte[64];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) { grad[y * 8 + x] = (byte)(y * 16 + x); tiled[l0.Addr(0, x, y)] = grad[y * 8 + x]; }
        var c0 = Z(tiled); var c1 = Z(new byte[0x4000].Select((_, i) => (byte)(i & 0xFF)).ToArray());
        var file = new List<byte>(); file.AddRange("RDS5"u8.ToArray()); file.AddRange(BitConverter.GetBytes(4u)); file.AddRange(BitConverter.GetBytes(0x10000u)); file.AddRange(new byte[12]);
        file.AddRange(new byte[0x1000 - file.Count]);
        long off0 = 0x1000; file.AddRange(c0); while (file.Count % 0x100 != 0) file.Add(0);
        long off1 = file.Count; file.AddRange(c1); while (file.Count % 0x100 != 0) file.Add(0);
        long off2 = file.Count; file.AddRange(new byte[0x100]);   // bloc BCPack factice
        long end = file.Count + 0x100; file.AddRange(new byte[0x100]);
        uint A(uint fl, long o) => (fl << 24) | (uint)(o >> 8);
        var tab = new List<byte>();
        foreach (var (fl, o, vp) in new[] { (0x20u, off0, 0u), (0x20u, off1, 1u), (0x1Cu, off2, 2u), (0x00u, end, 3u) }) { tab.AddRange(BitConverter.GetBytes(A(fl, o))); tab.AddRange(BitConverter.GetBytes(vp)); }
        var arr = file.ToArray(); BitConverter.GetBytes(4u).CopyTo(arr, 4); tab.CopyTo(arr, 0x18);
        var full = new byte[0x1000 + 0x400]; // l'index ETPF doit se trouver à align(0x10+8n,0x100)
        long eoff = (0x10L + 8 * 4 + 0xFF) & ~0xFFL;
        // l'index ETPF (0x100) tient dans l'en-tête de 0x1000 octets
        "ETPF"u8.CopyTo(arr.AsSpan((int)eoff)); BitConverter.GetBytes(0x27Au).CopyTo(arr, (int)eoff + 4); BitConverter.GetBytes(1u).CopyTo(arr, (int)eoff + 8);
        uint d0 = 7u | (7u << 14), d1 = (61u << 3) | (2u << 20) | (1u << 24);
        BitConverter.GetBytes(d0).CopyTo(arr, (int)eoff + 16); BitConverter.GetBytes(d1).CopyTo(arr, (int)eoff + 20); BitConverter.GetBytes(0UL).CopyTo(arr, (int)eoff + 24);
        BitConverter.GetBytes(0x11UL).CopyTo(arr, (int)eoff + 32);
        var pth = Path.Combine(tmp, "synth.ttpf"); File.WriteAllBytes(pth, arr);
        var synthStore = TtpfInspector.Read(pth);
        Check(synthStore.Textures.Count == 1 && synthStore.Textures[0].Virt == 0x1000 && synthStore.Textures[0].Mips == 1 && synthStore.Textures[0].DataSize == l0.SliceBytes, "TTPF synthétique : emplacement des données, mips, taille calculée");
        using var fsx = File.OpenRead(pth);
        var v = TtpfImages.ReadVirtual(fsx, synthStore, 0x1000 + 0x4000 + 5, 8, out long miss0);
        Check(miss0 == 0 && v.SequenceEqual(new byte[] { 5, 6, 7, 8, 9, 10, 11, 12 }), "lecture de l'espace virtuel : octets du 2e bloc zlib corrects");
        var vb = TtpfImages.ReadVirtual(fsx, synthStore, 0x1000 + 0x8000, 16, out long missB);
        Check(missB == 16 && vb.All(x => x == 0), "lecture : bloc BCPack -> octets manquants comptés, pas d'exception");
        var imgR = TtpfImages.DecodeMip0(fsx, synthStore, synthStore.Textures[0], out var prob);
        bool g = imgR != null && imgR.Width == 8 && imgR.Height == 8;
        for (int i = 0; g && i < 64; i++) if (imgR!.Rgba[i * 4] != grad[i] || imgR.Rgba[i * 4 + 3] != 255) g = false;
        Check(g && prob == null, "décodage de bout en bout (R8, tuilage 4K) : dégradé retrouvé");
        var straddle = synthStore.Textures[0] with { Virt = 0x1000 + 0x8000 - 16 };
        Check(TtpfImages.DecodeMip0(fsx, synthStore, straddle, out var p2) == null && p2 != null && p2.Contains("BCPack"), "texture dans un bloc BCPack : refus explicite, jamais d'image fausse");
        Check(TtpfImages.DecodeMip0(fsx, synthStore, synthStore.Textures[0] with { Kind = 4 }, out var p3) == null && p3 != null, "cube/tableau : refus explicite");

        // cache de blocs : mêmes résultats qu'avec lecture directe, y compris bloc BCPack et budget qui force l'éviction
        {
            var chunkCache = new TtpfChunkCache();
            var c1a = TtpfImages.ReadVirtual(fsx, synthStore, 0x1000 + 0x4000 + 5, 8, out long mc1, chunkCache);
            var c1b = TtpfImages.ReadVirtual(fsx, synthStore, 0x1000 + 0x4000 + 5, 8, out long mc2, chunkCache);
            Check(mc1 == 0 && mc2 == 0 && c1a.SequenceEqual(v) && c1b.SequenceEqual(v) && chunkCache.Hits == 1 && chunkCache.Misses == 1, "cache de blocs : 2e lecture servie par le cache, octets identiques");
            var cb = TtpfImages.ReadVirtual(fsx, synthStore, 0x1000 + 0x8000, 16, out long mcb, chunkCache);
            Check(mcb == 16 && cb.All(x => x == 0), "cache de blocs : bloc BCPack toujours compté comme manquant");
            var none = new TtpfChunkCache(0);
            var z1 = TtpfImages.ReadVirtual(fsx, synthStore, 0x1000 + 0x4000 + 5, 8, out long mn1, none);
            var z2 = TtpfImages.ReadVirtual(fsx, synthStore, 0x1000 + 0x4000 + 5, 8, out long mn2, none);
            Check(mn1 == 0 && mn2 == 0 && z1.SequenceEqual(v) && z2.SequenceEqual(v) && none.Hits == 0, "cache de blocs : budget nul -> aucune mémorisation, résultat correct");
            var imgC = TtpfImages.DecodeMip0(fsx, synthStore, synthStore.Textures[0], out var probC, new TtpfChunkCache());
            Check(imgC != null && probC == null && imgR != null && imgC.Rgba.SequenceEqual(imgR.Rgba), "cache de blocs : décodage d'une texture identique avec ou sans cache");
        }
    }
}

Directory.Delete(tmp, true);
Console.WriteLine(fails == 0 ? "TOUS LES TESTS PASSENT" : $"{fails} ECHEC(S)");
return fails;

sealed class ConflictModule : IFormatModule
{
    public ModuleId Id => ModuleId.Models;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".bk2" };
    public SourceClassification Classify(string path, string extension) => new(SourceType.Video, "");
    public InspectionResult? Inspect(SourceFile file) => null;
}
