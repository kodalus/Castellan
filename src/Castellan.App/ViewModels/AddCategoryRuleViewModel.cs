using System.Collections.ObjectModel;
using Castellan.Application;
using Castellan.Application.Repositories;
using Castellan.Domain.Aggregates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class AddCategoryRuleViewModel : ObservableObject
{
    private readonly ICategoryRuleRepository _rules;
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _uow;

    public ObservableCollection<CategoryOption> Categories { get; } = [];
    [ObservableProperty] private string _pattern = "";
    [ObservableProperty] private int _categoryIndex = -1;

    public AddCategoryRuleViewModel(ICategoryRuleRepository rules, ICategoryRepository categories, IUnitOfWork uow)
    {
        _rules = rules;
        _categories = categories;
        _uow = uow;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var cats = await _categories.ListAsync(ct);
        Categories.Clear();
        foreach (var c in cats.Where(c => !c.IsSystem && !c.IsArchived))
            Categories.Add(new CategoryOption(c.Id, c.Name));
        if (Categories.Count > 0) CategoryIndex = 0;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Pattern)) return;
        if (CategoryIndex < 0 || CategoryIndex >= Categories.Count) return;

        var rule = CategoryRule.Create(Pattern, Categories[CategoryIndex].Id);
        await _rules.AddAsync(rule, ct);
        await _uow.SaveChangesAsync(ct);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
