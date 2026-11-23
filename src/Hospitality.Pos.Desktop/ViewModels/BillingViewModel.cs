using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Domain;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;
using System.Windows.Media;

namespace Hospitality.Pos.Desktop.ViewModels;

public partial class CartLine : ObservableObject
{
    public CartLine(MenuItem item, int quantity = 1)
    {
        Item = item;
        Quantity = quantity;
    }

    public MenuItem Item { get; }
    [ObservableProperty] private int _quantity;

    public long GrossPaise => Item.PricePaise * Quantity;
    public string GrossText => Services.Inr.Format(GrossPaise);
    public string UnitPriceText => Item.PriceText;
    public Brush VegBrush => Ui.Brush(Item.IsVeg ? "#16A34A" : "#B91C1C");
    public string Name => Item.Name;

    partial void OnQuantityChanged(int value)
    {
        OnPropertyChanged(nameof(GrossPaise));
        OnPropertyChanged(nameof(GrossText));
    }
}

public partial class TableChip : ObservableObject
{
    public required DiningTable Table { get; init; }
    public string Label => Table.Name;
    public bool IsOccupied => Table.IsOccupied;
    public string Detail => IsOccupied ? "Open bill" : $"{Table.Seats} seats";
    [ObservableProperty] private bool _isSelected;
}

public partial class BillingViewModel : ObservableObject, IRefreshable
{
    private readonly MenuRepository _menuRepository;
    private readonly TableRepository _tableRepository;
    private readonly OrderRepository _orderRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly AppSession _session;
    private readonly ToastService _toasts;
    private readonly DataChangeNotifier _notifier;
    private readonly IBillPresenter _billPresenter;

    private readonly List<MenuItem> _allItems = [];
    private PosSettings _config = new();
    private OrderTotals _totals = new(0, 0, 0, 0, 0, 0, 0, 0);
    private int? _selectedTableId;

    public BillingViewModel(
        MenuRepository menuRepository,
        TableRepository tableRepository,
        OrderRepository orderRepository,
        SettingsRepository settingsRepository,
        AppSession session,
        ToastService toasts,
        DataChangeNotifier notifier,
        IBillPresenter billPresenter)
    {
        _menuRepository = menuRepository;
        _tableRepository = tableRepository;
        _orderRepository = orderRepository;
        _settingsRepository = settingsRepository;
        _session = session;
        _toasts = toasts;
        _notifier = notifier;
        _billPresenter = billPresenter;

        OrderTypes = new ObservableCollection<SelectableChip>
        {
            new() { Key = "Dine-in", Label = "Dine-in" },
            new() { Key = "Takeaway", Label = "Takeaway" },
            new() { Key = "Delivery", Label = "Delivery" }
        };
        OrderTypes[0].IsSelected = true;
        OrderType = "Dine-in";

        PaymentMethods = new ObservableCollection<SelectableChip>
        {
            new() { Key = "UPI", Label = "UPI", Icon = Ui.Icon("IconUpi") },
            new() { Key = "Cash", Label = "Cash", Icon = Ui.Icon("IconCash") },
            new() { Key = "Card", Label = "Card", Icon = Ui.Icon("IconCard") }
        };
        PaymentMethods[0].IsSelected = true;

        notifier.Changed += () =>
        {
            _config = _settingsRepository.Load();
            RefreshTables();
        };

        Refresh();
    }

    public ObservableCollection<SelectableChip> Categories { get; } = [];
    public ObservableCollection<SelectableChip> OrderTypes { get; }
    public ObservableCollection<SelectableChip> PaymentMethods { get; }
    public ObservableCollection<MenuItem> MenuItems { get; } = [];
    public ObservableCollection<CartLine> Cart { get; } = [];
    public ObservableCollection<TableChip> TableChips { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _orderType = "Dine-in";
    [ObservableProperty] private bool _isDineIn = true;
    [ObservableProperty] private string _orderTitle = "New order";
    [ObservableProperty] private string _contextText = "Dine-in • select a table";
    [ObservableProperty] private bool _isExistingOrder;
    [ObservableProperty] private string _existingOrderNote = "";
    [ObservableProperty] private string? _currentOrderId;
    [ObservableProperty] private int _discountPercent;
    [ObservableProperty] private string _discountLabel = "No discount";
    [ObservableProperty] private bool _isPaymentOpen;
    [ObservableProperty] private string _visibleCountText = "";
    [ObservableProperty] private bool _hasCart;

    [ObservableProperty] private string _grossText = "₹0";
    [ObservableProperty] private string _discountText = "₹0";
    [ObservableProperty] private string _taxableText = "₹0";
    [ObservableProperty] private string _cgstText = "₹0";
    [ObservableProperty] private string _sgstText = "₹0";
    [ObservableProperty] private string _roundOffText = "₹0";
    [ObservableProperty] private string _totalText = "₹0";
    [ObservableProperty] private string _payableText = "₹0";
    [ObservableProperty] private string _taxNote = "";
    [ObservableProperty] private string _cartCountText = "0 items";
    [ObservableProperty] private string _gstLabel = "GST 5%";

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public void Refresh()
    {
        _config = _settingsRepository.Load();
        GstLabel = $"GST {_config.GstRateBps / 100m:0.##}%";
        TaxNote = _config.PricesIncludeTax
            ? $"Menu prices include {_config.GstRateBps / 100m:0.##}% GST"
            : $"{_config.GstRateBps / 100m:0.##}% GST is added at checkout";

        if (Categories.Count == 0)
        {
            LoadCategories();
        }

        if (_allItems.Count == 0)
        {
            LoadItems();
        }

        RefreshTables();
        Recalculate();
    }

    public void Prepare(int tableId)
    {
        if (Cart.Count > 0 && _selectedTableId is not null && _selectedTableId != tableId)
        {
            _toasts.Warning("Finish or clear the current bill before switching tables.");
            return;
        }

        if (Cart.Count > 0 && _selectedTableId is null && IsExistingOrder)
        {
            _toasts.Warning("Settle the open bill first.");
            return;
        }

        SelectOrderType("Dine-in");
        LoadTable(tableId);
    }

    public void PrepareNew()
    {
        if (Cart.Count == 0)
        {
            CurrentOrderId = null;
            IsExistingOrder = false;
            _selectedTableId = null;
            UpdateSelection();
            OrderTitle = "New order";
        }
    }

    [RelayCommand]
    private void SelectCategory(SelectableChip chip)
    {
        foreach (var category in Categories)
        {
            category.IsSelected = category == chip;
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void AddItem(MenuItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!item.IsAvailable)
        {
            _toasts.Warning($"{item.Name} is marked sold out.");
            return;
        }

        var line = Cart.FirstOrDefault(cartLine => cartLine.Item.Id == item.Id);
        if (line is null)
        {
            Cart.Add(new CartLine(item));
        }
        else
        {
            line.Quantity++;
        }

        Recalculate();
    }

    [RelayCommand]
    private void IncreaseLine(CartLine? line)
    {
        if (line is not null)
        {
            line.Quantity++;
            Recalculate();
        }
    }

    [RelayCommand]
    private void DecreaseLine(CartLine? line)
    {
        if (line is null)
        {
            return;
        }

        line.Quantity--;
        if (line.Quantity <= 0)
        {
            Cart.Remove(line);
        }

        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(CartLine? line)
    {
        if (line is not null)
        {
            Cart.Remove(line);
            Recalculate();
        }
    }

    [RelayCommand]
    private void ClearCart()
    {
        if (Cart.Count == 0)
        {
            return;
        }

        if (IsExistingOrder)
        {
            _toasts.Warning("The open bill was cleared. Its items are still saved in the kitchen order.");
        }

        Cart.Clear();
        CurrentOrderId = null;
        IsExistingOrder = false;
        ExistingOrderNote = "";
        Recalculate();
    }

    [RelayCommand]
    private void SelectOrderType(SelectableChip chip) => SelectOrderType(chip.Key);

    private void SelectOrderType(string key)
    {
        foreach (var type in OrderTypes)
        {
            type.IsSelected = type.Key == key;
        }

        OrderType = key;
        IsDineIn = key == "Dine-in";

        if (!IsDineIn)
        {
            _selectedTableId = null;
        }

        UpdateSelection();
    }

    [RelayCommand]
    private void SelectTable(TableChip? chip)
    {
        if (chip is null)
        {
            return;
        }

        if (IsExistingOrder && chip.Table.Id != _selectedTableId)
        {
            if (chip.IsOccupied)
            {
                _toasts.Warning("That table already has an open bill. Settle it from the Tables screen.");
                UpdateSelection();
                return;
            }

            _toasts.Warning("Settle or clear the loaded bill before moving to another table.");
            UpdateSelection();
            return;
        }

        if (Cart.Count > 0 && !IsExistingOrder && chip.IsOccupied)
        {
            _toasts.Warning("That table has an open bill. Pick an available table or open it from Tables.");
            UpdateSelection();
            return;
        }

        LoadTable(chip.Table.Id);
    }

    private void LoadTable(int tableId)
    {
        var table = _tableRepository.GetTables().FirstOrDefault(item => item.Id == tableId);
        if (table is null)
        {
            return;
        }

        _selectedTableId = tableId;

        var order = _orderRepository.GetOpenOrderForTable(tableId);
        if (order is not null)
        {
            Cart.Clear();
            CurrentOrderId = order.Id;
            IsExistingOrder = true;
            ExistingOrderNote = $"Open bill {order.BillNo} loaded — settle it here or add more items.";
            OrderTitle = $"Open bill {order.BillNo}";

            foreach (var item in order.Items)
            {
                var menuItem = _allItems.FirstOrDefault(menu => menu.Id == item.MenuItemId)
                    ?? new MenuItem
                    {
                        Id = item.MenuItemId,
                        Name = item.Name,
                        PricePaise = item.UnitPricePaise,
                        TaxBps = item.TaxBps,
                        IsVeg = item.IsVeg,
                        IsAvailable = true
                    };

                Cart.Add(new CartLine(menuItem, item.Qty));
            }

            var gross = Cart.Sum(line => line.GrossPaise);
            DiscountPercent = gross > 0
                ? (int)Math.Round(order.DiscountPaise * 100m / gross, MidpointRounding.AwayFromZero)
                : 0;
            _toasts.Info($"Loaded open bill {order.BillNo}.");
        }
        else if (!IsExistingOrder)
        {
            Cart.Clear();
            CurrentOrderId = null;
            OrderTitle = "New order";
            ExistingOrderNote = "";
            DiscountPercent = 0;
        }

        UpdateSelection();
        Recalculate();
    }

    [RelayCommand]
    private void ApplyDiscount(string? percentText)
    {
        DiscountPercent = int.TryParse(percentText, out var percent) ? percent : 0;
        Recalculate();
    }

    [RelayCommand]
    private void SendKitchen()
    {
        if (Cart.Count == 0)
        {
            _toasts.Warning("Add at least one item first.");
            return;
        }

        if (IsExistingOrder)
        {
            _toasts.Warning("This bill is already in the kitchen. Settle it from the payment panel.");
            return;
        }

        if (!ValidateTable())
        {
            return;
        }

        var order = _orderRepository.CreateOpenOrder(
            OrderType,
            _selectedTableId,
            _totals,
            BuildLineInputs());

        _toasts.Success($"{order.BillNo} sent to the kitchen.");
        Cart.Clear();
        CurrentOrderId = null;
        IsExistingOrder = false;
        _selectedTableId = null;
        OrderTitle = "New order";
        ExistingOrderNote = "";
        DiscountPercent = 0;
        Recalculate();
        RefreshTables();
    }

    [RelayCommand]
    private void OpenPayment()
    {
        if (Cart.Count == 0)
        {
            _toasts.Warning("Add at least one item before taking payment.");
            return;
        }

        if (!ValidateTable())
        {
            return;
        }

        IsPaymentOpen = true;
    }

    [RelayCommand]
    private void CancelPayment() => IsPaymentOpen = false;

    [RelayCommand]
    private void SelectPayment(SelectableChip chip)
    {
        foreach (var method in PaymentMethods)
        {
            method.IsSelected = method == chip;
        }
    }

    [RelayCommand]
    private void ConfirmPayment()
    {
        if (Cart.Count == 0)
        {
            IsPaymentOpen = false;
            return;
        }

        if (!ValidateTable())
        {
            IsPaymentOpen = false;
            return;
        }

        var method = PaymentMethods.FirstOrDefault(item => item.IsSelected)?.Key ?? "UPI";
        var lines = BuildLineInputs();
        PosOrder? order;

        if (IsExistingOrder && CurrentOrderId is not null)
        {
            order = _orderRepository.PayOpenOrder(CurrentOrderId, method, _totals, lines);
        }
        else
        {
            order = _orderRepository.CreatePaidOrder(OrderType, _selectedTableId, _totals, lines, method);
        }

        if (order is null)
        {
            _toasts.Error("The bill could not be saved. Please try again.");
            return;
        }

        var bill = BillFactory.Create(order, _config);
        IsPaymentOpen = false;
        Cart.Clear();
        CurrentOrderId = null;
        IsExistingOrder = false;
        _selectedTableId = null;
        OrderTitle = "New order";
        ExistingOrderNote = "";
        DiscountPercent = 0;
        Recalculate();
        RefreshTables();

        _toasts.Success($"{order.BillNo} settled — {Services.Inr.Format(order.TotalPaise)} via {method}.");
        _billPresenter.Show(bill);
    }

    [RelayCommand]
    private void ClearDiscount() => ApplyDiscount("0");

    private bool ValidateTable()
    {
        if (!IsDineIn)
        {
            return true;
        }

        if (_selectedTableId is null)
        {
            _toasts.Warning("Select a table for this dine-in order.");
            return false;
        }

        return true;
    }

    private void Recalculate()
    {
        var taxLines = Cart.Select(line => new TaxLine(line.Item.PricePaise, line.Quantity, line.Item.TaxBps)).ToList();
        var gross = taxLines.Sum(line => (long)line.UnitPricePaise * line.Quantity);
        var discount = TaxCalculator.PercentDiscount(gross, DiscountPercent);
        _totals = TaxCalculator.Calculate(taxLines, discount, _config.PricesIncludeTax);

        GrossText = Services.Inr.Format(_totals.GrossPaise);
        DiscountText = DiscountPercent == 0 ? "₹0" : $"- {Services.Inr.Format(_totals.DiscountPaise)}";
        TaxableText = Services.Inr.Format(_totals.TaxablePaise);
        CgstText = Services.Inr.Format(_totals.CgstPaise);
        SgstText = Services.Inr.Format(_totals.SgstPaise);
        RoundOffText = FormatSigned(_totals.RoundOffPaise);
        TotalText = Services.Inr.Format(_totals.TotalPaise);
        PayableText = TotalText;
        DiscountLabel = DiscountPercent == 0 ? "No discount" : $"{DiscountPercent}% off";
        HasCart = Cart.Count > 0;
        CartCountText = $"{Cart.Sum(line => line.Quantity)} item{(Cart.Sum(line => line.Quantity) == 1 ? "" : "s")}";

        if (IsExistingOrder && CurrentOrderId is not null)
        {
            ExistingOrderNote = $"{OrderTitle} • {CartCountText}";
        }

        UpdateSelection();
    }

    private List<OrderLineInput> BuildLineInputs() =>
        Cart.Select(line => new OrderLineInput(
            line.Item.Id,
            line.Item.Name,
            line.Quantity,
            line.Item.PricePaise,
            line.Item.TaxBps,
            line.Item.IsVeg,
            line.GrossPaise)).ToList();

    private void LoadCategories()
    {
        Categories.Clear();
        Categories.Add(new SelectableChip { Key = "0", Label = "All items", IsSelected = true });

        foreach (var category in _menuRepository.GetCategories())
        {
            Categories.Add(new SelectableChip { Key = category.Id.ToString(), Label = category.Name });
        }
    }

    private void LoadItems()
    {
        _allItems.Clear();
        _allItems.AddRange(_menuRepository.GetItems());
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var categoryId = int.TryParse(Categories.FirstOrDefault(category => category.IsSelected)?.Key, out var id)
            ? id
            : 0;

        var search = SearchText.Trim();
        var filtered = _allItems.Where(item =>
            (categoryId == 0 || item.CategoryId == categoryId) &&
            (search.Length == 0 ||
             item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             item.Description.Contains(search, StringComparison.OrdinalIgnoreCase)));

        MenuItems.Clear();
        foreach (var item in filtered)
        {
            MenuItems.Add(item);
        }

        VisibleCountText = $"{MenuItems.Count} item{(MenuItems.Count == 1 ? "" : "s")}";
    }

    private void RefreshTables()
    {
        var selectedId = _selectedTableId;
        var tables = _tableRepository.GetTables();

        TableChips.Clear();
        foreach (var table in tables)
        {
            TableChips.Add(new TableChip
            {
                Table = table,
                IsSelected = table.Id == selectedId
            });
        }

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        foreach (var chip in TableChips)
        {
            chip.IsSelected = chip.Table.Id == _selectedTableId;
        }

        if (!IsDineIn)
        {
            ContextText = $"{OrderType} order";
            return;
        }

        var table = TableChips.FirstOrDefault(chip => chip.Table.Id == _selectedTableId);
        ContextText = table is null
            ? "Dine-in • select a table"
            : $"Dine-in • {table.Label}";

        if (table is not null && !IsExistingOrder)
        {
            OrderTitle = Cart.Count == 0 ? $"New order • {table.Label}" : $"New order • {table.Label}";
        }
    }

    private static string FormatSigned(long paise)
    {
        if (paise == 0)
        {
            return "₹0";
        }

        return $"{(paise > 0 ? "+" : "−")} {Services.Inr.Format(Math.Abs(paise))}";
    }
}
