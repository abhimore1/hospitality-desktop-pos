using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;

namespace Hospitality.Pos.Desktop.ViewModels;

public sealed class KitchenItem
{
    public required string Name { get; init; }
    public int Quantity { get; init; }
    public bool IsVeg { get; init; }
    public string QuantityText => $"{Quantity}×";
    public Brush VegBrush => Ui.Brush(IsVeg ? "#16A34A" : "#B91C1C");
}

public sealed class KitchenTicket
{
    public required Models.PosOrder Order { get; init; }
    public string OrderId => Order.Id;
    public string BillNo => Order.BillNo;
    public string TableLabel => string.IsNullOrEmpty(Order.TableName) ? Order.OrderType : $"Table {Order.TableName}";
    public string OrderType => Order.OrderType;
    public string TimeText => Order.CreatedAt.ToString("h:mm tt");
    public string ElapsedText => FormatElapsed(DateTime.Now - Order.CreatedAt);
    public string StatusLabel => Order.KotStatus switch
    {
        "new" => "New",
        "preparing" => "Preparing",
        "ready" => "Ready",
        _ => "Served"
    };
    public Brush AccentBrush => Ui.Brush(Order.KotStatus switch
    {
        "new" => "#0284C7",
        "preparing" => "#B45309",
        "ready" => "#15803D",
        _ => "#64748B"
    });
    public Brush AccentSoft => Ui.Brush(Order.KotStatus switch
    {
        "new" => "#E0F2FE",
        "preparing" => "#FEF3C7",
        "ready" => "#DCFCE7",
        _ => "#F1F5F9"
    });
    public string NextStatus => Order.KotStatus switch
    {
        "new" => "preparing",
        "preparing" => "ready",
        _ => "served"
    };
    public string NextLabel => Order.KotStatus switch
    {
        "new" => "Start preparing",
        "preparing" => "Mark ready",
        _ => "Mark served"
    };
    public List<KitchenItem> Items { get; init; } = [];
    public string ItemSummary => $"{Items.Sum(item => item.Quantity)} items";

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

public sealed class KitchenColumn
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required Brush Accent { get; init; }
    public required Brush AccentSoft { get; init; }
    public ObservableCollection<KitchenTicket> Tickets { get; } = [];
    public string CountText => $"{Tickets.Count} ticket{(Tickets.Count == 1 ? "" : "s")}";
}

public partial class KitchenViewModel : ObservableObject, IRefreshable
{
    private readonly OrderRepository _orders;
    private readonly DataChangeNotifier _notifier;
    private readonly ToastService _toasts;

    public KitchenViewModel(OrderRepository orders, DataChangeNotifier notifier, ToastService toasts)
    {
        _orders = orders;
        _notifier = notifier;
        _toasts = toasts;

        Columns = new ObservableCollection<KitchenColumn>
        {
            new() { Key = "new", Title = "New", Subtitle = "Waiting to start", Accent = Ui.Brush("#0284C7"), AccentSoft = Ui.Brush("#E0F2FE") },
            new() { Key = "preparing", Title = "Preparing", Subtitle = "On the line", Accent = Ui.Brush("#B45309"), AccentSoft = Ui.Brush("#FEF3C7") },
            new() { Key = "ready", Title = "Ready", Subtitle = "Pick up now", Accent = Ui.Brush("#15803D"), AccentSoft = Ui.Brush("#DCFCE7") }
        };

        _notifier.Changed += OnDataChanged;
        Refresh();
    }

    public ObservableCollection<KitchenColumn> Columns { get; }
    [ObservableProperty] private int _openTicketCount;
    [ObservableProperty] private string _openCountText = "0";
    [ObservableProperty] private string _preparingCountText = "0";
    [ObservableProperty] private string _readyCountText = "0";
    [ObservableProperty] private string _averageTimeText = "—";
    [ObservableProperty] private bool _isEmpty = true;

    public void Refresh()
    {
        var orders = _orders.GetKitchenOrders().Where(order => order.KotStatus != "served").ToList();

        foreach (var column in Columns)
        {
            column.Tickets.Clear();
        }

        foreach (var order in orders)
        {
            var status = order.KotStatus is "new" or "preparing" or "ready" ? order.KotStatus : "new";
            var column = Columns.First(item => item.Key == status);
            column.Tickets.Add(new KitchenTicket
            {
                Order = order,
                Items = order.Items.Select(item => new KitchenItem
                {
                    Name = item.Name,
                    Quantity = item.Qty,
                    IsVeg = item.IsVeg
                }).ToList()
            });
        }

        OpenTicketCount = orders.Count;
        OpenCountText = orders.Count.ToString();
        PreparingCountText = Columns.First(column => column.Key == "preparing").Tickets.Count.ToString();
        ReadyCountText = Columns.First(column => column.Key == "ready").Tickets.Count.ToString();
        IsEmpty = orders.Count == 0;

        var oldest = orders.OrderBy(order => order.CreatedAt).FirstOrDefault();
        AverageTimeText = oldest is null
            ? "No live tickets"
            : $"Oldest {DateTime.Now.Subtract(oldest.CreatedAt).TotalMinutes:0} min";
    }

    [RelayCommand]
    private void Advance(KitchenTicket? ticket)
    {
        if (ticket is null)
        {
            return;
        }

        _orders.AdvanceKitchenStatus(ticket.OrderId, ticket.NextStatus);
        _toasts.Success(ticket.NextStatus switch
        {
            "preparing" => $"{ticket.BillNo} started.",
            "ready" => $"{ticket.BillNo} is ready for pickup.",
            _ => $"{ticket.BillNo} served."
        });
        Refresh();
    }

    private void OnDataChanged() => Refresh();
}
