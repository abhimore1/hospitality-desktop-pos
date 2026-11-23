using Hospitality.Pos.Desktop.Models;

namespace Hospitality.Pos.Desktop.Services;

public interface IBillPresenter
{
    void Show(BillDocument document);
}

public sealed class PosSettings
{
    public string OutletName { get; set; } = "Spice Route Kitchen";
    public string Address { get; set; } = "12, FC Road, Shivajinagar";
    public string City { get; set; } = "Pune, Maharashtra 411005";
    public string Phone { get; set; } = "+91 98220 44556";
    public string Gstin { get; set; } = "27AABCS1429P1Z6";
    public string Fssai { get; set; } = "11522012000456";
    public int GstRateBps { get; set; } = 500;
    public bool PricesIncludeTax { get; set; } = true;
    public string BillPrefix { get; set; } = "INV";
    public string TerminalName { get; set; } = "Counter 01";
    public string LicensePlan { get; set; } = "Growth";
    public string LicenseUntil { get; set; } = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd");
    public bool DemoMode { get; set; } = true;

    public string GstRateText => $"{GstRateBps / 100m:0.##}%";
    public string TaxModeText => PricesIncludeTax ? "Menu prices include GST" : "GST added at billing";
}
