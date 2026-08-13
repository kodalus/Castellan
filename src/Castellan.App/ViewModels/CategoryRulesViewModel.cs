using System.Collections.ObjectModel;
using Castellan.Application;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record CategoryRuleRow(CategoryRuleId Id, string Pattern, string CategoryName);

public partial class CategoryRulesViewModel : ObservableObject
{
    private readonly ICategoryRuleRepository _rules;
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _uow;

    public ObservableCollection<CategoryRuleRow> Rules { get; } = [];
    [ObservableProperty] private bool _isEmpty = true;

    public CategoryRulesViewModel(ICategoryRuleRepository rules, ICategoryRepository categories, IUnitOfWork uow)
    {
        _rules = rules;
        _categories = categories;
        _uow = uow;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var ruleList = await _rules.ListAsync(ct);
            var catList = await _categories.ListAsync(ct);
            var catMap = catList.ToDictionary(c => c.Id, c => c.Name);

            Rules.Clear();
            foreach (var r in ruleList)
            {
                var catName = catMap.TryGetValue(r.CategoryId, out var n) ? n : "?";
                Rules.Add(new CategoryRuleRow(r.Id, r.Pattern, catName));
            }
            IsEmpty = Rules.Count == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[CategoryRules.Load] " + ex);
        }
    }

    [RelayCommand]
    private static async Task AddRuleAsync()
        => await Shell.Current.GoToAsync("addCategoryRule");

    [RelayCommand]
    private async Task DeleteRuleAsync(CategoryRuleRow row, CancellationToken ct = default)
    {
        var allRules = await _rules.ListAsync(ct);
        var rule = allRules.FirstOrDefault(r => r.Id == row.Id);
        if (rule is null) return;
        await _rules.RemoveAsync(rule, ct);
        await _uow.SaveChangesAsync(ct);
        Rules.Remove(row);
        IsEmpty = Rules.Count == 0;
    }
}
