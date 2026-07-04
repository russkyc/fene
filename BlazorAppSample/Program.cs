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
    .Create("Blazor App Sample")
    .WithStartPosition(WindowStartPosition.CenterScreen)
    .WithSize(800, 600)
    .WithMinSize(800, 600)
    .UseDarkMode()
    .WithBackgroundColor(Color.FromArgb(255, 30, 30, 30))
    .WithUserDataFolder(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Data", "MainShell"))
    .Build();

app.RunDesktop(mainWindow);