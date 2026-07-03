# Fene

### Slim WebView2 Desktop Wrapper for .NET 10

By completely bypassing heavy, deep UI stacks like WPF, WinForms, or MAUI, **Fene** drops application memory overhead, scales down process footprints, achieves sub-millisecond initialization loops, and provides raw control over desktop layout behaviors, process isolation pipelines, and unified web session states.

---

## 📖 Architecture & Design

Fene bridges low-level Win32 threading constraints with modern .NET asynchronous paradigms using three primary abstractions:

```text
  +-------------------------------------------------------------+
  |                   Your App Core / Blazor                    |
  |  Runs business logic, UI state, and calls Fene async APIs   |
  +-------------------------------------------------------------+
                                 |
                                 v  (Thread-Safe Enqueue)
  +-------------------------------------------------------------+
  |              WebViewWindow (ConcurrentQueue)                |
  |  Captures tasks from ANY thread & holds them for processing |
  +-------------------------------------------------------------+
                                 |
                                 v  (Drained synchronously)
  +-------------------------------------------------------------+
  |               Native Win32 Message Loop (STA)               |
  |  Pumps events via PInvoke.GetMessage on dedicated thread    |
  |  Initializes CoreWebView2 and mutates HWND window state     |
  +-------------------------------------------------------------+

```

* **Isolated STA Thread Loops**: Every window spawned runs on its own `ApartmentState.STA` thread, strictly satisfying COM interop requirements.
* **Lock-Free Thread Synchronization**: Fene wraps execution blocks into a lock-free `ConcurrentQueue<Action>` drained synchronously by the native window procedure (`WndProc`). This safely marshals background tasks without heavy framework dispatchers or deadlocks.
* **Process Topologies**: By sharing a single `UserDataFolder` across windows, instances instantly tap into a unified cookie jar, session cache, and LocalStorage pool.

---

## 🚀 Quick Start & Bootstrapping

Fene can be used as a standalone pure C# desktop engine, or seamlessly wrapped around an ASP.NET Core / Blazor web host.

### Option A: Pure C# Console Setup (Minimal, No Blazor)

The absolute lightest way to run Fene. This spins up a native window pointing to an external or local URI without loading any web server frameworks.

```csharp
using System;
using System.Threading.Tasks;
using System.Drawing;
using Russkyc.Fene;

class Program
{
    // STAThread is absolutely required for native COM / WebView2 execution
    [STAThread]
    static async Task Main(string[] args)
    {
        var manager = new WindowManager();
        manager.Initialize("https://www.google.com"); 

        var mainWindow = new WebViewWindow("Minimal Fene App", 1024, 768)
        {
            EnableDarkMode = true,
            BackgroundColor = Color.FromArgb(255, 20, 20, 20)
        };

        // Spawn the window on a dedicated Win32 message loop
        await manager.OpenAsync(path: "/", mainWindow, isMainWindow: true);

        // Keep the main thread alive until the window is destroyed
        while (manager.MainWindow != null)
        {
            await Task.Delay(200);
        }
    }
}

```

### Option B: Blazor & ASP.NET Core Integration

Run your entire Blazor server application locally, hijacking the host process to run as a desktop application.

**1. Application Entry Point (`Program.cs`)**

```csharp
using System.Drawing;
using BlazorAppSample.Components;
using Russkyc.Fene;

var builder = WebApplication.CreateBuilder(args);

// Register baseline Razor Component rendering mechanisms
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Registers the WindowManager singleton ecosystem automatically
builder.Services.AddFeneServices();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Configure our primary desktop application window shell container 
var mainWindow = new WebViewWindow("Blazor App Sample", 1200, 800)
{
    EnableDarkMode = true,
    BackgroundColor = Color.FromArgb(255, 30, 30, 30),
    UserDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Data", "MainShell"),
    ShowOnlyAfterLoad = true
};

// Hand control off directly to Fene's extension runtime pipeline loop
app.RunAsDesktopWindow(initialPath: "/", mainWindow);

```

**2. ASP.NET Core Hosting Extensions (`FeneApplicationExtensions.cs`)**
*Required for Option B to wire Kestrel into the Win32 message loop.*

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Russkyc.Fene;

public static class FeneApplicationExtensions
{
    public static IServiceCollection AddFeneServices(this IServiceCollection services)
    {
        services.AddSingleton<WindowManager>();
        return services;
    }

    [STAThread]
    public static void RunAsDesktopWindow(this WebApplication app, string initialPath, WebViewWindow mainWindow)
    {
        var manager = app.Services.GetRequiredService<WindowManager>();

        Task.Run(async () => await app.RunAsync());

        var server = app.Services.GetRequiredService<IServer>();
        IServerAddressesFeature? addressesFeature = null;

        while (addressesFeature == null || !addressesFeature.Addresses.Any())
        {
            Task.Delay(50).GetAwaiter().GetResult();
            addressesFeature = server.Features.Get<IServerAddressesFeature>();
        }

        string localServerUrl = addressesFeature.Addresses.First();
        manager.Initialize(localServerUrl);

        mainWindow.Closed += () =>
        {
            manager.CloseAllWindows();
            Task.Run(async () => await app.StopAsync()).GetAwaiter().GetResult();
        };

        string processedPath = initialPath.StartsWith("http://") || initialPath.StartsWith("https://")
            ? initialPath
            : $"{localServerUrl.TrimEnd('/')}/{initialPath.TrimStart('/')}";

        mainWindow.ShowAndRun(processedPath);
    }
}

```

---

## 🛠️ Usage Guide

### Window Chrome, Framing & Shell Layouts

Customize window borders, dark mode optimization, custom icons, and precise monitor-snapping placement.

```csharp
var productionShell = new WebViewWindow("Enterprise Shell Container", 1024, 768)
{
    IsBorderless = false,
    EnableDarkMode = true,
    BackgroundColor = Color.FromArgb(255, 18, 18, 18),
    IconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app_logo.ico"),
    X = 150,
    Y = 100,
    ShowOnlyAfterLoad = true
};

```

### Runtime Window State & Screen Location

Query and mutate layout coordinates, focus profiles, and maximize/minimize conditions live while the message loop is running.

```csharp
// Move the window container dynamically to target screen layout pixels
productionShell.SetLocation(x: 500, y: 300);

// Stay anchored above all other non-topmost desktop applications
productionShell.IsTopMost = true;

// Force the handle to the top of the desktop layer and assign active system focus
productionShell.BringToFront();

// Alter window display mode
productionShell.WindowState = WindowState.Maximized;

// Dispatch a WM_CLOSE control message directly
productionShell.Close();

```

### Chromium Runtime Settings & Browser Capability Flags

The `WebViewSettingsOptions` object maps directly over to underlying browser engine flags. These must be assigned **before** dispatching a window open command.

```csharp
var customOptionsWindow = new WebViewWindow("Secured Terminal Base", 800, 600);

customOptionsWindow.Options.EnableGpuAcceleration = true;
customOptionsWindow.Options.DisableWebSecurity = true; // Bypasses CORS constraints for development
customOptionsWindow.Options.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";
customOptionsWindow.Options.AreDevToolsEnabled = true;
customOptionsWindow.Options.IsScriptEnabled = true;
customOptionsWindow.Options.IsWebMessageEnabled = true;
customOptionsWindow.Options.IsPasswordAutosaveEnabled = false;
customOptionsWindow.Options.IsGeneralAutofillEnabled = false;

```

### Virtual Host to Local Folder Mapping

Map a virtual host name directly to a physical directory block. This resolves web resources using clean, absolute URL paths without dealing with local `file://` protocol restrictions.

```csharp
productionShell.MapVirtualHost(
    hostName: "app.internal.local",
    folderPath: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"),
    accessKind: Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow
);

await windowManager.OpenAsync("https://app.internal.local/index.html", productionShell);

```

### JavaScript Interop & Web Messaging Pipeline

Execute evaluation string blocks securely or pass string variables natively across executing threads.

```csharp
// Intercept messages from JavaScript
productionShell.WebMessageReceived += (string stringifiedMessageJson) =>
{
    Console.WriteLine($"Inbound message payload captured: {stringifiedMessageJson}");
};

// Dispatch messages to browser
productionShell.PostWebMessageAsString("TriggerAppUpdateEvent");
productionShell.PostWebMessageAsJson("{\"action\": \"rehydrate\"}");

// Evaluate script snippets and return values to C#
string elementValueJson = await productionShell.ExecuteScriptAsync(
    "document.getElementById('session-state-token').value;"
);

```

### Monitor Topology Detection

Query connected screen configurations dynamically to arrange layouts, handle multi-monitor shifts, or position child dialogs exactly where they need to be.

```csharp
productionShell.DisplaysChanged += () =>
{
    IReadOnlyList<Display> connectedScreens = productionShell.Displays;
    
    foreach (var screen in connectedScreens)
    {
        if (screen.IsPrimary)
        {
            // Center exactly on the primary monitor's working area
            int targetX = screen.WorkingArea.X + (screen.WorkingArea.Width - 1024) / 2;
            int targetY = screen.WorkingArea.Y + (screen.WorkingArea.Height - 768) / 2;
            productionShell.SetLocation(targetX, targetY);
        }
    }
};

```

### Native Platform Utilities (`Platform` Class)

Since Fene skips heavyweight UI frameworks, the `Platform` utility class leverages native COM interop to provide fast, zero-dependency access to native Windows shell dialogs and system information.

```csharp
// 1. Single File Picker
string? selectedImage = Platform.ShowOpenFileDialog(
    title: "Select Profile Picture", 
    filter: "Image Files|*.png;*.jpg|All Files|*.*"
);

// 2. Multi-File Picker
string[] documents = Platform.ShowOpenMultipleFilesDialog(
    title: "Upload Documents",
    filter: "PDF Files|*.pdf"
);

// 3. Save File Picker
string? saveDestination = Platform.ShowSaveFileDialog(
    title: "Save Report",
    filter: "CSV Files|*.csv",
    defaultExtension: ".csv"
);

// 4. Folder Selection Dialog
string? exportPath = Platform.ShowFolderBrowserDialog("Choose Export Destination");

// 5. Query System Fonts (Zero-Dependency via Registry)
IEnumerable<string> installedFonts = Platform.GetInstalledFonts();
foreach (var font in installedFonts)
{
    Console.WriteLine($"Available font: {font}");
}

```

### State Cleansing & Engine Events

Track navigation steps and purge saved state records programmatically.

```csharp
productionShell.NavigationStarted += (string destinationUri) => Console.WriteLine($"Routing to: {destinationUri}");
productionShell.NavigationCompleted += (string currentActiveSource) => Console.WriteLine($"Ready: {currentActiveSource}");
productionShell.Closed += () => Console.WriteLine("Native Win32 handle destroyed.");

// Programmatically displays Edge Developer Tools
productionShell.OpenDevTools();

// Purges browsing metrics, local caches, and auth records safely from storage
await productionShell.ClearBrowsingDataAsync();

```

---

## 🔐 Advanced Session Management

### Multi-Window Shared Profiles (OAuth Flow)

Demonstrates how to spawn a modal login dialog sharing the parent app's runtime cache profile data.

```csharp
public async Task ExecutionLoginSequenceAsync()
{
    WebViewWindow primaryFrame = _windowManager.MainWindow;

    WebViewWindow loginDialog = new WebViewWindow("Identity Access Verification", 500, 650)
    {
        IsTopMost = true,
        UserDataFolder = primaryFrame.UserDataFolder // Shared profile context
    };

    // Blocks cleanly until the target window triggers its Win32 close events
    await _windowManager.ShowDialogAsync("https://accounts.google.com/signin", loginDialog, external: true);

    // Primary window instantly pulls tokens stored by the dialog window
    var postAuthCookies = await primaryFrame.GetCookiesAsync("https://google.com");
    Console.WriteLine($"Fetched structural tokens: {postAuthCookies.Count}");
}

```

### Cloned Session Automation (HttpClient Injection)

Extract authentication sessions cleanly out of raw desktop interfaces, cloning user identities back over to high-speed backend execution tasks.

```csharp
public async Task ExtractAndFetchSecurePayloadAsync()
{
    WebViewWindow sessionSourceWindow = _windowManager.MainWindow;

    CookieContainer synchronizedJar = await sessionSourceWindow.GetCookieContainerAsync("https://myaccount.google.com");

    using HttpClientHandler networkingHandler = new HttpClientHandler
    {
        CookieContainer = synchronizedJar,
        UseCookies = true,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    using HttpClient backendAgentClient = new HttpClient(networkingHandler);
    backendAgentClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");

    HttpResponseMessage rawResultPayload = await backendAgentClient.GetAsync("https://myaccount.google.com/profile/name");

    if (rawResultPayload.IsSuccessStatusCode)
    {
        string parsedHtmlDocumentContent = await rawResultPayload.Content.ReadAsStringAsync();
        Console.WriteLine($"Data payload retrieved. Bytes: {parsedHtmlDocumentContent.Length}");
    }
}

```

---

## 🗂️ Complete API Specification

### `WindowManager`

| Method | Description |
| --- | --- |
| `Initialize(string baseUrl)` | Sets the global root URL for relative route evaluation. |
| `OpenAsync(path, window, isMainWindow)` | Spawns an async window. Task completes upon navigation finish. |
| `ShowDialogAsync(path, window, external)` | Spawns a window and blocks until closure. |
| `GetWindow(Guid id)` | Looks up a tracked window by registration ID. |
| `CloseWindow(Guid id)` | Triggers destruction of a specific tracked window. |
| `CloseAllWindows()` | Closes all tracked secondary instances. |

### `WebViewWindow`

| Property | Type | Description |
| --- | --- | --- |
| `BackgroundColor` | Color | Sets the clear-color paint target. |
| `EnableDarkMode` | bool | Enforces immersive dark title bars via DWM. |
| `IconPath` | string | Path to a `.ico` taskbar resource. |
| `UserDataFolder` | string | Profile location for cache, cookies, and database state. |
| `IsBorderless` | bool | Strips standard OS window chrome. |
| `UserAgentOverride` | string | Custom client browser identification header. |
| `X`, `Y` | int? | Specific screen positioning coordinates. |
| `WindowState` | WindowState | Normal, Minimized, or Maximized parameters. |
| `IsTopMost` | bool | Hardware-pinned over other desktop frames. |
| `Displays` | IReadOnlyList | Retrieves connected monitor workspace geometries. |
| `Options` | WebViewSettingsOptions | Internal Chromium execution flags and configuration. |

| Method | Description |
| --- | --- |
| `Navigate(string url)` | Explicit target navigation. |
| `ExecuteScriptAsync(string script)` | Evaluates JavaScript within the document context. |
| `PostWebMessageAsString(string msg)` | Dispatches string payload to active web scripts. |
| `PostWebMessageAsJson(string json)` | Dispatches JSON payload to active web scripts. |
| `OpenDevTools()` | Invokes the embedded Chromium DevTools panel. |
| `SetLocation(int x, int y)` | Programmatically moves the Win32 window. |
| `BringToFront()` | Forces window to the foreground and assigns focus. |
| `ClearBrowsingDataAsync()` | Wipes runtime state, web tokens, and cache layouts. |
| `GetCookiesAsync(string uri)` | Returns an active system-net compatible cookie list. |
| `GetCookieContainerAsync(string uri)` | Builds an active `CookieContainer` for network requests. |

### `Platform`

| Method | Description |
| --- | --- |
| `ShowOpenFileDialog(title, filter, owner)` | Opens native Win32 `IFileOpenDialog` for a single file. Returns selected path or null. |
| `ShowOpenMultipleFilesDialog(title, filter, owner)` | Opens native Win32 `IFileOpenDialog` configured for multiple files. Returns array of paths. |
| `ShowSaveFileDialog(title, filter, defaultExt, owner)` | Opens native Win32 `IFileSaveDialog` for exporting data. Returns destination path or null. |
| `ShowFolderBrowserDialog(title, owner)` | Opens native Win32 `IFileOpenDialog` constrained to directories only. Returns folder path or null. |
| `GetInstalledFonts()` | Retrieves system-installed fonts directly from the registry without heavyweight drawing dependencies. |

---

## 📜 License

Copyright © 2026 JOHN RUSSELL CAMO.