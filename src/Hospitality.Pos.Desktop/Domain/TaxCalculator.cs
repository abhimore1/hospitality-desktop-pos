namespace Hospitality.Pos.Desktop.Domain;

public readonly record struct TaxLine(long UnitPricePaise, int Quantity, int TaxRateBps);

public sealed record OrderTotals(
    long GrossPaise,
    long DiscountPaise,
    long TaxablePaise,
    long TaxPaise,
    long CgstPaise,
    long SgstPaise,
    long RoundOffPaise,
    long TotalPaise);

/// <summary>
/// GST arithmetic used by billing and by demo-data seeding.
/// Prices may be tax-inclusive (common on Indian menus) or tax-exclusive.
/// The discount is allocated across lines before tax is extracted.
/// </summary>
public static class TaxCalculator
{
    public static OrderTotals Calculate(
        IReadOnlyList<TaxLine> lines,
        long discountPaise,
        bool pricesIncludeTax)
    {
        var gross = lines.Sum(line => (long)line.UnitPricePaise * line.Quantity);
        if (gross <= 0)
        {
            return new OrderTotals(0, 0, 0, 0, 0, 0, 0, 0);
        }

        discountPaise = Math.Clamp(discountPaise, 0, gross);

        decimal taxable = 0;
        decimal tax = 0;
        long allocated = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lineGross = (long)line.UnitPricePaise * line.Quantity;

            long lineDiscount;
            if (i == lines.Count - 1)
            {
                lineDiscount = discountPaise - allocated;
            }
            else
            {
                lineDiscount = (long)Math.Round(
                    discountPaise * (decimal)lineGross / gross,
                    MidpointRounding.AwayFromZero);
                lineDiscount = Math.Min(lineDiscount, lineGross);
            }

            allocated += lineDiscount;
            var net = lineGross - lineDiscount;
            var rate = line.TaxRateBps / 10000m;

            if (pricesIncludeTax)
            {
                var lineTaxable = net / (1m + rate);
                taxable += lineTaxable;
                tax += net - lineTaxable;
            }
            else
            {
                taxable += net;
                tax += net * rate;
            }
        }

        var taxablePaise = (long)Math.Round(taxable, MidpointRounding.AwayFromZero);
        var taxPaise = (long)Math.Round(tax, MidpointRounding.AwayFromZero);
        var cgst = (long)Math.Round(taxPaise / 2m, MidpointRounding.AwayFromZero);
        var sgst = taxPaise - cgst;

        var unrounded = taxablePaise + taxPaise;
        var roundedTotal = RoundToRupee(unrounded);

        return new OrderTotals(
            gross,
            discountPaise,
            taxablePaise,
            taxPaise,
            cgst,
            sgst,
            roundedTotal - unrounded,
            roundedTotal);
    }

    public static long RoundToRupee(long paise) =>
        (long)(Math.Round(paise / 100m, MidpointRounding.AwayFromZero) * 100m);

    /// <summary>Bill discount helper: percent of gross, in paise.</summary>
    public static long PercentDiscount(long grossPaise, int percent) =>
        (long)Math.Round(grossPaise * (percent / 100m), MidpointRounding.AwayFromZero);
}
