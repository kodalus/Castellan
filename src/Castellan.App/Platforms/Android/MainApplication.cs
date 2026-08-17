using Android.App;
using Android.Runtime;
using AndroidX.AppCompat.App;

namespace Castellan.App;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	public override void OnCreate()
	{
		// UserAppTheme = Dark w App.xaml.cs steruje tylko kolorami rysowanymi przez
		// MAUI. Natywne powierzchnie Androida — arkusz „więcej”, DisplayAlert,
		// DisplayActionSheet, kalendarz w DatePickerze — biorą kolory z motywu
		// Theme.MaterialComponents.DayNight, który patrzy na ustawienie systemowe.
		// Na telefonie w trybie jasnym wychodziły więc białe na tle ciemnej apki.
		// Wymuszenie trybu nocnego ustawia je wszystkie naraz.
		AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightYes;
		base.OnCreate();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
