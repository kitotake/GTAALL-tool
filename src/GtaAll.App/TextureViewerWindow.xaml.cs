using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GtaAll.Core.Formats;
using GtaAll.Core.IO;
using GtaAll.Textures.Decoding;

namespace GtaAll.App;

/// <summary>
/// Visionneuse de textures d'un magasin TTPF : liste filtrable à gauche, image (mip 0) à droite. Lecture seule sur la source ;
/// l'export écrit dans le dossier de travail (jamais dans le dossier des sources).
/// </summary>
public partial class TextureViewerWindow : Window
{
    private readonly string _path;
    private TtpfStore? _store;
    private List<TextureRow> _all = new();
    private readonly TtpfChunkCache _cache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _ticket;
    private DecodedTexture? _shown;
    private TextureRow? _shownRow;

    public TextureViewerWindow(string path)
    {
        InitializeComponent();
        _path = path;
        Title = "Textures — " + System.IO.Path.GetFileName(path);
        ThemeManager.Attach(this);
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SetStatus("Lecture de l'index des textures…", StatusKind.Busy);
        try
        {
            var st = await Task.Run(() => TtpfInspector.Read(_path));
            var rows = await Task.Run(() => TextureCatalog.Build(st));
            _store = st; _all = rows;
            ApplyFilter();
            SetStatus($"{rows.Count:N0} texture(s), dont {rows.Count(r => r.Decodable):N0} décodable(s). Cliquez sur une ligne pour voir l'image.", StatusKind.Ok);
        }
        catch (Exception ex)
        {
            GtaAll.Core.Logging.Log.Error("textures", "lecture de l'index TTPF impossible", ex);
            SetStatus("Lecture impossible : " + ex.Message, StatusKind.Error);
        }
    }

    // ---- Liste -------------------------------------------------------------------

    private void Filter_Changed(object sender, EventArgs e) { if (IsLoaded) ApplyFilter(); }

    private void ApplyFilter()
    {
        var rows = TextureCatalog.Filter(_all, SearchBox.Text, OkOnlyBox.IsChecked == true);
        TextureList.ItemsSource = rows;
        CountText.Text = $"{rows.Count:N0} / {_all.Count:N0} texture(s)";
    }

    private async void Texture_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TextureList.SelectedItem is not TextureRow row || _store == null) return;
        var st = _store;
        int ticket = ++_ticket;
        InfoText.Text = TextureCatalog.Describe(row);

        if (!row.Decodable)
        {
            ClearImage();
            SetStatus("Pas d'image pour cette texture : " + row.State, StatusKind.Info);
            return;
        }

        SetStatus("Décodage…", StatusKind.Busy);
        DecodedTexture? img = null;
        string? problem = null;
        try
        {
            await _gate.WaitAsync();
            try
            {
                if (ticket == _ticket)
                {
                    var result = await Task.Run(() =>
                    {
                        using var fs = SafeFileAccess.OpenRead(_path);
                        var d = TtpfImages.DecodeMip0(fs, st, row.Texture, out var p, _cache);
                        return (Img: d, Problem: p);
                    });
                    img = result.Img; problem = result.Problem;
                }
            }
            finally { _gate.Release(); }
        }
        catch (Exception ex)
        {
            GtaAll.Core.Logging.Log.Error("textures", "décodage de texture impossible", ex);
            if (ticket == _ticket) { ClearImage(); SetStatus("Décodage impossible : " + ex.Message, StatusKind.Error); }
            return;
        }

        if (ticket != _ticket) return; // une autre texture a été choisie entre-temps
        if (img == null)
        {
            ClearImage();
            SetStatus("Décodage impossible : " + (problem ?? "raison inconnue"), StatusKind.Error);
            return;
        }
        _shown = img; _shownRow = row;
        ExportBtn.IsEnabled = true;
        ShowImage();
        SetStatus($"{row.Hash} — {img.Width}×{img.Height}, {row.FormatText}.", StatusKind.Ok);
    }

    // ---- Image -------------------------------------------------------------------

    private void ClearImage()
    {
        _shown = null; _shownRow = null;
        Pic.Source = null;
        ExportBtn.IsEnabled = false;
    }

    private void ShowImage()
    {
        if (_shown == null) return;
        var bgra = ImageFiles.RgbaToBgra(_shown.Rgba, OpaqueBox.IsChecked == true);
        var bmp = BitmapSource.Create(_shown.Width, _shown.Height, 96, 96, PixelFormats.Bgra32, null, bgra, _shown.Width * 4);
        bmp.Freeze();
        // petites textures : pixels francs ; grandes : réduction de qualité
        RenderOptions.SetBitmapScalingMode(Pic, _shown.Width <= 256 && _shown.Height <= 256 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Fant);
        Pic.Source = bmp;
    }

    private void Opaque_Click(object sender, RoutedEventArgs e) => ShowImage();

    private void Fit_Click(object sender, RoutedEventArgs e) => SetFit(true);

    private void Actual_Click(object sender, RoutedEventArgs e) => SetFit(false);

    private void SetFit(bool fit)
    {
        Pic.Stretch = fit ? Stretch.Uniform : Stretch.None;
        var vis = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PicScroll.HorizontalScrollBarVisibility = vis;
        PicScroll.VerticalScrollBarVisibility = vis;
    }

    // ---- Export ------------------------------------------------------------------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_shown == null || _shownRow == null) return;
        try
        {
            var dir = System.IO.Path.Combine(AppState.Settings.EffectiveWorkDirectory, "export", "textures");
            SafeFileAccess.EnsureOutputOutsideSources(System.IO.Path.GetDirectoryName(_path)!, dir);
            Directory.CreateDirectory(dir);
            var t = _shownRow.Texture;
            var name = $"{t.Hash:X16}_{t.FormatName}_{t.Width}x{t.Height}".Replace(' ', '_');
            var png = System.IO.Path.Combine(dir, name + ".png");
            File.WriteAllBytes(png, ImageFiles.PngBytes(_shown.Rgba, _shown.Width, _shown.Height));
            File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".dds"), ImageFiles.DdsBytes(_shown.Width, _shown.Height, t.DxgiFormat, _shown.RawElements));
            SetStatus("Exporté : " + png + " (+ .dds)", StatusKind.Ok);
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + png + "\"");
        }
        catch (Exception ex)
        {
            GtaAll.Core.Logging.Log.Error("textures", "export de texture impossible", ex);
            SetStatus("Export impossible : " + ex.Message, StatusKind.Error);
        }
    }

    private CancellationTokenSource? _exportCts;

    /// <summary>Exporte en PNG + DDS toutes les textures décodables de la liste affichée (filtre appliqué). Un 2e clic annule.</summary>
    private async void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        if (_exportCts != null) { _exportCts.Cancel(); return; }
        if (_store == null) return;
        var todo = (TextureList.ItemsSource as IEnumerable<TextureRow>)?.Where(r => r.Decodable).ToList() ?? new List<TextureRow>();
        if (todo.Count == 0) { SetStatus("Aucune texture décodable dans la liste affichée.", StatusKind.Info); return; }

        var dir = System.IO.Path.Combine(AppState.Settings.EffectiveWorkDirectory, "export", "textures_" + System.IO.Path.GetFileNameWithoutExtension(_path));
        var ask = MessageBox.Show(this,
            $"Exporter {todo.Count:N0} texture(s) en PNG + DDS dans :\n{dir}\n\nSeules les textures de la liste affichée (filtre appliqué) et décodables sont exportées.",
            "Exporter tout", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;

        var cts = new CancellationTokenSource();
        _exportCts = cts;
        ExportAllBtn.Content = "Annuler l'export";
        var st = _store; var path = _path; var ct = cts.Token;
        var prog = new Progress<ExportProgress>(p => SetStatus($"Export… {p.Done:N0} / {p.Total:N0} ({p.Exported:N0} écrites, {p.Failed:N0} en échec)", StatusKind.Busy));
        try
        {
            var r = await Task.Run(() => TextureExporter.ExportAll(path, st, todo, dir, true, prog, ct));
            var msg = $"{(r.Cancelled ? "Export annulé" : "Export terminé")} : {r.Exported:N0} texture(s) écrite(s) dans {r.OutputDir}" +
                      (r.Failed > 0 ? $", {r.Failed:N0} en échec" : "");
            SetStatus(msg, r.Cancelled || r.Failed > 0 ? StatusKind.Info : StatusKind.Ok);
            if (r.Exported > 0) System.Diagnostics.Process.Start("explorer.exe", "\"" + r.OutputDir + "\"");
        }
        catch (Exception ex)
        {
            GtaAll.Core.Logging.Log.Error("textures", "export en lot impossible", ex);
            SetStatus("Export impossible : " + ex.Message, StatusKind.Error);
        }
        finally
        {
            _exportCts = null; cts.Dispose();
            ExportAllBtn.Content = "Exporter tout (liste affichée)";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _exportCts?.Cancel();
        base.OnClosed(e);
    }

    // ---- État --------------------------------------------------------------------

    private void SetStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text;
        var key = kind switch { StatusKind.Error => "ErrorBrush", StatusKind.Ok => "OkBrush", StatusKind.Busy => "AccentBrush", _ => "TextBrush" };
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, key);
    }
}
