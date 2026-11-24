using Microsoft.Win32;

namespace GtaAll.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        AppState.Load();
        ThemeManager.Apply(AppState.Settings.Appearance);   // avant la création de la première fenêtre
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        base.OnStartup(e);
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
