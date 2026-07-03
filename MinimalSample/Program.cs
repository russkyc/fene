using Russkyc.Fene;

namespace MinimalSample;

public static class Program
{
    [STAThread]
    static void Main()
    {
        var appCacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Data");

        var thread1 = new Thread(() =>
        {
            var window1 = new WebViewWindow("App Window 1", 1000, 700)
            {
                EnableDarkMode = true,
                BackgroundColor = System.Drawing.Color.FromArgb(255, 30, 30, 30),
                UserDataFolder = Path.Combine(appCacheDirectory, "Window1"),
                ShowOnlyAfterLoad = true
            };

            window1.MapVirtualHost(
                hostName: "appassets.local",
                folderPath: @"C:\Users\russ\VsCodeProjects\reveal-landing\images\",
                accessKind: HostResourceAccessKind.Allow
            );

            window1.MapVirtualHost(
                hostName: "app.local",
                folderPath: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"),
                accessKind: HostResourceAccessKind.Allow
            );

            window1.WebMessageReceived += (message) =>
            {
                Console.WriteLine($"[Window 1 Received]: {message}");
                _ = window1.ExecuteScriptAsync($"console.log('Echo Window 1: {message}')");
            };

            window1.ShowAndRun("http://app.local/index.html");
        });

        thread1.SetApartmentState(ApartmentState.STA); // Strict requirement for WebView2/unmanaged window message hooks
        thread1.Start();

        var thread2 = new Thread(() =>
        {
            var window2 = new WebViewWindow("App Window 2", 800, 600)
            {
                EnableDarkMode = true,
                UserDataFolder = Path.Combine(appCacheDirectory, "Window2"),
                ShowOnlyAfterLoad = true
            };

            window2.WebMessageReceived += (message) => { Console.WriteLine($"[Window 2 Received]: {message}"); };

            // Navigate to an external location completely unblocked by Window 1
            window2.ShowAndRun("https://google.com");
        });

        thread2.SetApartmentState(ApartmentState.STA);
        // thread2.Start();

        // Block the primary process thread execution loop until both window background tasks exit completely
        thread1.Join();
        // thread2.Join();
    }
}