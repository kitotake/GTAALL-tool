using System.IO;
using System.Windows;
using System.Windows.Controls;
using GtaAll.Core.Formats;
using GtaAll.Core.Search;
using GtaAll.Core.Settings;
using GtaAll.Core.Sources;
using Microsoft.Win32;

namespace GtaAll.App;

public enum StatusKind { Info, Busy, Ok, Error }

public sealed class EntryRow
{
    public Rpf9Entry E { get; }
    public EntryRow(Rpf9Entry e) { E = e; }
    public string Id => E.Id;
    public string NameText => "— (TOC chiffrée)";
    public EntryKind Kind => E.Kind;
    public int StreamCount => E.StreamCount;
    public long CompressedSize => E.CompressedSize;
    public long InflatedSize => E.InflatedSize;
}

public sealed class SourceRow
{
    public SourceFile F { get; }
    public SourceRow(SourceFile f) { F = f; }
    public string RelativePath => F.RelativePath;
    public string TypeText => F.Type switch { SourceType.Rpf9 => "RPF9", SourceType.RpfOther => "RPF ?", _ => F.Type.ToString() };
    public string SizeText => FormatSize(F.Size);

    public static string FormatSize(long b)
    {
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{b} o" : $"{v:0.#} {u[i]}";
    }
}

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = AppState.Store;
    private readonly AppSettings _settings = AppState.Settings;
    private List<SourceFile> _allSources = new();
    private string? _sourceRoot;
    private SourceFile? _current;
    private Rpf9Index? _index;
    private CancellationTokenSource? _cts, _filterCts;
    private bool _indexing;

    public MainWindow()
    {
        InitializeComponent();
        KindBox.ItemsSource = new[] { "(tous)" }.Concat(Enum.GetNames<EntryKind>()).ToList();
        KindBox.SelectedIndex = 0;
        ThemeManager.Attach(this);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShowNotes();
        UpdateWorkLabel();
        if (!_settings.FirstRunCompleted) ShowWelcome();
        else SetStatus("Prêt. Lecture seule : les fichiers sources ne sont jamais modifiés.", StatusKind.Info);
        if (_settings.StartupNotices.Count > 0) SetStatus(string.Join(" ", _settings.StartupNotices), StatusKind.Info);
        UpdateButtons();
    }

    private void ShowNotes() =>
        NotesBox.Text = "Disponible : liste des fichiers, en-tête RPF9, indexation par flux zlib, recherche/filtre, export brut.\n\n" +
                        "À venir (non implémenté) : TTPF/RDS5 et textures, audio, vidéo, modèles, données structurées.\n\n" +
                        "Les noms de fichiers sont indisponibles : la table des matières RPF9 est chiffrée et GTAALL-tool ne tente pas de la déchiffrer.";

    private void UpdateWorkLabel()
    {
        WorkLabel.Text = "Cache/export : " + _settings.EffectiveWorkDirectory;
        WorkLabel.ToolTip = _settings.EffectiveWorkDirectory;
    }

    // ---- Assistant et paramètres -------------------------------------------------

    private void ShowWelcome()
    {
        new WelcomeWindow(_settings, _store) { Owner = this }.ShowDialog();
        AfterSettingsChanged();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_settings, _store) { Owner = this };
        w.ShowDialog();
        if (w.RelaunchWizard) ShowWelcome(); else AfterSettingsChanged();
    }

    private void AfterSettingsChanged()
    {
        UpdateWorkLabel();
        RefreshSources();
        UpdateButtons();
    }

    // ---- Sources -----------------------------------------------------------------

    private async void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier contenant les fichiers de votre copie du jeu" };
        if (!string.IsNullOrEmpty(_settings.LastSourceDirectory) && Directory.Exists(_settings.LastSourceDirectory))
            dlg.InitialDirectory = _settings.LastSourceDirectory;
        if (dlg.ShowDialog(this) != true) return;

        var root = dlg.FolderName;
        _sourceRoot = root;
        _settings.LastSourceDirectory = root;
        try { _store.Save(_settings); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        SetStatus("Analyse du dossier…", StatusKind.Busy);
        try
        {
            _allSources = await Task.Run(() => SourceScanner.Scan(root));
            RefreshSources();
            CheckWorkDir();
        }
        catch (Exception ex) { SetStatus("Erreur d'analyse : " + ex.Message, StatusKind.Error); }
    }

    private void RefreshSources()
    {
        var visible = _allSources
            .Where(f => ModuleCatalog.ForSourceType(f.Type) is { } m && _settings.IsEnabled(m))
            .Select(f => new SourceRow(f)).ToList();
        int hidden = _allSources.Count - visible.Count;
        SourceList.ItemsSource = visible;
        _current = null; _index = null;
        EntryGrid.ItemsSource = null; HeaderList.ItemsSource = null; CountText.Text = ""; HexBox.Text = "";
        ShowNotes();

        if (_sourceRoot == null) return;
        if (!ModuleCatalog.All.Any(m => _settings.IsEnabled(m.Id)))
        {
            SetStatus("Aucun module activé : ouvrez les Paramètres pour en choisir un.", StatusKind.Error);
            return;
        }
        var msg = $"{visible.Count} fichier(s) affiché(s), dont {visible.Count(v => v.F.CanIndex)} archive(s) RPF9 indexable(s).";
        if (hidden > 0) msg += $" {hidden} fichier(s) d'autres catégories masqué(s) (modules à venir ou désactivés).";
        SetStatus(msg, visible.Count == 0 ? StatusKind.Error : StatusKind.Info);
    }

    /// <summary>Le cache et les exports ne doivent jamais se trouver dans le dossier des sources.</summary>
    private bool CheckWorkDir()
    {
        if (_settings.WorkDirIsInside(_sourceRoot))
        {
            SetStatus("Le dossier de cache/export est dans le dossier des sources : choisissez-en un autre dans les Paramètres.", StatusKind.Error);
            return false;
        }
        return true;
    }

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        _current = (SourceList.SelectedItem as SourceRow)?.F;
        _index = null;
        EntryGrid.ItemsSource = null; HeaderList.ItemsSource = null; CountText.Text = ""; HexBox.Text = "";
        if (_current == null) { UpdateButtons(); return; }

        bool inspectable = _current.Type is SourceType.Video or SourceType.Audio or SourceType.Structured or SourceType.Ttpf;
        SetStatus(_current.Status, _current.CanIndex || inspectable ? StatusKind.Info : StatusKind.Error);
        if (inspectable)
        {
            try
            {
                var r = FileInspector.Inspect(_current);
                if (r != null) { HeaderList.ItemsSource = r.Fields; NotesBox.Text = string.Join("\n", r.Notes); }
            }
            catch (Exception ex) { NotesBox.Text = "Inspection impossible : " + ex.Message; SetStatus("Inspection impossible : " + ex.Message, StatusKind.Error); }
        }
        if (_current.CanIndex)
        {
            try
            {
                var buf = new byte[Rpf9Header.Size];
                using var fs = new FileStream(_current.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.ReadExactly(buf);
                var h = Rpf9Header.Parse(buf, fs.Length);
                HeaderList.ItemsSource = h.Fields;
                NotesBox.Text = string.Join("\n", h.Warnings);
            }
            catch (Exception ex) { NotesBox.Text = "En-tête illisible : " + ex.Message; SetStatus("En-tête illisible : " + ex.Message, StatusKind.Error); }

            var cached = IndexCache.TryLoad(_settings.EffectiveWorkDirectory, _current.Path);
            if (cached != null)
            {
                _index = cached; ShowIndex();
                SetStatus($"Index chargé depuis le cache : {cached.Entries.Count:N0} entrées.", StatusKind.Ok);
            }
        }
        UpdateButtons();
    }

    // ---- Indexation --------------------------------------------------------------

    private async void Index_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || !_current.CanIndex || !CheckWorkDir()) return;
        _cts = new CancellationTokenSource();
        _indexing = true; UpdateButtons();
        var path = _current.Path; var work = _settings.EffectiveWorkDirectory; var ct = _cts.Token;
        var prog = new Progress<IndexProgress>(p =>
        {
            Progress.Value = p.Length == 0 ? 0 : 100.0 * p.Position / p.Length;
            SetStatus($"Indexation… {p.Streams:N0} flux, {p.Entries:N0} entrées ({Progress.Value:0}%)", StatusKind.Busy);
        });
        try
        {
            var idx = await Task.Run(() => Rpf9Indexer.Build(path, prog, ct), ct);
            Directory.CreateDirectory(work);
            IndexCache.Save(work, idx);
            if (_current?.Path == path) { _index = idx; ShowIndex(); }
            SetStatus($"Indexation terminée : {idx.Entries.Count:N0} entrées, {idx.StreamCount:N0} flux (index enregistré dans le cache).", StatusKind.Ok);
        }
        catch (OperationCanceledException) { SetStatus("Indexation annulée.", StatusKind.Info); }
        catch (Exception ex) { SetStatus("Erreur d'indexation : " + ex.Message, StatusKind.Error); }
        finally { _indexing = false; Progress.Value = 0; UpdateButtons(); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void ShowIndex()
    {
        if (_index == null) return;
        HeaderList.ItemsSource = _index.HeaderFields;
        NotesBox.Text = string.Join("\n", _index.Warnings);
        ApplyFilter();
    }

    // ---- Recherche et sélection --------------------------------------------------

    private void Filter_Changed(object sender, EventArgs e) { if (IsLoaded) ApplyFilter(); }

    private async void ApplyFilter()
    {
        if (_index == null) return;
        _filterCts?.Cancel(); _filterCts = new CancellationTokenSource();
        var ct = _filterCts.Token;
        EntryKind? kind = KindBox.SelectedIndex > 0 && KindBox.SelectedItem is string name ? Enum.Parse<EntryKind>(name) : null;
        var q = new EntryQuery(SearchBox.Text ?? "", kind);
        var all = _index.Entries;
        try
        {
            var rows = await Task.Run(() => EntryFilter.Apply(all, q, ct).Select(x => new EntryRow(x)).ToList(), ct);
            EntryGrid.ItemsSource = rows;
            CountText.Text = $"   {rows.Count:N0} / {all.Count:N0}";
        }
        catch (OperationCanceledException) { }
    }

    private void Entry_Changed(object sender, SelectionChangedEventArgs e)
    {
        HexBox.Text = EntryGrid.SelectedItem is EntryRow r ? string.Join(" ", r.E.Head.Select(b => b.ToString("X2"))) : "";
        UpdateButtons();
    }

    // ---- Export ------------------------------------------------------------------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || EntryGrid.SelectedItem is not EntryRow r || !CheckWorkDir()) return;
        try
        {
            var p = Rpf9Exporter.Export(_current.Path, r.E, Path.Combine(_settings.EffectiveWorkDirectory, "export"));
            SetStatus("Exporté : " + p, StatusKind.Ok);
        }
        catch (Exception ex) { SetStatus("Export impossible : " + ex.Message, StatusKind.Error); }
    }

    // ---- État --------------------------------------------------------------------

    private void UpdateButtons()
    {
        IndexBtn.IsEnabled = _current?.CanIndex == true && !_indexing;
        CancelBtn.IsEnabled = _indexing;
        ExportBtn.IsEnabled = EntryGrid.SelectedItem is EntryRow && !_indexing;
    }

    private void SetStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text;
        var key = kind switch { StatusKind.Error => "ErrorBrush", StatusKind.Ok => "OkBrush", StatusKind.Busy => "AccentBrush", _ => "TextBrush" };
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, key);
    }
}
