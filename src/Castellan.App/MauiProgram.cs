using Castellan.App.Views;
using Castellan.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Castellan.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "castellan.db");
        builder.Services.AddInfrastructure(dbPath);

        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AccountsPage>();
        builder.Services.AddTransient<TransactionsPage>();
        builder.Services.AddTransient<EnvelopesPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        app.Services.ApplyMigrations();

        return app;
    }
}
