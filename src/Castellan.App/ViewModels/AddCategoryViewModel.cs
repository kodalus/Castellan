using Castellan.Application;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class AddCategoryViewModel : ObservableObject
{
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _uow;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _kindIndex = 0;

    public string[] Kinds { get; } = ["Wydatek", "Przychód"];

    public AddCategoryViewModel(ICategoryRepository categories, IUnitOfWork uow)
    {
        _categories = categories;
        _uow = uow;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Name)) return;
        var kind = KindIndex == 1 ? CategoryKind.Income : CategoryKind.Expense;
        var cat = Category.Create(Name.Trim(), kind);
        await _categories.AddAsync(cat, ct);
        await _uow.SaveChangesAsync(ct);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
