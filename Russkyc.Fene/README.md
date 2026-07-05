# Fene - Slim WebView2 Desktop Wrapper for .NET 10

A specialized, ultra-lean WebView2 desktop wrapper for .NET 10. Built directly on top of native Win32 bindings via CsWin32, Fene provides a raw Windows container to build lightweight web apps without the overhead of heavy UI frameworks.

### Key Features

* **Zero Layout Overhead:** Skips heavy UI frameworks for a minimal memory footprint.
* **Fluent Window Builder:** Configure borders, dark mode, sizes, and locations easily.
* **Blazor & ASP.NET Core Ready:** Seamlessly host local Blazor Server or web hosts as desktop applications.
* **Native Platform Utilities:** Zero-dependency access to native Win32 file/folder dialogs and system font discovery.
* **JavaScript Interop:** Secure bidirectional JSON and string messaging pipelines.

## Quick Start (Pure C# Setup)

```csharp
using System.Drawing;
using Russkyc.Fene;

var prefersDark = Platform.IsSystemInDarkMode();
var windowManager = WindowManager.Shared;

var window = WindowBuilder
    .Create("App Window 1", 1000, 700)
    .WithStartPosition(WindowStartPosition.CenterScreen)
    .UseDarkMode(prefersDark)
    .WithBackgroundColor(prefersDark ? ColorTranslator.FromHtml("#202020") : Color.White)
    .MapVirtualHost("app.local", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"))
    .Build();

windowManager.RunDesktop(window, "http://app.local/index.html");

```

## Blazor Integration Example

```csharp
using Russkyc.Fene;
// ... standard ASP.NET Core builder setup ...

// Add this line to register Fene services for Blazor integration
builder.Services.AddFeneServices();

var app = builder.Build();

// ... map assets and components ...

// Create a Fene window for the Blazor app
var mainWindow = WindowBuilder.Create("Blazor Desktop App")
    .WithSize(800, 600)
    .UseDarkMode()
    .Build();

// Replace the default app.Run() with app.RunDesktop() to launch the Blazor app in a Fene window
app.RunDesktop(mainWindow);

```

Complete documentation and additional examples: [Fene GitHub repository](https://github.com/russkyc/fene)

## License

Licensed under the MIT License.