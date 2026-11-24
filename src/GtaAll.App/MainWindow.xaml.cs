using System.IO;
using System.Windows;
using System.Windows.Controls;
using GtaAll.Core.Formats;
using GtaAll.Core.Search;
using GtaAll.Core.Sources;
using Microsoft.Win32;

namespace GtaAll.App;

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

public partial class MainWindow : Window
{
    private string? _sourceRoot, _workDir;
    private SourceFile? _current;
    private Rpf9Index? _index;
    private CancellationTokenSource? _cts, _filterCts;

    public MainWindow()
    {
        InitializeComponent();
        KindBox.ItemsSource = new object[] { "(tous)" }.Concat(Enum.GetNames<EntryKind>()).ToList();
        KindBox.SelectedIndex = 0;
        NotesBox.Text = "GTAALL-tool — lecture seule.\n\nImplémenté : liste des fichiers, en-tête RPF9, indexation par flux zlib, recherche/filtre, export brut.\n" +
                        "Non implémenté : TTPF/RDS5, BCPack, audio, vidéo, modèles, PSO structuré.\n\n" +
                        "Les noms de fichiers sont indisponibles : la table des matières RPF9 est chiffrée et GTAALL-tool ne tente pas de la déchiffrer.";
    }

    private void OpenSource_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier contenant les fichiers de votre copie du jeu" };
        if (dlg.ShowDialog() != true) return;
        _sourceRoot = dlg.FolderName;
        Status("Analyse du dossier…");
        var root = _sourceRoot;
        Task.Run(() => SourceScanner.Scan(root)).ContinueWith(t => Dispatcher.Invoke(() =>
        {
            if (t.IsFaulted) { Status("Erreur : " + t.Exception!.GetBaseException().Message); return; }
            SourceList.ItemsSource = t.Result;
            Status($"{t.Result.Count} fichier(s) reconnu(s) ; {t.Result.Count(f => f.CanIndex)} archive(s) RPF9.");
        }));
    }

    private void ChooseWork_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier de cache et d'export (distinct des sources)" };
        if (dlg.ShowDialog() != true) return;
        if (IsInside(dlg.FolderName, _sourceRoot)) { MessageBox.Show("Ce dossier est dans le dossier des sources. Choisissez-en un autre."); return; }
        _workDir = dlg.FolderName; Status("Dossier cache/export : " + _workDir);
        UpdateButtons();
    }

    private static bool IsInside(string child, string? parent)
    {
        if (parent == null) return false;
        var c = Path.GetFullPath(child).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(parent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private void Source_Changed(object s, SelectionChangedEventArgs e)
    {
        _current = SourceList.SelectedItem as SourceFile; _index = null;
        EntryGrid.ItemsSource = null; HeaderList.ItemsSource = null; CountText.Text = "";
        if (_current == null) { UpdateButtons(); return; }
        Status(_current.Status);
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
            catch (Exception ex) { NotesBox.Text = "En-tête illisible : " + ex.Message; }
            if (_workDir != null && IndexCache.TryLoad(_workDir, _current.Path) is { } cached) { _index = cached; ShowIndex(); Status("Index chargé depuis le cache."); }
        }
        UpdateButtons();
    }

    private async void Index_Click(object s, RoutedEventArgs e)
    {
        if (_current == null || !_current.CanIndex) return;
        _cts = new CancellationTokenSource();
        IndexBtn.IsEnabled = false; CancelBtn.IsEnabled = true;
        var path = _current.Path; var work = _workDir; var ct = _cts.Token;
        var prog = new Progress<IndexProgress>(p =>
        {
            Progress.Value = p.Length == 0 ? 0 : 100.0 * p.Position / p.Length;
            Status($"Indexation… {p.Streams:N0} flux, {p.Entries:N0} entrées");
        });
        try
        {
            var idx = await Task.Run(() => Rpf9Indexer.Build(path, prog, ct), ct);
            if (work != null) IndexCache.Save(work, idx);
            _index = idx; ShowIndex();
            Status($"Indexation terminée : {idx.Entries.Count:N0} entrées, {idx.StreamCount:N0} flux.");
        }
        catch (OperationCanceledException) { Status("Indexation annulée."); }
        catch (Exception ex) { Status("Erreur d'indexation : " + ex.Message); }
        finally { CancelBtn.IsEnabled = false; Progress.Value = 0; UpdateButtons(); }
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => _cts?.Cancel();

    private void ShowIndex()
    {
        if (_index == null) return;
        HeaderList.ItemsSource = _index.HeaderFields;
        NotesBox.Text = string.Join("\n", _index.Warnings);
        ApplyFilter();
    }

    private void Filter_Changed(object s, EventArgs e) { if (IsLoaded) ApplyFilter(); }

    private async void ApplyFilter()
    {
        if (_index == null) return;
        _filterCts?.Cancel(); _filterCts = new CancellationTokenSource(); var ct = _filterCts.Token;
        EntryKind? kind = KindBox.SelectedIndex > 0 ? Enum.Parse<EntryKind>((string)KindBox.SelectedItem) : null;
        var q = new EntryQuery(SearchBox.Text ?? "", kind); var all = _index.Entries;
        try
        {
            var res = await Task.Run(() => EntryFilter.Apply(all, q, ct).Select(x => new EntryRow(x)).ToList(), ct);
            EntryGrid.ItemsSource = res; CountText.Text = $"  {res.Count:N0} / {all.Count:N0}";
        }
        catch (OperationCanceledException) { }
    }

    private void Entry_Changed(object s, SelectionChangedEventArgs e)
    {
        if (EntryGrid.SelectedItem is EntryRow r)
            HexBox.Text = string.Join(" ", r.E.Head.Select(b => b.ToString("X2")));
        UpdateButtons();
    }

    private void Export_Click(object s, RoutedEventArgs e)
    {
        if (_current == null || EntryGrid.SelectedItem is not EntryRow r || _workDir == null) return;
        try
        {
            var p = Rpf9Exporter.Export(_current.Path, r.E, Path.Combine(_workDir, "export"));
            Status("Exporté : " + p);
        }
        catch (Exception ex) { Status("Export impossible : " + ex.Message); }
    }

    private void UpdateButtons()
    {
        IndexBtn.IsEnabled = _current?.CanIndex == true && !CancelBtn.IsEnabled;
        ExportBtn.IsEnabled = _workDir != null && EntryGrid.SelectedItem is EntryRow;
    }

    private void Status(string t) => StatusText.Text = t;
}
