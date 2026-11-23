using System.IO;
using System.Windows;
using System.Windows.Threading;
using Hospitality.Pos.Desktop.Data;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;
using Hospitality.Pos.Desktop.Services.Sync;
using Hospitality.Pos.Desktop.ViewModels;
using Hospitality.Pos.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hospitality.Pos.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureServices(ConfigureServices)
            .Build();

        _host.Start();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _host.Services.GetRequiredService<PosDatabase>().Initialize();
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show(
                $"The local database could not be opened.\n\n{ex.Message}\n\nPath: {_host.Services.GetRequiredService<PosDatabase>().DatabasePath}",
                "Restrunt POS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var login = _host.Services.GetRequiredService<LoginWindow>();
        if (login.ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        var main = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception, "DispatcherUnhandledException");
        MessageBox.Show(
            $"Restrunt POS hit an unexpected problem.\n\n{e.Exception.Message}\n\nDetails were written to the local error log.",
            "Restrunt POS",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogError(ex, $"AppDomainUnhandledException (IsTerminating={e.IsTerminating})");
        }
        else
        {
            LogError(new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception"),
                $"AppDomainUnhandledException (IsTerminating={e.IsTerminating})");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }

    private static void LogError(Exception exception, string source = "Unhandled")
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RestruntPos");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "error.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never mask the original failure.
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Local data
        services.AddSingleton<PosDatabase>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<StaffRepository>();
        services.AddSingleton<MenuRepository>();
        services.AddSingleton<TableRepository>();
        services.AddSingleton<OrderRepository>();
        services.AddSingleton<ReportRepository>();
        services.AddSingleton<OutboxRepository>();
        services.AddSingleton<AuditRepository>();

        // App services
        services.AddSingleton<AppSession>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<DataChangeNotifier>();
        services.AddSingleton<AppNavigation>();
        services.AddSingleton<IBillPresenter, BillPresenter>();
        services.AddSingleton<ISyncTransport, NoopSyncTransport>();
        services.AddSingleton<ISyncCoordinator, SyncCoordinator>();

        // View models
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<BillingViewModel>();
        services.AddSingleton<TablesViewModel>();
        services.AddSingleton<KitchenViewModel>();
        services.AddSingleton<MenuViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<LoginViewModel>();

        // Windows
        services.AddTransient<LoginWindow>();
        services.AddTransient<BillWindow>();
        services.AddSingleton<MainWindow>();
    }
}
