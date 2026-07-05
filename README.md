# Fene - A Slim WebView2 Desktop Wrapper for .NET 10

Fene serves as a specialized, ultra-lean alternative for scenarios where you only need a raw Windows container. Initially explored to be used to build lightweight Windows desktop web applications without standard framework overhead. By building directly on top of native Win32 bindings via CsWin32, Fene minimizes layout overhead and footprint.

> [!IMPORTANT]
> While the api covers a lot of common scenarios, it might not cover all of the use cases you might have. If you need a more robust solution, consider using a more established framework.

## Quick Start & Bootstrapping

Fene can be used as a standalone pure C# desktop engine via a fluent builder pattern, or seamlessly wrapped around an
ASP.NET Core / Blazor web host.

### Option A: Pure C# Console Setup (Minimal, No Blazor)

A simple way to run Fene. This utilizes the intuitive `WindowBuilder` fluent API to stand up a native window pointing to
an external or local URI with zero layout overhead.

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

### Option B: Blazor & ASP.NET Core Integration

Run your entire Blazor server application locally, making the blazor host process run as a desktop application.

```csharp
using System.Drawing;
using BlazorAppSample.Components;
using Russkyc.Fene;

var builder = WebApplication.CreateBuilder(args);

// Register services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Registers WindowManager.Shared context using native Fene extensions
builder.Services.AddFeneServices();

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

var mainWindow = WindowBuilder
    .Create("Blazor App Sample")
    .WithStartPosition(WindowStartPosition.CenterScreen)
    .WithSize(800, 600)
    .WithMinSize(800, 600)
    .UseDarkMode()
    .WithBackgroundColor(Color.FromArgb(255, 30, 30, 30))
    .WithUserDataFolder(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Data", "MainShell"))
    .Build();

app.RunDesktop(mainWindow);

```



## Usage Guide

### Fluid Window Construction (`WindowBuilder`)

Fene provides a static factory entry method `WindowBuilder.Create()` which exposes standard chaining verbs. Optional
defaults are native; omitting sizes or placement instructs the Windows OS to map dimensions automatically using native
cascading defaults (`CW_USEDEFAULT`).

```csharp
// Scenario A: Expressive custom frame configuration
var productionShell = WindowBuilder.Create("Enterprise Shell Container")
    .WithSize(1024, 768)
    .WithMinSize(800, 600)
    .WithStartPosition(WindowStartPosition.CenterScreen)
    .UseDarkMode()
    .WithBackgroundColor(Color.FromArgb(255, 18, 18, 18))
    .WithIcon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app_logo.ico"))
    .ShowOnlyAfterLoad()
    .Build();

// Scenario B: Specialized Kiosk targeting sequence
var kioskShell = WindowBuilder.Create("Terminal UI")
    .BuildKiosk(); // Automatically locks window boundaries, forces borderless, and maximizes frame layout

```

### Runtime Window State Modifications

Query and mutate layout coordinates, focus profiles, and maximize/minimize conditions live while the message loop is
running.

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

### Chromium Settings & Browser Isolation

Configure execution settings within the `.ConfigureSettings(...)` lambda. These properties write directly onto the final
container instantiation loop during `Build()`.

```csharp
var customOptionsWindow = WindowBuilder.Create("Secured Terminal Base")
    .ConfigureSettings(options => 
    {
        options.EnableGpuAcceleration = true;
        options.DisableWebSecurity = true; // Bypasses CORS constraints for development
        options.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";
        options.AreDevToolsEnabled = true;
        options.IsScriptEnabled = true;
        options.IsWebMessageEnabled = true;
        options.IsZoomControlEnabled = true;
        options.AreDefaultScriptDialogsEnabled = true;
        options.IsBuiltInErrorPageEnabled = true;
        options.IsPasswordAutosaveEnabled = false;
        options.IsGeneralAutofillEnabled = false;
        options.IsGestureAutoplayBlocked = true;
        options.AreHostObjectsAllowed = true;
        options.IsPinchZoomEnabled = true;
        options.PreventDragAndDropNavigation = true;   // Keeps user drop mutations from altering source URIs
    })
    .Build();

```

### Virtual Host to Local Folder Mapping

Map a virtual host name directly to a physical directory block. This resolves web resources using clean, absolute URL
paths without dealing with local `file://` protocol restrictions.

```csharp
var localMappedWindow = WindowBuilder.Create("Local Asset Viewer")
    .MapVirtualHost("app.internal.local", "wwwroot", HostResourceAccessKind.Allow)
    .Build();

await windowManager.OpenAsync("https://app.internal.local/index.html", localMappedWindow);

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

Query connected screen configurations dynamically to arrange layouts, handle multi-monitor shifts, or position child
dialogs exactly where they need to be.

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

### Native Platform Utilities & Font Discovery (`Platform` Class)

Since Fene skips heavyweight UI frameworks, the `Platform` utility class leverages native COM interop to provide fast,
zero-dependency access to native Windows shell dialogs and system information.

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

// 5. Query Real System Fonts (Returns proper space-separated Family names mapped directly to absolute paths)
Dictionary<string, string> installedFonts = Platform.GetInstalledFonts();
if (installedFonts.TryGetValue("Arial Bold", out var absolutePath))
{
    Console.WriteLine($"Arial Bold is registered locally at path: {absolutePath}");
}

```



## Advanced Session & Modal Orchestration

### Multi-Window Shared Profiles & Refocusing Modals

Demonstrates how to spawn a true modal dialog window that blocks parent input interactions.

> **Win32 Modality Assurance:** When a modal created with `WindowStartPosition.CenterOwner` finishes execution, Fene
> naturally triggers re-enabling sequences and passes active foreground focus directly back to the modal's owner handle
*on the UI thread loop prior to handle destruction*, avoiding focus drop behavior.

```csharp
public async Task ExecutionLoginSequenceAsync()
{
    Window primaryFrame = _windowManager.MainWindow;

    // Use our builder to establish our blocking modal properties
    Window loginDialog = WindowBuilder.Create("Identity Access Verification")
        .WithSize(500, 650)
        .WithStartPosition(WindowStartPosition.CenterOwner) // Tracks owner coordinate center alignments
        .MakeTopMost()
        .Build();

    loginDialog.UserDataFolder = primaryFrame.UserDataFolder; // Shared isolated profile context

    // Blocks execution clean until the target window triggers its Win32 close events
    await _windowManager.ShowDialogAsync("https://accounts.google.com/signin", loginDialog, external: true, owner: primaryFrame);

    // Primary window instantly pulls tokens stored by the dialog window. Active focus is returned cleanly to primaryFrame.
    var postAuthCookies = await primaryFrame.GetCookiesAsync("https://google.com");
    Console.WriteLine($"Fetched structural tokens: {postAuthCookies.Count}");
}

```



## API Specification

### `WindowStartPosition`

* `OSDefault`: Instructs Windows to apply classic shell cascade placement bounds (`CW_USEDEFAULT`).
* `CenterScreen`: Measures desktop system dimensions to place layouts directly in the central pixel workspace.
* `CenterOwner`: Scans parent boundary parameters to snap coordinates relative to the calling window context.

### `HostResourceAccessKind`

Determines resource cross-origin configuration access when using directory mapping layers.

* `Deny` ($0$): Refuses access entirely.
* `Allow` ($1$): Explicitly maps directory resources over standard paths securely.
* `DenyCors` ($2$): Limits access bounds across varying alternative virtual host environments.

### `WindowManager`

| Method                                           | Description                                                                                            |
|--------------------------------------------------|--------------------------------------------------------------------------------------------------------|
| `RunDesktop(window, path)`                       | For Minimal setup, starts the main message loop and navigates to a specific URI.                       |
| `Initialize(string baseUrl)`                     | Sets the global root URL for relative route evaluation.                                                |
| `OpenAsync(path, window, isMainWindow)`          | Spawns an async window. Task completes upon navigation finish.                                         |
| `ShowDialogAsync(path, window, external, owner)` | Spawns a window and blocks until closure. Manages structural Win32 parent handle interaction tracking. |
| `GetWindow(Guid id)`                             | Looks up a tracked window by registration ID.                                                          |
| `CloseWindow(Guid id)`                           | Triggers destruction of a specific tracked window.                                                     |
| `CloseAllWindows()`                              | Closes all tracked secondary instances.                                                                |

### `Window`

| Property / Event     | Type                   | Description                                              |
|----------------------|------------------------|----------------------------------------------------------|
| `StartPosition`      | WindowStartPosition    | Evaluated positional behavior rules engine parameters.   |
| `BackgroundColor`    | Color                  | Sets the clear-color paint target.                       |
| `EnableDarkMode`     | bool                   | Enforces immersive dark title bars via DWM attributes.   |
| `IconPath`           | string                 | Path to a `.ico` taskbar resource.                       |
| `UserDataFolder`     | string                 | Profile location for cache, cookies, and database state. |
| `IsBorderless`       | bool                   | Strips standard OS window chrome.                        |
| `UserAgentOverride`  | string                 | Custom client browser identification header.             |
| `X`, `Y`             | int?                   | Specific screen positioning coordinates.                 |
| `Width`, `Height`    | int?                   | Target outer window layout edge parameters.              |
| `WindowState`        | WindowState            | Normal, Minimized, or Maximized parameters.              |
| `IsTopMost`          | bool                   | Hardware-pinned over other desktop frames.               |
| `Displays`           | IReadOnlyList          | Retrieves connected monitor workspace geometries.        |
| `Options`            | WebViewSettingsOptions | Internal Chromium execution flags and configuration.     |
| `WebMessageReceived` | Action                 | Event triggered when inbound Javascript maps messaging.  |
| `DisplaysChanged`    | Action                 | Event triggered when runtime display topologies alter.   |

### `WebViewSettingsOptions`

| Property                         | Type    | Default | Description                                                                         |
|----------------------------------|---------|---------|-------------------------------------------------------------------------------------|
| `EnableGpuAcceleration`          | bool    | `true`  | Toggles hardware acceleration processing pools.                                     |
| `DisableWebSecurity`             | bool    | `true`  | Disables traditional sandbox CORS parameters for local testing.                     |
| `AdditionalBrowserArguments`     | string? | `null`  | Appends native command line switches directly into Chromium initialization threads. |
| `AreDevToolsEnabled`             | bool    | `false` | Enables contextual Chromium inspector console elements.                             |
| `IsScriptEnabled`                | bool    | `true`  | Authorizes or halts document Javascript evaluation loops.                           |
| `IsWebMessageEnabled`            | bool    | `true`  | Controls structural JSON bridge transmission pipelines.                             |
| `IsZoomControlEnabled`           | bool    | `false` | Allows or blocks standard scale interactions.                                       |
| `AreDefaultScriptDialogsEnabled` | bool    | `false` | Enables default prompt / alert window sequences.                                    |
| `IsBuiltInErrorPageEnabled`      | bool    | `true`  | Toggles built-in Chromium navigation fail frames.                                   |
| `IsPasswordAutosaveEnabled`      | bool    | `false` | Controls whether user authentication forms save profile data locally.               |
| `IsGeneralAutofillEnabled`       | bool    | `false` | Automates generic input element history prediction logic.                           |
| `IsGestureAutoplayBlocked`       | bool    | `false` | Halts media assets from playing without direct user focus taps.                     |
| `AreHostObjectsAllowed`          | bool    | `true`  | Dispatches direct native bridge binding references.                                 |
| `IsPinchZoomEnabled`             | bool    | `false` | Resolves multi-touch zoom action layouts.                                           |

### `Platform`

| Method                                                 | Description                                                                                                                 |
|--------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------|
| `ShowOpenFileDialog(title, filter, owner)`             | Opens native Win32 `IFileOpenDialog` for a single file. Returns selected path or null.                                      |
| `ShowOpenMultipleFilesDialog(title, filter, owner)`    | Opens native Win32 `IFileOpenDialog` configured for multiple files. Returns array of paths.                                 |
| `ShowSaveFileDialog(title, filter, defaultExt, owner)` | Opens native Win32 `IFileSaveDialog` for exporting data. Returns destination path or null.                                  |
| `ShowFolderBrowserDialog(title, owner)`                | Opens native Win32 `IFileOpenDialog` constrained to directories only. Returns folder path or null.                          |
| `GetInstalledFonts()`                                  | Scans `HKLM` and `HKCU` registry tables to map registered space-separated family strings directly to absolute system paths. |
| `IsSystemInDarkMode()`                                 | Evaluates systemic personal preference parameters from Windows DWM configuration states.                                    |



## License

Licensed under the MIT License. See [LICENSE](LICENSE) for details.