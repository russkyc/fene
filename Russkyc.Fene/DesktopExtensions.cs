using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.DependencyInjection;

namespace Russkyc.Fene;

public static class DesktopExtensions
{
    public static IServiceCollection AddFeneServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<WindowManager>(_ => WindowManager.Shared);
        return serviceCollection;
    }
    
    public static void RunDesktop(this WebApplication app, Window mainWindow, string initialPath = "/")
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

        // OpenAsync natively configures and spins up the background STA thread loop safely
        windowManager.OpenAsync(initialPath, mainWindow, true).GetAwaiter().GetResult();
        Thread.Sleep(Timeout.Infinite);
    }
    
}