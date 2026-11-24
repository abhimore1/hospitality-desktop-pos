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
    public bool IsLongOpen => IsOccupied && Table.OpenedAt is not null && DateTime.Now - Table.OpenedAt.Value > TimeSpan.FromHours(2);
    public string StatusLabel => IsLongOpen ? "Over 2h" : IsOccupied ? "Occupied" : "Available";
    public string ActionLabel => IsOccupied ? "Open bill" : "Start order";
    public string AmountText => IsOccupied ? Services.Inr.Format(Table.OpenAmountPaise) : "—";
    public string ElapsedText => IsOccupied && Table.OpenedAt is not null
        ? $"Open {FormatElapsed(DateTime.Now - Table.OpenedAt.Value)}"
        : "";
    public Brush StatusBrush => Ui.Brush(IsLongOpen ? "#B91C1C" : IsOccupied ? "#B45309" : "#15803D");
    public Brush StatusBackground => Ui.Brush(IsLongOpen ? "#FEE2E2" : IsOccupied ? "#FEF3C7" : "#DCFCE7");
    public Brush AccentBrush => Ui.Brush(IsLongOpen ? "#DC2626" : IsOccupied ? "#F59E0B" : "#22C55E");

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes} min";
        }

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m";
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
    private readonly OrderRepository _orderRepository;
    private readonly AppNavigation _navigation;
    private readonly ToastService _toasts;
    private readonly DataChangeNotifier _notifier;

    public TablesViewModel(
        TableRepository tableRepository,
        OrderRepository orderRepository,
        AppNavigation navigation,
        ToastService toasts,
        DataChangeNotifier notifier)
    {
        _tableRepository = tableRepository;
        _orderRepository = orderRepository;
        _navigation = navigation;
        _toasts = toasts;
        _notifier = notifier;
        _notifier.Changed += Refresh;
        Refresh();
    }

    public ObservableCollection<TableSection> Sections { get; } = [];
    public ObservableCollection<TableCard> OccupiedTables { get; } = [];
    public ObservableCollection<TableCard> AvailableTargetTables { get; } = [];

    [ObservableProperty] private string _occupiedText = "0";
    [ObservableProperty] private string _availableText = "0";
    [ObservableProperty] private string _openAmountText = "₹0";
    [ObservableProperty] private string _totalText = "0 tables";
    [ObservableProperty] private string _longOpenText = "0 tables";
    [ObservableProperty] private bool _hasLongOpen;

    [ObservableProperty] private bool _isMergeDialogOpen;
    [ObservableProperty] private TableCard? _mergeSourceTable;
    [ObservableProperty] private TableCard? _mergeTargetTable;

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
        var longOpen = tables.Count(table =>
            table.IsOccupied && table.OpenedAt is not null && DateTime.Now - table.OpenedAt.Value > TimeSpan.FromHours(2));
        OccupiedText = occupied.ToString();
        AvailableText = (tables.Count - occupied).ToString();
        OpenAmountText = Services.Inr.Format(tables.Sum(table => table.OpenAmountPaise));
        TotalText = $"{tables.Count} tables";
        LongOpenText = longOpen == 1 ? "1 table" : $"{longOpen} tables";
        HasLongOpen = longOpen > 0;
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

    [RelayCommand]
    private void OpenMergeDialog(TableCard? card)
    {
        if (card is null || !card.IsOccupied)
        {
            _toasts.Warning("Select an occupied table to merge.");
            return;
        }

        MergeSourceTable = card;
        AvailableTargetTables.Clear();

        var allTables = _tableRepository.GetTables();
        foreach (var t in allTables)
        {
            if (t.Id != card.Table.Id)
            {
                AvailableTargetTables.Add(new TableCard { Table = t });
            }
        }

        MergeTargetTable = AvailableTargetTables.FirstOrDefault(t => t.IsOccupied) ?? AvailableTargetTables.FirstOrDefault();
        IsMergeDialogOpen = true;
    }

    [RelayCommand]
    private void ConfirmMerge()
    {
        if (MergeSourceTable is null || MergeTargetTable is null)
        {
            _toasts.Warning("Please select both source and target tables.");
            return;
        }

        if (_orderRepository.MergeTables(MergeSourceTable.Table.Id, MergeTargetTable.Table.Id, out var message))
        {
            _toasts.Success(message);
            IsMergeDialogOpen = false;
            Refresh();
        }
        else
        {
            _toasts.Error(message);
        }
    }

    [RelayCommand]
    private void CancelMergeDialog()
    {
        IsMergeDialogOpen = false;
        MergeSourceTable = null;
        MergeTargetTable = null;
    }
}
