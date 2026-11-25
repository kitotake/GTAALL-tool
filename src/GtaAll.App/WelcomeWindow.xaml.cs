using System.IO;
using System.Windows;
using System.Windows.Controls;
using GtaAll.Core.Settings;
using Microsoft.Win32;

namespace GtaAll.App;

/// <summary>Assistant de premier démarrage : modules, ouverture/indexation, cache et exports.</summary>
public partial class WelcomeWindow : Window
{
    private static readonly string[] Titles =
    {
        "Choisissez vos modules", "Ouvrir un dossier et indexer", "Où sont enregistrés le cache et les exports"
    };

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly Dictionary<ModuleId, CheckBox> _modules;
    private string? _workDir;
    private int _step;

    public WelcomeWindow(AppSettings settings, SettingsStore store)
    {
        InitializeComponent();
        _settings = settings; _store = store;
        _workDir = settings.WorkDirectory;
        _modules = ModuleUi.Build(ModulesHost, settings);
        ThemeManager.Attach(this);
        ShowStep(0);
    }

    private void ShowStep(int step)
    {
        _step = step;
        Step1Panel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepText.Text = $"Étape {step + 1} sur 3";
        TitleText.Text = Titles[step];
        BackBtn.IsEnabled = step > 0;
        NextBtn.Content = step == 2 ? "Terminer" : "Suivant";
        if (step == 2) RefreshWorkDir();
    }

    private string EffectiveWorkDir => string.IsNullOrWhiteSpace(_workDir) ? AppSettings.DefaultWorkDirectory() : _workDir!;

    private void RefreshWorkDir()
    {
        WorkDirBox.Text = EffectiveWorkDir;
        WorkDetailText.Text = $"Index (cache) : {Path.Combine(EffectiveWorkDir, "index")}\nExports : {Path.Combine(EffectiveWorkDir, "export")}";
    }

    private void ChangeWorkDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier de cache et d'export (distinct des sources)" };
        if (dlg.ShowDialog(this) != true) return;
        _workDir = dlg.FolderName; RefreshWorkDir();
    }

    private void ResetWorkDir_Click(object sender, RoutedEventArgs e) { _workDir = null; RefreshWorkDir(); }

    private void Back_Click(object sender, RoutedEventArgs e) { if (_step > 0) ShowStep(_step - 1); }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < 2) { ShowStep(_step + 1); return; }
        _settings.EnabledModules = ModuleUi.Read(_modules);
        _settings.WorkDirectory = _workDir;
        _settings.FirstRunCompleted = true;
        TrySave();
        DialogResult = true;
    }

    /// <summary>« Ignorer » : on garde les valeurs actuelles et on ne ré-affiche plus l'assistant automatiquement.</summary>
    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _settings.FirstRunCompleted = true;
        TrySave();
        DialogResult = false;
    }

    private void TrySave()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Impossible d'enregistrer les préférences : " + ex.Message, "GTAALL-tool");
        }
    }
}
