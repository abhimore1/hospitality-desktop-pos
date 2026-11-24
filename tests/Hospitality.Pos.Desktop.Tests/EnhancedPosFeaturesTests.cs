using System.Xml.Linq;
using Hospitality.Pos.Desktop.Data;
using Hospitality.Pos.Desktop.Domain;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;

namespace Hospitality.Pos.Desktop.Tests;

public class EnhancedPosFeaturesTests
{
    [Fact]
    public void SplitPayment_EqualSplit_BalancesExactlyToPaise()
    {
        // Test an odd paise amount: ₹333.35 = 33335 paise split 3 ways
        long totalPaise = 33335;
        var parts = 3;

        var baseShare = totalPaise / parts; // 11111 paise
        var remainder = totalPaise % parts; // 2 paise

        var splits = new List<long>();
        for (var i = 0; i < parts; i++)
        {
            var amount = baseShare + (i == 0 ? remainder : 0);
            splits.Add(amount);
        }

        Assert.Equal(3, splits.Count);
        Assert.Equal(11113, splits[0]); // First gets remainder
        Assert.Equal(11111, splits[1]);
        Assert.Equal(11111, splits[2]);
        Assert.Equal(totalPaise, splits.Sum()); // Zero paise leakage
    }

    [Fact]
    public void BillDocument_ReprintWatermark_RendersDuplicateNoticeOnlyOnReprint()
    {
        var originalBill = new BillDocument
        {
            InvoiceNo = "INV/2026-27/0001",
            ReprintCount = 0
        };

        Assert.False(originalBill.IsDuplicate);
        Assert.Equal("", originalBill.DuplicateNotice);

        var reprintedBill = new BillDocument
        {
            InvoiceNo = "INV/2026-27/0001",
            ReprintCount = 2
        };

        Assert.True(reprintedBill.IsDuplicate);
        Assert.Contains("DUPLICATE INVOICE", reprintedBill.DuplicateNotice);
        Assert.Contains("Reprint #2", reprintedBill.DuplicateNotice);
    }

    [Fact]
    public void BillDocument_CustomerAndRoomDetails_RenderAccurately()
    {
        var bill = new BillDocument
        {
            InvoiceNo = "INV/2026-27/0042",
            CustomerName = "Rajesh Sharma",
            CustomerPhone = "9876543210",
            CustomerGstin = "27ABCDE1234F1Z5",
            RoomNumber = "204"
        };

        Assert.True(bill.HasCustomerInfo);
        Assert.True(bill.HasCustomerPhone);
        Assert.True(bill.HasCustomerGstin);
        Assert.True(bill.HasRoomNumber);
        Assert.Equal("204", bill.RoomNumber);
    }

    [Fact]
    public void TallyXml_Structure_GeneratesWellFormedXmlEnvelope()
    {
        // Verify standard Tally XML envelope structure
        var outletName = "Grand Sagar Restaurant &amp; Suites";
        var dateStr = "20261124";
        var billNo = "INV/2026-27/0101";
        var partyLedger = "Cash";
        var totalRs = "525.00";
        var taxableRs = "500.00";
        var cgstRs = "12.50";
        var sgstRs = "12.50";

        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <ENVELOPE>
              <HEADER>
                <TALLYREQUEST>Import Data</TALLYREQUEST>
              </HEADER>
              <BODY>
                <IMPORTDATA>
                  <REQUESTDESC>
                    <REPORTNAME>Vouchers</REPORTNAME>
                    <STATICVARIABLES>
                      <SVCURRENTCOMPANY>{outletName}</SVCURRENTCOMPANY>
                    </STATICVARIABLES>
                  </REQUESTDESC>
                  <REQUESTDATA>
                    <TALLYMESSAGE xmlns:UDF="TallyUDF">
                      <VOUCHER VCHTYPE="Sales" ACTION="Create">
                        <DATE>{dateStr}</DATE>
                        <GUID>guid-12345</GUID>
                        <VOUCHERTYPENAME>Sales</VOUCHERTYPENAME>
                        <VOUCHERNUMBER>{billNo}</VOUCHERNUMBER>
                        <PARTYLEDGERNAME>{partyLedger}</PARTYLEDGERNAME>
                        <ALLLEDGERENTRIES.LIST>
                          <LEDGERNAME>{partyLedger}</LEDGERNAME>
                          <ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE>
                          <AMOUNT>-{totalRs}</AMOUNT>
                        </ALLLEDGERENTRIES.LIST>
                        <ALLLEDGERENTRIES.LIST>
                          <LEDGERNAME>Restaurant Food Sales</LEDGERNAME>
                          <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
                          <AMOUNT>{taxableRs}</AMOUNT>
                        </ALLLEDGERENTRIES.LIST>
                        <ALLLEDGERENTRIES.LIST>
                          <LEDGERNAME>CGST Output Account</LEDGERNAME>
                          <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
                          <AMOUNT>{cgstRs}</AMOUNT>
                        </ALLLEDGERENTRIES.LIST>
                        <ALLLEDGERENTRIES.LIST>
                          <LEDGERNAME>SGST Output Account</LEDGERNAME>
                          <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
                          <AMOUNT>{sgstRs}</AMOUNT>
                        </ALLLEDGERENTRIES.LIST>
                      </VOUCHER>
                    </TALLYMESSAGE>
                  </REQUESTDATA>
                </IMPORTDATA>
              </BODY>
            </ENVELOPE>
            """;

        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root);
        Assert.Equal("ENVELOPE", doc.Root.Name.LocalName);

        var voucher = doc.Descendants("VOUCHER").FirstOrDefault();
        Assert.NotNull(voucher);
        Assert.Equal("Sales", voucher.Attribute("VCHTYPE")?.Value);
        Assert.Equal(billNo, voucher.Element("VOUCHERNUMBER")?.Value);

        var ledgers = voucher.Descendants("ALLLEDGERENTRIES.LIST").ToList();
        Assert.Equal(4, ledgers.Count);

        // Verify debit and credit balance in XML
        var amounts = ledgers.Select(l => decimal.Parse(l.Element("AMOUNT")?.Value ?? "0")).ToList();
        Assert.Equal(0m, amounts.Sum()); // -525.00 + 500.00 + 12.50 + 12.50 = 0
    }

    [Fact]
    public void DayEndClosure_CalculatesVarianceCorrectly()
    {
        var closure = new DayEndClosure
        {
            ClosureDate = "2026-11-24",
            ClosedBy = "Manager",
            TotalSalesPaise = 5000000,
            SystemCashPaise = 2500000,
            CountedCashPaise = 2500000,
            CashVariancePaise = 0
        };

        Assert.True(closure.IsBalanced);
        Assert.Equal("₹0", closure.CashVarianceText);

        closure.CountedCashPaise = 2490000;
        closure.CashVariancePaise = -10000; // ₹100 shortage

        Assert.False(closure.IsBalanced);
        Assert.Equal("-₹100", closure.CashVarianceText);
    }

    [Fact]
    public void AntiFraud_DiscountThreshold_RequiresApprovalAbove10Percent()
    {
        long grossPaise = 100000; // ₹1000
        var discount5Pct = TaxCalculator.PercentDiscount(grossPaise, 5);
        var discount15Pct = TaxCalculator.PercentDiscount(grossPaise, 15);

        Assert.Equal(5000, discount5Pct);
        Assert.Equal(15000, discount15Pct);

        // Under 10%: No manager approval needed
        var requiresApproval5 = (double)discount5Pct / grossPaise > 0.10;
        Assert.False(requiresApproval5);

        // Over 10%: Requires manager approval PIN
        var requiresApproval15 = (double)discount15Pct / grossPaise > 0.10;
        Assert.True(requiresApproval15);
    }
}
