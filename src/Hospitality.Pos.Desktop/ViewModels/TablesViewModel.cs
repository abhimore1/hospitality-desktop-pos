using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;

namespace Hospitality.Pos.Desktop.ViewModels;

public sealed class TableCard
{
    public required Models.DiningTable Table { get; init; }
    public string Name => Table.Name;
    public string Section => Table.Section;
    public string SeatsText => $"{Table.Seats} seats";
    public bool IsOccupied => Table.IsOccupied;
    public string StatusLabel => IsOccupied ? "Occupied" : "Available";
    public string ActionLabel => IsOccupied ? "Open bill" : "New order";
    public string AmountText => IsOccupied ? Services.Inr.Format(Table.OpenAmountPaise) : "—";
    public string ElapsedText => Table.OpenedAt is null
        ? "Ready to seat"
        : $"Seated {FormatElapsed(DateTime.Now - Table.OpenedAt.Value)}";
    public Brush StatusBrush => Ui.Brush(IsOccupied ? "#B45309" : "#15803D");
    public Brush StatusBackground => Ui.Brush(IsOccupied ? "#FEF3C7" : "#DCFCE7");
    public Brush AccentBrush => Ui.Brush(IsOccupied ? "#F59E0B" : "#22C55E");

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes} min ago";
        }

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m ago";
    }
}

public sealed class TableSection
{
    public required string Name { get; init; }
    public ObservableCollection<TableCard> Tables { get; } = [];
    public string Summary => $"{Tables.Count(card => card.IsOccupied)} occupied • {Tables.Count(card => !card.IsOccupied)} free";
}

public partial class TablesViewModel : ObservableObject, IRefreshable
{
    private readonly TableRepository _tableRepository;
    private readonly AppNavigation _navigation;
    private readonly DataChangeNotifier _notifier;

    public TablesViewModel(
        TableRepository tableRepository,
        AppNavigation navigation,
        DataChangeNotifier notifier)
    {
        _tableRepository = tableRepository;
        _navigation = navigation;
        _notifier = notifier;
        _notifier.Changed += Refresh;
        Refresh();
    }

    public ObservableCollection<TableSection> Sections { get; } = [];

    [ObservableProperty] private string _occupiedText = "0";
    [ObservableProperty] private string _availableText = "0";
    [ObservableProperty] private string _openAmountText = "₹0";
    [ObservableProperty] private string _totalText = "0 tables";

    public void Refresh()
    {
        var tables = _tableRepository.GetTables();
        Sections.Clear();

        foreach (var group in tables.GroupBy(table => table.Section))
        {
            var section = new TableSection { Name = group.Key };
            foreach (var table in group.OrderBy(table => table.Name))
            {
                section.Tables.Add(new TableCard { Table = table });
            }

            Sections.Add(section);
        }

        var occupied = tables.Count(table => table.IsOccupied);
        OccupiedText = occupied.ToString();
        AvailableText = (tables.Count - occupied).ToString();
        OpenAmountText = Services.Inr.Format(tables.Sum(table => table.OpenAmountPaise));
        TotalText = $"{tables.Count} tables";
    }

    [RelayCommand]
    private void OpenTable(TableCard? card)
    {
        if (card is null)
        {
            return;
        }

        _navigation.Navigate("Billing", card.Table.Id);
    }
}
