using Castellan.App.Views;
using Castellan.Infrastructure;
using Castellan.Infrastructure.Data;
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

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CastellanDbContext>().Database.Migrate();

        return app;
    }
}
