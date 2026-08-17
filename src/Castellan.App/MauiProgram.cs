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
        builder.Services.AddTransient<UpdateTransactionUseCase>();
        builder.Services.AddTransient<CreateTransferUseCase>();
        builder.Services.AddTransient<DeleteTransactionUseCase>();
        builder.Services.AddTransient<PlanMonthUseCase>();
        builder.Services.AddTransient<GetMonthOverviewUseCase>();
        builder.Services.AddTransient<GetAccountsWithBalancesUseCase>();
        builder.Services.AddTransient<ReconcileAccountUseCase>();
        builder.Services.AddTransient<IngestRawNotificationUseCase>();
        builder.Services.AddTransient<GetMonthlyStatsUseCase>();
        builder.Services.AddTransient<AssignCategoryUseCase>();
        builder.Services.AddTransient<GetTransferProposalsUseCase>();
        builder.Services.AddTransient<ConfirmTransferUseCase>();
        builder.Services.AddTransient<RejectTransferUseCase>();
        builder.Services.AddTransient<CreateFundUseCase>();
        builder.Services.AddTransient<UpdateFundUseCase>();
        builder.Services.AddTransient<DeleteFundUseCase>();
        builder.Services.AddTransient<ContributeToFundUseCase>();
        builder.Services.AddTransient<GetFundOverviewUseCase>();
        builder.Services.AddTransient<PayTransactionFromFundUseCase>();
        builder.Services.AddTransient<CreateAssetUseCase>();
        builder.Services.AddTransient<UpdateAssetValueUseCase>();
        builder.Services.AddTransient<GetCushionOverviewUseCase>();
        builder.Services.AddTransient<CreateDebtUseCase>();
        builder.Services.AddTransient<UpdateDebtUseCase>();
        builder.Services.AddTransient<DeleteDebtUseCase>();
        builder.Services.AddTransient<PayDebtInstallmentUseCase>();
        builder.Services.AddTransient<GetDebtOverviewUseCase>();
        builder.Services.AddTransient<SimulateDebtPayoffUseCase>();
        builder.Services.AddTransient<ApplyDebtPaymentUseCase>();
        builder.Services.AddTransient<CategoryLinkPrompt>();
        builder.Services.AddTransient<ExportDataUseCase>();
        builder.Services.AddTransient<ImportDataUseCase>();

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
        builder.Services.AddTransient<EditTransactionViewModel>();
        builder.Services.AddTransient<AddTransferViewModel>();
        builder.Services.AddTransient<PlanEnvelopesViewModel>();
        builder.Services.AddTransient<ReconcileAccountViewModel>();
        builder.Services.AddTransient<QuickAddTransactionViewModel>();
        builder.Services.AddTransient<InboxViewModel>();
        builder.Services.AddTransient<CategoryRulesViewModel>();
        builder.Services.AddTransient<AddCategoryRuleViewModel>();
        builder.Services.AddTransient<CategoriesViewModel>();
        builder.Services.AddTransient<AddCategoryViewModel>();
        builder.Services.AddTransient<StatisticsViewModel>();
        builder.Services.AddTransient<IncomeViewModel>();
        builder.Services.AddTransient<AssignCategoryViewModel>();
        builder.Services.AddTransient<FundsViewModel>();
        builder.Services.AddTransient<AddFundViewModel>();
        builder.Services.AddTransient<EditFundViewModel>();
        builder.Services.AddTransient<ContributeFundViewModel>();
        builder.Services.AddTransient<AssetsViewModel>();
        builder.Services.AddTransient<AddAssetViewModel>();
        builder.Services.AddTransient<UpdateAssetValueViewModel>();
        builder.Services.AddTransient<AddDebtViewModel>();
        builder.Services.AddTransient<EditDebtViewModel>();
        builder.Services.AddTransient<PayDebtViewModel>();
        builder.Services.AddTransient<DebtPlanViewModel>();
        builder.Services.AddTransient<BackupViewModel>();

        // Pages (tab pages are Transient so Shell reuses cached instances)
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AccountsPage>();
        builder.Services.AddTransient<TransactionsPage>();
        builder.Services.AddTransient<EnvelopesPage>();
        builder.Services.AddTransient<AddAccountPage>();
        builder.Services.AddTransient<AddTransactionPage>();
        builder.Services.AddTransient<EditTransactionPage>();
        builder.Services.AddTransient<AddTransferPage>();
        builder.Services.AddTransient<PlanEnvelopesPage>();
        builder.Services.AddTransient<ReconcileAccountPage>();
        builder.Services.AddTransient<QuickAddTransactionPage>();
        builder.Services.AddTransient<InboxPage>();
        builder.Services.AddTransient<CategoryRulesPage>();
        builder.Services.AddTransient<AddCategoryRulePage>();
        builder.Services.AddTransient<CategoriesPage>();
        builder.Services.AddTransient<AddCategoryPage>();
        builder.Services.AddTransient<StatisticsPage>();
        builder.Services.AddTransient<IncomePage>();
        builder.Services.AddTransient<AssignCategoryPage>();
        builder.Services.AddTransient<FundsPage>();
        builder.Services.AddTransient<AddFundPage>();
        builder.Services.AddTransient<EditFundPage>();
        builder.Services.AddTransient<ContributeFundPage>();
        builder.Services.AddTransient<AssetsPage>();
        builder.Services.AddTransient<AddAssetPage>();
        builder.Services.AddTransient<UpdateAssetValuePage>();
        builder.Services.AddTransient<AddDebtPage>();
        builder.Services.AddTransient<EditDebtPage>();
        builder.Services.AddTransient<PayDebtPage>();
        builder.Services.AddTransient<DebtPlanPage>();
        builder.Services.AddTransient<BackupPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        app.Services.ApplyMigrations();
        app.Services.SeedDefaultData();

        return app;
    }
}
