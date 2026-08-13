using System.Collections.ObjectModel;
using Castellan.Application;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record CategoryRow(CategoryId Id, string Name, string KindDisplay);

public partial class CategoriesViewModel : ObservableObject
{
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _uow;

    public ObservableCollection<CategoryRow> Items { get; } = [];
    [ObservableProperty] private bool _isEmpty = true;

    public CategoriesViewModel(ICategoryRepository categories, IUnitOfWork uow)
    {
        _categories = categories;
        _uow = uow;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var list = await _categories.ListAsync(ct);
            Items.Clear();
            foreach (var c in list.Where(c => !c.IsSystem && !c.IsArchived))
                Items.Add(new CategoryRow(c.Id, c.Name, c.Kind == CategoryKind.Expense ? "Wydatek" : "Przychód"));
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Categories.Load] " + ex);
        }
    }

    [RelayCommand]
    private static async Task AddCategoryAsync()
        => await Shell.Current.GoToAsync("addCategory");

    [RelayCommand]
    private async Task ArchiveCategoryAsync(CategoryRow row, CancellationToken ct = default)
    {
        var all = await _categories.ListAsync(ct);
        var cat = all.FirstOrDefault(c => c.Id == row.Id);
        if (cat is null) return;
        cat.Archive();
        await _uow.SaveChangesAsync(ct);
        Items.Remove(row);
        IsEmpty = Items.Count == 0;
    }
}
