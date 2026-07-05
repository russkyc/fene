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
    private Window? _mainWindow;
    private string _baseUrl = string.Empty;
    private readonly ConcurrentDictionary<Guid, Window> _activeWindows = new();
    private static readonly Lazy<WindowManager> _shared = new(() => new WindowManager());
    public static WindowManager Shared => _shared.Value;
    
    /// <summary>
    /// Gets the primary application window, if currently initialized.
    /// </summary>
    public Window? MainWindow => _mainWindow;

    internal void Initialize(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public void RunDesktop(Window mainWindow, string url)
    {
        mainWindow.Closed += () => 
        {
            Environment.Exit(0);
        };

        // Fix: If the entry thread isn't STA, spawn an isolated safe STA context
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            var uiThread = new Thread(() =>
            {
                mainWindow.ShowAndRun(url);
            }) { IsBackground = false };

            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            uiThread.Join(); // Blocks entry thread nicely until native UI finishes
        }
        else
        {
            mainWindow.ShowAndRun(url);
            Thread.Sleep(Timeout.Infinite);
        }
    }
    
    /// <summary>
    /// Asynchronously spawns a native window. The task completes when the navigation 
    /// to the requested path is fully finished.
    /// </summary>
    public Task<Guid> OpenAsync(string path, Window window, bool isMainWindow = false)
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
    public Task ShowDialogAsync(string path, Window window, bool external = false, Window? owner = null)
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
    public Window? GetWindow(Guid id) => _activeWindows.TryGetValue(id, out var window) ? window : null;

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