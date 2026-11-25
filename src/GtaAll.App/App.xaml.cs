using Microsoft.Win32;

namespace GtaAll.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        ComposeModules();
        AppState.Load();
        ThemeManager.Apply(AppState.Settings.Appearance);   // avant la création de la première fenêtre
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        base.OnStartup(e);
    }

    /// <summary>Racine de composition : seul endroit qui connaît tous les modules. Installe aussi le journal.</summary>
    private static void ComposeModules()
    {
        GtaAll.Core.Logging.Log.Current = new GtaAll.Core.Logging.FileLog(
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GTAALL-tool", "logs"));
        foreach (GtaAll.Core.Modules.IFormatModule m in new GtaAll.Core.Modules.IFormatModule[]
        {
            new GtaAll.Core.Formats.Rpf9Module(), new GtaAll.Core.Formats.TtpfModule(), new GtaAll.Core.Formats.AudioModule(),
            new GtaAll.Core.Formats.VideoModule(), new GtaAll.Core.Formats.StructuredDataModule()
        })
            GtaAll.Core.Modules.ModuleRegistry.Default.Register(m);
        GtaAll.Core.Logging.Log.Info("app", "démarrage ; modules : " + string.Join(", ", GtaAll.Core.Modules.ModuleRegistry.Default.Modules.Select(x => x.Id)));
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        base.OnExit(e);
    }

    // Windows a changé de thème/couleurs : on réévalue (utile surtout en mode « suivre Windows »).
    private void OnSystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Dispatcher.BeginInvoke(new Action(() => ThemeManager.Refresh(AppState.Settings.Appearance)));
    }
}
