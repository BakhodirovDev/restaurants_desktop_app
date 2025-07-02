using System.Configuration;
using System.Data;
using System.Windows;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Restaurants.Services;
using Restaurants.Pages;

namespace Restaurants;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ServiceProvider serviceProvider;

    public App()
    {
        ServiceCollection services = new ServiceCollection();
        ConfigureServices(services);
        serviceProvider = services.BuildServiceProvider();
    }

    // Service provider accessor
    public ServiceProvider Services => serviceProvider;

    private void ConfigureServices(ServiceCollection services)
    {
        // Register HttpClient
        services.AddSingleton<HttpClient>();
        
        // Register services
        services.AddSingleton<IProductCategoryService, ProductCategoryService>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Create main window
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}

