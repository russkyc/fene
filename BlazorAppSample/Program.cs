using System.Drawing;
using BlazorAppSample.Components;
using Russkyc.Fene;

var builder = WebApplication.CreateBuilder(args);

// Register services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFene();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapFeneFontsApi();

var mainWindow = WebViewWindowBuilder
    .Create("Blazor App Sample", 1200, 800)
    .UseDarkMode()
    .WithBackgroundColor(Color.FromArgb(255, 30, 30, 30))
    .WithUserDataFolder(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Data", "MainShell"))
    .ShowOnlyAfterLoad()
    .WithMinSize(1200, 800)
    .Build();

app.RunAsDesktopWindow(initialPath: "/", mainWindow);