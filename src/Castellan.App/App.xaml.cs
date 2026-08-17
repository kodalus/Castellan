using Microsoft.Extensions.DependencyInjection;

namespace Castellan.App;

public partial class App : Microsoft.Maui.Controls.Application
{
	public App()
	{
		InitializeComponent();

		// Paleta ma tylko wariant ciemny — bez tego telefon ustawiony na jasny motyw
		// dostałby ciemne tło ze stylów i jasne domyślne kolory kontrolek systemowych.
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}