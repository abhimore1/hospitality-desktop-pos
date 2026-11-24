using Hospitality.Pos.Desktop.Domain;

namespace Hospitality.Pos.Desktop.Tests;

public class TaxCalculatorTests
{
    [Fact]
    public void Calculate_EmptyLines_ReturnsZeroTotals()
    {
        var totals = TaxCalculator.Calculate([], discountPaise: 0, pricesIncludeTax: true);

        Assert.Equal(0, totals.GrossPaise);
        Assert.Equal(0, totals.DiscountPaise);
        Assert.Equal(0, totals.TaxablePaise);
        Assert.Equal(0, totals.TaxPaise);
        Assert.Equal(0, totals.CgstPaise);
        Assert.Equal(0, totals.SgstPaise);
        Assert.Equal(0, totals.RoundOffPaise);
        Assert.Equal(0, totals.TotalPaise);
    }

    [Fact]
    public void Calculate_TaxInclusiveSingleLine_ExtractsGstAndRoundsToRupee()
    {
        var lines = new[] { new TaxLine(UnitPricePaise: 10500, Quantity: 1, TaxRateBps: 500) }; // ₹105 incl 5%

        var totals = TaxCalculator.Calculate(lines, discountPaise: 0, pricesIncludeTax: true);

        Assert.Equal(10500, totals.GrossPaise);
        Assert.Equal(0, totals.DiscountPaise);
        Assert.Equal(10000, totals.TaxablePaise);
        Assert.Equal(500, totals.TaxPaise);
        Assert.Equal(250, totals.CgstPaise);
        Assert.Equal(250, totals.SgstPaise);
        Assert.Equal(0, totals.RoundOffPaise);
        Assert.Equal(10500, totals.TotalPaise);
    }

    [Fact]
    public void Calculate_TaxExclusiveSingleLine_AddsGst()
    {
        var lines = new[] { new TaxLine(UnitPricePaise: 10000, Quantity: 2, TaxRateBps: 500) }; // ₹100 x2 excl 5%

        var totals = TaxCalculator.Calculate(lines, discountPaise: 0, pricesIncludeTax: false);

        Assert.Equal(20000, totals.GrossPaise);
        Assert.Equal(0, totals.DiscountPaise);
        Assert.Equal(20000, totals.TaxablePaise);
        Assert.Equal(1000, totals.TaxPaise);
        Assert.Equal(500, totals.CgstPaise);
        Assert.Equal(500, totals.SgstPaise);
        Assert.Equal(0, totals.RoundOffPaise);
        Assert.Equal(21000, totals.TotalPaise);
    }

    [Fact]
    public void Calculate_DiscountIsClampedAndAllocatedBeforeTax()
    {
        var lines = new[]
        {
            new TaxLine(UnitPricePaise: 10000, Quantity: 1, TaxRateBps: 500),
            new TaxLine(UnitPricePaise: 5000, Quantity: 1, TaxRateBps: 500)
        }; // gross ₹150

        var totals = TaxCalculator.Calculate(lines, discountPaise: 1500, pricesIncludeTax: false); // ₹15 off

        Assert.Equal(15000, totals.GrossPaise);
        Assert.Equal(1500, totals.DiscountPaise);
        Assert.Equal(13500, totals.TaxablePaise);
        Assert.Equal(675, totals.TaxPaise);
        Assert.Equal(338, totals.CgstPaise);
        Assert.Equal(337, totals.SgstPaise);
        Assert.Equal(14200, totals.TaxablePaise + totals.TaxPaise + totals.RoundOffPaise);
        Assert.Equal(14200, totals.TotalPaise); // rounded to nearest rupee
        Assert.Equal(25, totals.RoundOffPaise);
    }

    [Fact]
    public void RoundToRupee_RoundsHalfAwayFromZero()
    {
        Assert.Equal(200, TaxCalculator.RoundToRupee(150));
        Assert.Equal(200, TaxCalculator.RoundToRupee(151));
        Assert.Equal(0, TaxCalculator.RoundToRupee(49));
        Assert.Equal(100, TaxCalculator.RoundToRupee(149));
    }

    [Fact]
    public void PercentDiscount_ComputesFromGross()
    {
        Assert.Equal(1500, TaxCalculator.PercentDiscount(10000, 15));
        Assert.Equal(0, TaxCalculator.PercentDiscount(10000, 0));
    }
}
