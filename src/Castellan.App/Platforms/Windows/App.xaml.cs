using Microsoft.UI.Xaml;

namespace Castellan.App.WinUI;

/// <summary>
/// Punkt wejścia buildu Windows. Cała zawartość aplikacji jest wspólna z Androidem —
/// ta klasa tylko oddaje sterowanie do <see cref="MauiProgram"/>.
/// </summary>
public partial class App : MauiWinUIApplication
{
    public App() => InitializeComponent();

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
