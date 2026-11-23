namespace Hospitality.Pos.Desktop.Models;

public sealed class MenuCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#0F766E";
    public int SortOrder { get; set; }
}

public sealed class MenuItem
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long PricePaise { get; set; }
    public int TaxBps { get; set; } = 500;
    public bool IsVeg { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string HsnSac { get; set; } = "996331";
    public int SortOrder { get; set; }
    public string CategoryName { get; set; } = "";
    public string CategoryColor { get; set; } = "#0F766E";

    public string PriceText => Services.Inr.Format(PricePaise);
    public string TaxText => $"{TaxBps / 100m:0.##}% GST";
    public string AvailabilityState => IsAvailable ? "On the menu" : "Sold out";
    public string AvailabilityAction => IsAvailable ? "Mark sold out" : "Mark available";
    public string Initials => string.IsNullOrWhiteSpace(Name)
        ? "?"
        : string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();
}

public sealed class Staff
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string PinHash { get; set; } = "";
    public string Color { get; set; } = "#0F766E";
    public string Initials { get; set; } = "";

    public string RoleLabel => char.ToUpperInvariant(Role.Length > 0 ? Role[0] : ' ') + (Role.Length > 1 ? Role[1..] : "");
}

public sealed class InventoryItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Stock { get; set; }
    public double LowThreshold { get; set; }
    public string StockText => $"{Stock:0.##} {Unit}";
    public double StockRatio => LowThreshold <= 0 ? 1 : Math.Clamp(Stock / (LowThreshold * 2), 0, 1);
    public double StockPercent => Math.Round(StockRatio * 100);
}
