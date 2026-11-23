using System.Windows;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Services;
using Hospitality.Pos.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Hospitality.Pos.Desktop.Services;

/// <summary>Opens the printable bill preview after a payment is taken.</summary>
public sealed class BillPresenter : IBillPresenter
{
    private readonly IServiceProvider _services;

    public BillPresenter(IServiceProvider services) => _services = services;

    public void Show(BillDocument document)
    {
        var window = _services.GetRequiredService<BillWindow>();
        window.Owner = Application.Current.MainWindow;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.LoadDocument(document);
        window.ShowDialog();
    }
}
