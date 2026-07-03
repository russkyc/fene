using System.Collections.Concurrent;

namespace Russkyc.Fene;

public class WindowManager
{
    private WebViewWindow? _mainWindow;
    private string _baseUrl = string.Empty;
    
    // Thread-safe tracking collection for all spawned secondary windows
    private readonly ConcurrentDictionary<Guid, WebViewWindow> _activeWindows = new();
    
    public WebViewWindow? MainWindow => _mainWindow;

    internal void Initialize(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Spawns a new native window asynchronously. 
    /// The Task completes when the Blazor page has fully finished loading inside the WebView2 engine.
    /// </summary>
    public Task<Guid> OpenAsync(string path, WebViewWindow window, bool isMainWindow = false)
    {
        var tcs = new TaskCompletionSource<Guid>();

        var windowId = Guid.NewGuid();
        
        if (!isMainWindow)
        {
            _activeWindows.TryAdd(windowId, window);
            window.Closed += () => _activeWindows.TryRemove(windowId, out _);
        }
        else
        {
            _mainWindow = window;
        }

        // Bridge the async gap: complete the Task when the page is fully ready
        window.NavigationCompleted += (_) => 
        {
            tcs.TrySetResult(windowId);
        };

        // Failsafe: If the user aggressively closes the window before it even finishes loading
        window.Closed += () =>
        {
            tcs.TrySetCanceled();
        };

        var uiThread = new Thread(() =>
        {
            string targetUrl = path.StartsWith("http://") || path.StartsWith("https://")
                ? path
                : $"{_baseUrl}/{path.TrimStart('/')}";

            window.ShowAndRun(targetUrl);
        })
        {
            IsBackground = true 
        };

        // UI threads MUST be STA. We cannot use Task.Run() here.
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        return tcs.Task;
    }

    /// <summary>
    /// Spawns a new native window and waits asynchronously until the user CLOSES it.
    /// Used for the traditional Dialog/Modal pattern.
    /// </summary>
    public Task ShowDialogAsync(string path, WebViewWindow window, bool external = false)
    {
        var tcs = new TaskCompletionSource();
        var windowId = Guid.NewGuid();
        
        _activeWindows.TryAdd(windowId, window);

        // Complete the Task ONLY when the window is physically destroyed
        window.Closed += () => 
        {
            _activeWindows.TryRemove(windowId, out _);
            tcs.TrySetResult();
        };

        var uiThread = new Thread(() =>
        {
            var targetUrl = external ? path : path.StartsWith("http://") || path.StartsWith("https://")
                ? path
                : $"{_baseUrl}/{path.TrimStart('/')}";

            window.ShowAndRun(targetUrl);
        })
        {
            IsBackground = true 
        };

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        return tcs.Task;
    }
    
    /// <summary>
    /// Retrieves a specific active window by its tracking ID.
    /// </summary>
    public WebViewWindow? GetWindow(Guid id)
    {
        _activeWindows.TryGetValue(id, out var window);
        return window;
    }
    
    /// <summary>
    /// Closes a specific window by its tracking ID.
    /// </summary>
    /// <param name="id"></param>
    public void CloseWindow(Guid id)
    {
        if (!_activeWindows.TryGetValue(id, out var window)) return;
        window.Close();
        _activeWindows.TryRemove(id, out _);
    }

    /// <summary>
    /// Closes all active secondary windows tracked by the manager.
    /// </summary>
    public void CloseAllWindows()
    {
        foreach (var window in _activeWindows.Values)
        {
            window.Close();
        }
    }
}