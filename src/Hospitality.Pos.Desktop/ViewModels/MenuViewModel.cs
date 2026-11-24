using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;
using Microsoft.Win32;

namespace Hospitality.Pos.Desktop.ViewModels;

public partial class MenuViewModel : ObservableObject, IRefreshable
{
    private readonly MenuRepository _menuRepository;
    private readonly ToastService _toasts;

    public MenuViewModel(MenuRepository menuRepository, ToastService toasts)
    {
        _menuRepository = menuRepository;
        _toasts = toasts;
        Refresh();
    }

    public ObservableCollection<SelectableChip> Categories { get; } = [];
    public ObservableCollection<MenuItem> Items { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _itemCountText = "0 items";
    [ObservableProperty] private string _availableCountText = "";
    [ObservableProperty] private bool _isEmpty;

    partial void OnSearchTextChanged(string value) => LoadItems();

    public void Refresh()
    {
        var currentCategory = Categories.FirstOrDefault(chip => chip.IsSelected)?.Key ?? "0";
        var categories = _menuRepository.GetCategories();

        Categories.Clear();
        Categories.Add(new SelectableChip { Key = "0", Label = "All items", IsSelected = currentCategory == "0" });

        foreach (var category in categories)
        {
            Categories.Add(new SelectableChip
            {
                Key = category.Id.ToString(),
                Label = category.Name,
                IsSelected = currentCategory == category.Id.ToString()
            });
        }

        if (Categories.All(chip => !chip.IsSelected))
        {
            Categories[0].IsSelected = true;
        }

        LoadItems();
    }

    [RelayCommand]
    private void SelectCategory(SelectableChip chip)
    {
        foreach (var category in Categories)
        {
            category.IsSelected = category == chip;
        }

        LoadItems();
    }

    [RelayCommand]
    private void ToggleAvailability(MenuItem? item)
    {
        if (item is null)
        {
            return;
        }

        _menuRepository.SetAvailability(item.Id, !item.IsAvailable);
        _toasts.Success(item.IsAvailable
            ? $"{item.Name} marked sold out."
            : $"{item.Name} is back on the menu.");
        LoadItems();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"menu-catalog-{DateTime.Today:yyyy-MM-dd}.csv",
            Title = "Export menu catalog"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var csv = _menuRepository.ExportMenuCsv();
            File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(true));
            _toasts.Success("Menu catalog exported to CSV.");
        }
        catch (Exception ex)
        {
            _toasts.Error($"Export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ImportCsv()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            Title = "Import menu catalog"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var csv = File.ReadAllText(dialog.FileName);
            if (_menuRepository.ImportMenuCsv(csv, out var added, out var updated, out var error))
            {
                _toasts.Success($"Menu imported: {added} new, {updated} updated.");
                Refresh();
            }
            else
            {
                _toasts.Error($"Import failed: {error}");
            }
        }
        catch (Exception ex)
        {
            _toasts.Error($"Import error: {ex.Message}");
        }
    }

    private void LoadItems()
    {
        var categoryId = int.TryParse(Categories.FirstOrDefault(chip => chip.IsSelected)?.Key, out var id) ? id : 0;
        var items = _menuRepository.GetItems(SearchText, categoryId);

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        ItemCountText = $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
        AvailableCountText = $"{Items.Count(item => item.IsAvailable)} available";
        IsEmpty = Items.Count == 0;
    }
}
