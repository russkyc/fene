using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.DependencyInjection;

namespace Russkyc.Fene;

public static class DesktopExtensions
{
    public static IServiceCollection AddFeneServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<WindowManager>();
        return serviceCollection;
    }
    
    public static void RunAsDesktopWindow(this WebApplication app, string initialPath, WebViewWindow mainWindow)
    {
        StaticWebAssetsLoader.UseStaticWebAssets(app.Environment, app.Configuration);
        
        var windowManager = app.Services.GetRequiredService<WindowManager>();
        
        app.StartAsync().GetAwaiter().GetResult();

        var serverUrl = app.Urls.First(u => u.StartsWith("http://") || u.StartsWith("https://"));
        windowManager.Initialize(serverUrl);

        mainWindow.Closed += () => 
        {
            Environment.Exit(0);
        };

        windowManager.OpenAsync(initialPath, mainWindow, true);
        Thread.Sleep(Timeout.Infinite);
    }
}