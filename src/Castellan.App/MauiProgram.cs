using System.Globalization;
using Castellan.App.Services;
using Castellan.App.ViewModels;
using Castellan.App.Views;
using Castellan.Application.UseCases;
using Castellan.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Castellan.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var polish = new CultureInfo("pl-PL");
        CultureInfo.DefaultThreadCurrentCulture = polish;
        CultureInfo.DefaultThreadCurrentUICulture = polish;

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

        // Use cases
        builder.Services.AddTransient<CreateAccountUseCase>();
        builder.Services.AddTransient<AddManualTransactionUseCase>();
        builder.Services.AddTransient<DeleteTransactionUseCase>();
        builder.Services.AddTransient<PlanMonthUseCase>();
        builder.Services.AddTransient<GetMonthOverviewUseCase>();
        builder.Services.AddTransient<GetAccountsWithBalancesUseCase>();
        builder.Services.AddTransient<ReconcileAccountUseCase>();
        builder.Services.AddTransient<IngestRawNotificationUseCase>();

#if ANDROID
        builder.Services.AddSingleton<INotificationPermissionService,
            Castellan.App.Platforms.Android.Services.AndroidNotificationPermissionService>();
#endif

        // ViewModels
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<AccountsViewModel>();
        builder.Services.AddTransient<TransactionsViewModel>();
        builder.Services.AddTransient<EnvelopesViewModel>();
        builder.Services.AddTransient<AddAccountViewModel>();
        builder.Services.AddTransient<AddTransactionViewModel>();
        builder.Services.AddTransient<PlanEnvelopesViewModel>();
        builder.Services.AddTransient<ReconcileAccountViewModel>();
        builder.Services.AddTransient<QuickAddTransactionViewModel>();
        builder.Services.AddTransient<InboxViewModel>();
        builder.Services.AddTransient<CategoryRulesViewModel>();
        builder.Services.AddTransient<AddCategoryRuleViewModel>();
        builder.Services.AddTransient<CategoriesViewModel>();
        builder.Services.AddTransient<AddCategoryViewModel>();

        // Pages (tab pages are Transient so Shell reuses cached instances)
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AccountsPage>();
        builder.Services.AddTransient<TransactionsPage>();
        builder.Services.AddTransient<EnvelopesPage>();
        builder.Services.AddTransient<AddAccountPage>();
        builder.Services.AddTransient<AddTransactionPage>();
        builder.Services.AddTransient<PlanEnvelopesPage>();
        builder.Services.AddTransient<ReconcileAccountPage>();
        builder.Services.AddTransient<QuickAddTransactionPage>();
        builder.Services.AddTransient<InboxPage>();
        builder.Services.AddTransient<CategoryRulesPage>();
        builder.Services.AddTransient<AddCategoryRulePage>();
        builder.Services.AddTransient<CategoriesPage>();
        builder.Services.AddTransient<AddCategoryPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        app.Services.ApplyMigrations();
        app.Services.SeedDefaultData();

        return app;
    }
}
