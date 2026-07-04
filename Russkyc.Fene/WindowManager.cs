using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Russkyc.Fene;

/// <summary>
/// Orchestrates the lifecycle of native WebView2 windows, maintaining tracking 
/// and thread-safe communication for multiple application windows.
/// </summary>
public class WindowManager
{
    private WebViewWindow? _mainWindow;
    private string _baseUrl = string.Empty;
    private readonly ConcurrentDictionary<Guid, WebViewWindow> _activeWindows = new();

    /// <summary>
    /// Gets the primary application window, if currently initialized.
    /// </summary>
    public WebViewWindow? MainWindow => _mainWindow;

    internal void Initialize(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Asynchronously spawns a native window. The task completes when the navigation 
    /// to the requested path is fully finished.
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

        window.NavigationCompleted += (_) => tcs.TrySetResult(windowId);
        window.Closed += () => tcs.TrySetCanceled();

        var uiThread = new Thread(() =>
        {
            string targetUrl = path.StartsWith("http://") || path.StartsWith("https://")
                ? path
                : $"{_baseUrl}/{path.TrimStart('/')}";

            window.ShowAndRun(targetUrl);
        }) { IsBackground = true };

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        return tcs.Task;
    }

    /// <summary>
    /// Spawns a window and awaits its total closure, useful for the Dialog/Modal pattern.
    /// </summary>
    /// <summary>
    /// Spawns a window and awaits its total closure, useful for the Dialog/Modal pattern.
    /// Optionally accepts an owner window to create a true, blocking Win32 modal.
    /// </summary>
    public Task ShowDialogAsync(string path, WebViewWindow window, bool external = false, WebViewWindow? owner = null)
    {
        var tcs = new TaskCompletionSource();
        var windowId = Guid.NewGuid();

        _activeWindows.TryAdd(windowId, window);

        IntPtr ownerHandle = owner?.Handle ?? IntPtr.Zero;

        // Block input to the parent window to enforce modality upfront
        if (ownerHandle != IntPtr.Zero)
        {
            PInvoke.EnableWindow(new HWND(ownerHandle), false);
        }

        window.Closed += () =>
        {
            // Safety fallback check if window loop exited atypically
            if (ownerHandle != IntPtr.Zero)
            {
                PInvoke.EnableWindow(new HWND(ownerHandle), true);
            }

            _activeWindows.TryRemove(windowId, out _);
            tcs.TrySetResult();
        };

        var uiThread = new Thread(() =>
        {
            var targetUrl = external
                ? path
                : (path.StartsWith("http://") || path.StartsWith("https://")
                    ? path
                    : $"{_baseUrl}/{path.TrimStart('/')}");

            window.ShowAndRun(targetUrl, owner);
        }) { IsBackground = true };

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        return tcs.Task;
    }

    /// <summary>
    /// Locates an active window by its unique tracking identifier.
    /// </summary>
    public WebViewWindow? GetWindow(Guid id) => _activeWindows.TryGetValue(id, out var window) ? window : null;

    /// <summary>
    /// Closes and removes an active secondary window by its identifier.
    /// </summary>
    public void CloseWindow(Guid id)
    {
        if (_activeWindows.TryRemove(id, out var window))
            window.Close();
    }

    /// <summary>
    /// Closes all active secondary windows managed by this instance.
    /// </summary>
    public void CloseAllWindows()
    {
        foreach (var window in _activeWindows.Values)
            window.Close();
    }
}