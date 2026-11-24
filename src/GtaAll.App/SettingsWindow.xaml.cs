using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GtaAll.Core.Appearance;
using GtaAll.Core.Settings;
using Microsoft.Win32;

namespace GtaAll.App;

public partial class SettingsWindow : Window
{
    /// <summary>Élément de liste déroulante (clé, libellé, disponibilité).</summary>
    public sealed record Choice(string Key, string Label, bool Enabled);

    private static readonly AppearanceCatalog Cat = AppearanceCatalog.Default;

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly Dictionary<ModuleId, CheckBox> _modules;
    private AppearanceSettings _work;
    private string? _workDir;
    private string? _notice;
    private bool _loading, _saved;

    /// <summary>Vrai si l'utilisateur a demandé à relancer l'assistant de bienvenue.</summary>
    public bool RelaunchWizard { get; private set; }

    public SettingsWindow(AppSettings settings, SettingsStore store)
    {
        InitializeComponent();
        _settings = settings; _store = store;
        _workDir = settings.WorkDirectory;
        _work = settings.Appearance.Clone();
        _modules = ModuleUi.Build(ModulesHost, settings);
        ThemeManager.Attach(this);
        BuildAccentSwatches();
        RefreshWorkDir();
        RefreshAppearanceUi();
        // Fermeture sans enregistrer (Annuler, croix, relance de l'assistant) : retour immédiat à l'apparence enregistrée.
        Closed += (_, _) => { if (!_saved) ThemeManager.Apply(_settings.Appearance); };
    }

    // ---- Apparence ---------------------------------------------------------------

    private void ApplyLive()
    {
        ThemeManager.Apply(_work);
        Dispatcher.BeginInvoke(new Action(RefreshAppearanceUi));   // différé : on ne modifie pas une liste pendant son propre événement
    }

    private void RefreshAppearanceUi()
    {
        _loading = true;
        var caps = SystemInfo.Detect();
        var resolved = ThemeManager.Resolved;
        var effectiveTheme = resolved?.Theme.Key ?? _work.ThemeKey;

        ThemeBox.ItemsSource = Cat.Themes.Select(t => new Choice(t.Key, t.Name, true)).ToList();
        ThemeBox.SelectedValue = _work.ThemeKey;
        ThemeBox.IsEnabled = !_work.FollowWindowsTheme;

        FollowBox.IsChecked = _work.FollowWindowsTheme;
        DarkRow.Visibility = _work.FollowWindowsTheme ? Visibility.Visible : Visibility.Collapsed;
        DarkThemeBox.ItemsSource = Cat.Themes.Where(t => t.IsDark).Select(t => new Choice(t.Key, t.Name, true)).ToList();
        DarkThemeBox.SelectedValue = _work.WindowsDarkThemeKey;

        StyleBox.ItemsSource = Cat.Styles.Select(s =>
        {
            bool ok = Cat.Compatible(effectiveTheme, s.Key);
            return new Choice(s.Key, ok ? s.Name : s.Name + " — incompatible avec ce thème", ok);
        }).ToList();
        StyleBox.SelectedValue = _work.StyleKey;

        bool beta = Cat.FindStyle(_work.StyleKey)?.IsBeta == true;
        BetaBadge.Visibility = beta ? Visibility.Visible : Visibility.Collapsed;
        BetaNote.Visibility = beta ? Visibility.Visible : Visibility.Collapsed;

        HexBox.Text = _work.AccentHex ?? "";
        foreach (Button b in AccentHost.Children.OfType<Button>())
        {
            bool sel = string.Equals(b.Tag as string, _work.AccentHex, StringComparison.OrdinalIgnoreCase);
            b.BorderThickness = new Thickness(sel ? 3 : 1);
            b.SetResourceReference(Button.BorderBrushProperty, sel ? "TextBrush" : "LineBrush");
        }

        bool glass = Cat.FindStyle(_work.StyleKey)?.NeedsTranslucency == true;
        TransparencySlider.Value = _work.TransparencyPercent;
        TransparencySlider.IsEnabled = glass && !_work.ReduceTransparency && caps.Glass.TransparencyEnabled;
        TransparencyLabel.Text = glass
            ? $"Niveau de transparence : {_work.TransparencyPercent} %"
            : "Niveau de transparence (disponible avec Liquid Glass)";
        ReduceBox.IsChecked = _work.ReduceTransparency;

        var notes = new List<string>();
        if (_notice != null) notes.Add(_notice);
        if (resolved != null) notes.AddRange(resolved.Notes);
        NotesText.Text = string.Join("\n", notes);
        PrevBtn.IsEnabled = _settings.PreviousAppearance != null;
        _loading = false;
    }

    private void BuildAccentSwatches()
    {
        AccentHost.Children.Clear();
        foreach (var a in AppearanceCatalog.AccentPresets)
        {
            var c = Rgb.Parse(a.Hex);
            var b = new Button
            {
                Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 3, 6, 3),
                Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), ToolTip = a.Name, Tag = a.Hex
            };
            b.Click += AccentSwatch_Click;
            AccentHost.Children.Add(b);
        }
    }

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        _work.AccentHex = (sender as Button)?.Tag as string;
        _notice = null; ErrorText.Text = "";
        ApplyLive();
    }

    private void ApplyHex_Click(object sender, RoutedEventArgs e)
    {
        if (!Rgb.TryParse(HexBox.Text, out var c)) { ErrorText.Text = "Couleur invalide : utilisez le format #RRGGBB, par exemple #4C8DF6."; return; }
        ErrorText.Text = ""; _notice = null;
        _work.AccentHex = c.Hex;
        ApplyLive();
    }

    private void AccentDefault_Click(object sender, RoutedEventArgs e) { _work.AccentHex = null; _notice = null; ErrorText.Text = ""; ApplyLive(); }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedValue is not string key) return;
        _work.ThemeKey = key;
        _notice = null;
        if (!Cat.Compatible(key, _work.StyleKey))
        {
            _work.StyleKey = AppearanceCatalog.StandardStyle;
            _notice = "Ce thème n'est pas compatible avec le style précédent : style repassé à Standard.";
        }
        ApplyLive();
    }

    private void DarkTheme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DarkThemeBox.SelectedValue is not string key) return;
        _work.WindowsDarkThemeKey = key; _notice = null; ApplyLive();
    }

    private void Follow_Click(object sender, RoutedEventArgs e)
    {
        _work.FollowWindowsTheme = FollowBox.IsChecked == true; _notice = null; ApplyLive();
    }

    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StyleBox.SelectedValue is not string key) return;
        _work.StyleKey = key; _notice = null; ApplyLive();
    }

    private void Transparency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _work.TransparencyPercent = (int)Math.Round(e.NewValue);
        TransparencyLabel.Text = $"Niveau de transparence : {_work.TransparencyPercent} %";
        ThemeManager.Apply(_work);
    }

    private void Reduce_Click(object sender, RoutedEventArgs e)
    {
        _work.ReduceTransparency = ReduceBox.IsChecked == true; _notice = null; ApplyLive();
    }

    /// <summary>Retour immédiat à l'apparence précédente (non enregistré tant qu'on ne clique pas sur Enregistrer).</summary>
    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.PreviousAppearance == null) return;
        _work = _settings.PreviousAppearance.Clone(); _notice = null; ApplyLive();
    }

    // ---- Dossier de travail ------------------------------------------------------

    private string EffectiveWorkDir => string.IsNullOrWhiteSpace(_workDir) ? AppSettings.DefaultWorkDirectory() : _workDir!;

    private void RefreshWorkDir()
    {
        WorkDirBox.Text = EffectiveWorkDir;
        WorkDetailText.Text = $"Index : {Path.Combine(EffectiveWorkDir, "index")}   ·   Exports : {Path.Combine(EffectiveWorkDir, "export")}";
    }

    private void ChangeWorkDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier de cache et d'export (distinct des sources)" };
        if (dlg.ShowDialog(this) != true) return;
        _workDir = dlg.FolderName; RefreshWorkDir();
    }

    private void ResetWorkDir_Click(object sender, RoutedEventArgs e) { _workDir = null; RefreshWorkDir(); }

    // ---- Boutons -----------------------------------------------------------------

    private void Relaunch_Click(object sender, RoutedEventArgs e) { RelaunchWizard = true; DialogResult = false; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.EnabledModules = ModuleUi.Read(_modules);
        _settings.WorkDirectory = _workDir;
        _settings.CommitAppearance(_work);
        try
        {
            _store.Save(_settings);
            _saved = true;
            ThemeManager.Apply(_settings.Appearance);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = "Impossible d'enregistrer les préférences : " + ex.Message;
        }
    }
}
