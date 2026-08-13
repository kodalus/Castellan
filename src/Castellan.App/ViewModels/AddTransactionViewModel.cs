using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record AccountOption(AccountId Id, string Name);
public sealed record CategoryOption(CategoryId Id, string Name);

public partial class AddTransactionViewModel : ObservableObject
{
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly AddManualTransactionUseCase _addTx;

    public ObservableCollection<AccountOption> AccountOptions { get; } = [];
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = [];

    [ObservableProperty] private int _accountIndex = -1;
    [ObservableProperty] private int _categoryIndex = -1;
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string? _note;

    public AddTransactionViewModel(
        IAccountRepository accounts,
        ICategoryRepository categories,
        AddManualTransactionUseCase addTx)
    {
        _accounts = accounts;
        _categories = categories;
        _addTx = addTx;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        AccountOptions.Clear();
        CategoryOptions.Clear();

        var accounts = await _accounts.ListAsync(ct);
        foreach (var a in accounts) AccountOptions.Add(new AccountOption(a.Id, a.Name));

        var categories = await _categories.ListAsync(ct);
        foreach (var c in categories.Where(c => !c.IsSystem && !c.IsArchived))
            CategoryOptions.Add(new CategoryOption(c.Id, c.Name));

        if (AccountOptions.Count > 0) AccountIndex = 0;
        if (CategoryOptions.Count > 0) CategoryIndex = 0;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (AccountIndex < 0 || AccountIndex >= AccountOptions.Count) return;
        if (!decimal.TryParse(AmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec)) return;

        var grosze = (long)Math.Round(dec * 100, MidpointRounding.AwayFromZero);
        var accountId = AccountOptions[AccountIndex].Id;

        CategoryId categoryId;
        if (CategoryIndex >= 0 && CategoryIndex < CategoryOptions.Count)
            categoryId = CategoryOptions[CategoryIndex].Id;
        else
            categoryId = Category.UnsortedId;

        var occurredAt = new DateTimeOffset(Date.ToUniversalTime(), TimeSpan.Zero);

        await _addTx.ExecuteAsync(
            new AddManualTransactionUseCase.Input(accountId, new Money(grosze), occurredAt, categoryId, Note), ct);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
