using System.Drawing;
using Russkyc.Fene;

var prefersDark = Platform.IsSystemInDarkMode();
var windowManager = WindowManager.Shared;

var window = WebViewWindowBuilder
    .Create("Simple App Window", 1000, 700)
    .WithStartPosition(WindowStartPosition.CenterScreen)
    .UseDarkMode(prefersDark)
    .WithBackgroundColor(prefersDark ? ColorTranslator.FromHtml("#202020") : Color.White)
    .MapVirtualHost("app.local", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"))
    .Build();

// Intercept routing messages based on incoming command tokens
window.OnWebMessageReceived<WebMessage>(async message =>
{
    if (message == null)
    {
        Console.WriteLine("[Error]: Failed to deserialize incoming message.");
        return;
    }

    Console.WriteLine($"[Processed message]: {message.Request}");

    if (message.Request == "notify_request")
    {
        Platform.ShowMessageBox($"C# Received notification request with payload: {message.Payload}",
            "Web Message Interop", owner: window);
    }
    else if (message.Request == "dialog_request")
    {
        var dialog = WebViewWindowBuilder
            .Create("Dialog Window", 600, 450)
            .WithStartPosition(WindowStartPosition.CenterOwner)
            .UseDarkMode(prefersDark)
            .WithBackgroundColor(prefersDark ? ColorTranslator.FromHtml("#202020") : Color.White)
            .ConfigureSettings(settings =>
            {
                settings.AreDefaultContextMenusEnabled = true;
                settings.AreDevToolsEnabled = true;
            })
            .Build();

        // Enforce true, non-blocking Win32 modality through the WindowManager abstraction
        await windowManager.ShowDialogAsync("https://www.google.com", dialog, owner: window);
    }
    else if (message.Request == "open_devtools_request")
    {
        window.OpenDevTools();
    }
    else
    {
        // Custom operational string payloads echo back to browser context safely
        _ = window.ExecuteScriptAsync($"console.log('Echo processing complete: {message.Payload}')");
    }
});

window.ClosingAsync = () =>
{
    var confirmClose = Platform.ShowConfirmationBox(
        "You are about to exit the application, are you sure?", 
        "Exit Confirmation",
        owner: window
    );
    return Task.FromResult(confirmClose);
};

windowManager.RunDesktop(window, "http://app.local/index.html");

// Minimal webmessage wrapper
internal record WebMessage(string Request, string? Payload);