using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Services;

namespace Hospitality.Pos.Desktop.Views;

public partial class BillWindow : Window
{
    private static readonly Brush Ink = Frozen("#0F172A");
    private static readonly Brush Muted = Frozen("#64748B");
    private static readonly Brush Light = Frozen("#94A3B8");

    public BillWindow()
    {
        InitializeComponent();
    }

    public void LoadDocument(BillDocument document)
    {
        DataContext = document;
        Title = $"Bill {document.InvoiceNo}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BillDocument bill)
        {
            return;
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var document = BuildDocument(bill);
            document.PageWidth = dialog.PrintableAreaWidth;
            document.PageHeight = dialog.PrintableAreaHeight;
            dialog.PrintDocument(
                ((IDocumentPaginatorSource)document).DocumentPaginator,
                $"Restrunt bill {bill.InvoiceNo}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The bill could not be printed.\n\n{ex.Message}",
                "Print bill", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static FlowDocument BuildDocument(BillDocument bill)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = Ink,
            PagePadding = new Thickness(40),
            ColumnWidth = double.PositiveInfinity
        };

        if (bill.IsDuplicate)
        {
            document.Blocks.Add(Line(bill.DuplicateNotice, 11, FontWeights.Bold, TextAlignment.Center, Frozen("#DC2626"), 0, 0, 6, 0));
        }

        document.Blocks.Add(Line(bill.OutletName, 20, FontWeights.SemiBold, TextAlignment.Center, Ink, 0, 0, 2, 0));
        document.Blocks.Add(Line($"{bill.Address}, {bill.City}", 10, FontWeights.Normal, TextAlignment.Center, Muted, 0, 0, 1, 0));
        document.Blocks.Add(Line($"Phone {bill.Phone}", 10, FontWeights.Normal, TextAlignment.Center, Muted, 0, 0, 1, 0));
        document.Blocks.Add(Line($"GSTIN {bill.Gstin}    FSSAI {bill.Fssai}", 9, FontWeights.Normal, TextAlignment.Center, Muted, 0, 0, 12, 0));
        document.Blocks.Add(Line("TAX INVOICE", 11, FontWeights.Bold, TextAlignment.Center, Ink, 0, 0, 10, 0));
        document.Blocks.Add(Line($"Invoice {bill.InvoiceNo}    ·    {bill.PaidAtText}", 10, FontWeights.Normal, TextAlignment.Left, Ink, 0, 0, 2, 0));
        document.Blocks.Add(Line($"{bill.OrderType}    ·    Table {bill.TableText}    ·    Paid via {bill.PaymentText}    ·    Billed by {bill.Cashier}",
            9.5, FontWeights.Normal, TextAlignment.Left, Muted, 0, 0, 8, 0));

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 4) };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(40) });
        table.Columns.Add(new TableColumn { Width = new GridLength(90) });

        var rows = new TableRowGroup();
        table.RowGroups.Add(rows);
        rows.Rows.Add(ItemRow("Item", "Qty", "Amount", true));

        foreach (var line in bill.Lines)
        {
            rows.Rows.Add(ItemRow(line.Name, line.Qty.ToString(), Inr.Format(line.LineTotalPaise)));
        }

        document.Blocks.Add(table);
        document.Blocks.Add(Line("──────────────────────────────────────────────", 8, FontWeights.Normal, TextAlignment.Center, Light, 2, 0, 6, 0));

        document.Blocks.Add(TotalLine("Taxable value", Inr.Format(bill.TaxablePaise), false));
        if (bill.HasDiscount)
        {
            document.Blocks.Add(TotalLine("Discount", $"- {Inr.Format(bill.DiscountPaise)}", false));
        }

        document.Blocks.Add(TotalLine(bill.CgstLabel, Inr.Format(bill.CgstPaise), false));
        document.Blocks.Add(TotalLine(bill.SgstLabel, Inr.Format(bill.SgstPaise), false));
        document.Blocks.Add(TotalLine("Round off", Inr.Format(bill.RoundOffPaise), false));
        document.Blocks.Add(TotalLine("TOTAL PAID", Inr.Format(bill.TotalPaise), true));

        document.Blocks.Add(Line($"All prices include GST at {bill.GstRateText}.", 8.5, FontWeights.Normal, TextAlignment.Center, Light, 12, 0, 2, 0));
        document.Blocks.Add(Line("This is a computer-generated invoice. Thank you — visit again!", 9.5, FontWeights.Normal, TextAlignment.Center, Muted, 0, 0, 2, 0));
        document.Blocks.Add(Line("Powered by Restrunt POS", 8.5, FontWeights.SemiBold, TextAlignment.Center, Light, 6, 0, 0, 0));

        return document;
    }

    private static Paragraph Line(
        string text,
        double size,
        FontWeight weight,
        TextAlignment alignment,
        Brush brush,
        double top,
        double right,
        double bottom,
        double left) =>
        new(new Run(text))
        {
            FontSize = size,
            FontWeight = weight,
            TextAlignment = alignment,
            Foreground = brush,
            Margin = new Thickness(left, top, right, bottom)
        };

    private static TableRow ItemRow(string name, string quantity, string amount, bool header = false)
    {
        var row = new TableRow();
        row.Cells.Add(Cell(name, TextAlignment.Left, header));
        row.Cells.Add(Cell(quantity, TextAlignment.Center, header));
        row.Cells.Add(Cell(amount, TextAlignment.Right, header));
        return row;
    }

    private static TableCell Cell(string text, TextAlignment alignment, bool header) =>
        new(new Paragraph(new Run(text))
        {
            FontSize = header ? 9 : 10.5,
            FontWeight = header ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = alignment,
            Foreground = header ? Muted : Ink,
            Margin = new Thickness(0, 3, 0, 3)
        });

    private static Paragraph TotalLine(string label, string amount, bool strong) =>
        new(new Run($"{label}    {amount}"))
        {
            FontSize = strong ? 13 : 10,
            FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = TextAlignment.Right,
            Foreground = strong ? Ink : Muted,
            Margin = new Thickness(0, strong ? 8 : 1, 0, 0)
        };

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
