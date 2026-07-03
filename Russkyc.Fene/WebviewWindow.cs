using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Russkyc.Fene;

// A clean enum for developer consumption
public enum WindowState
{
    Normal,
    Minimized,
    Maximized
}

public class WebViewWindow(string title = "WebView Window", int width = 600, int height = 500)
{
    public const uint WmSynchronizationcontextWorkAvailable = PInvoke.WM_USER + 1;
    private static readonly ConcurrentDictionary<HWND, WebViewWindow> WindowMap = new();

    private HWND _hwnd;
    private CoreWebView2Controller? _controller;
    private UiThreadSynchronizationContext? _uiThreadSyncCtx;

    private struct HostMapping
    {
        public string HostName { get; set; }
        public string FolderPath { get; set; }
        public HostResourceAccessKind AccessKind { get; set; }
    }

    private readonly List<HostMapping> _mappings = new();

    // --- Configuration Hooks ---
    public Color BackgroundColor { get; set; } = Color.Transparent;
    public bool EnableDarkMode { get; set; } = false;
    public string? IconPath { get; set; } = null;
    public string? UserDataFolder { get; set; } = null;
    public bool ShowOnlyAfterLoad { get; set; } = false;
    public bool IsBorderless { get; set; } = false;

    // --- NEW: Location and State ---
    public int? X { get; set; }
    public int? Y { get; set; }

    private WindowState _windowState = WindowState.Normal;

    public WindowState WindowState
    {
        get => _windowState;
        set
        {
            _windowState = value;
            if (!_hwnd.IsNull) ApplyWindowState(); // Updates live window instantly
        }
    }

    public WebViewSettingsOptions Options { get; } = new();

    // --- Exposed Native Events ---
    public event Action<string>? WebMessageReceived;
    public event Action<string>? NavigationStarted;
    public event Action<string>? NavigationCompleted;
    public event Action? Closed;
    public event Action? DisplaysChanged;

    public void MapVirtualHost(string hostName, string folderPath, HostResourceAccessKind accessKind)
    {
        _mappings.Add(new HostMapping { HostName = hostName, FolderPath = folderPath, AccessKind = accessKind });
    }

    // --- Exhaustive Runtime APIs ---

    public void Navigate(string url) => _controller?.CoreWebView2.Navigate(url);

    public async Task<string> ExecuteScriptAsync(string script)
    {
        if (_controller == null) return string.Empty;
        return await _controller.CoreWebView2.ExecuteScriptAsync(script);
    }

    public void PostWebMessageAsString(string message) => _controller?.CoreWebView2.PostWebMessageAsString(message);
    public void PostWebMessageAsJson(string json) => _controller?.CoreWebView2.PostWebMessageAsJson(json);
    public void OpenDevTools() => _controller?.CoreWebView2.OpenDevToolsWindow();

    /// <summary>
    /// Dynamically moves the window to a new location on the screen.
    /// </summary>
    public void SetLocation(int x, int y)
    {
        X = x;
        Y = y;
        if (!_hwnd.IsNull)
        {
            // SWP_NOSIZE and SWP_NOZORDER ensure we ONLY change the position, not the dimensions or depth.
            PInvoke.SetWindowPos(_hwnd, HWND.Null, x, y, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
        }
    }

    public unsafe void ShowAndRun(string startUrl)
    {
#if DEBUG
        PInvoke.AllocConsole();
#endif

        HINSTANCE hInstance = PInvoke.GetModuleHandle((char*)null);
        string className = $"WebViewWindowClass_{Guid.NewGuid():N}";

        COLORREF winColor =
            new COLORREF((uint)((BackgroundColor.B << 16) | (BackgroundColor.G << 8) | BackgroundColor.R));
        HBRUSH backgroundBrush = PInvoke.CreateSolidBrush(winColor);
        if (backgroundBrush.IsNull)
        {
            backgroundBrush = (HBRUSH)(IntPtr)(SYS_COLOR_INDEX.COLOR_BACKGROUND + 1);
        }

        HICON hIcon = default;
        if (!string.IsNullOrEmpty(IconPath) && File.Exists(IconPath))
        {
            fixed (char* iconPathPtr = IconPath)
            {
                var hImage = PInvoke.LoadImage(default, iconPathPtr, GDI_IMAGE_TYPE.IMAGE_ICON, 0, 0,
                    IMAGE_FLAGS.LR_LOADFROMFILE | IMAGE_FLAGS.LR_DEFAULTSIZE);
                hIcon = new HICON(hImage.Value);
            }
        }

        if (hIcon.IsNull)
        {
            hIcon = PInvoke.LoadIcon(hInstance, new PCWSTR((char*)32512));
        }

        fixed (char* classNamePtr = className)
        {
            WNDCLASSW wc = new()
            {
                lpfnWndProc = StaticWndProc,
                lpszClassName = classNamePtr,
                hInstance = hInstance,
                hbrBackground = backgroundBrush,
                hIcon = hIcon,
                style = WNDCLASS_STYLES.CS_VREDRAW | WNDCLASS_STYLES.CS_HREDRAW
            };
            if (PInvoke.RegisterClass(wc) == 0) throw new Exception("Win32 class registration failed.");
        }

        // Apply custom X/Y if provided, otherwise let Windows pick the default
        var startX = X ?? PInvoke.CW_USEDEFAULT;
        var startY = Y ?? PInvoke.CW_USEDEFAULT;

        // Determine if we are rendering OS window chrome or a raw popup canvas
        var style = IsBorderless ? WINDOW_STYLE.WS_POPUP : WINDOW_STYLE.WS_OVERLAPPEDWINDOW;

        fixed (char* windowNamePtr = title)
        fixed (char* classNamePtr = className)
        {
            _hwnd = PInvoke.CreateWindowEx(0, classNamePtr, windowNamePtr, style, startX,
                startY, width, height, default, default, hInstance, null);
        }

        if (_hwnd.IsNull) throw new Exception("Window creation handle extraction failed.");

        WindowMap[_hwnd] = this;

        if (!hIcon.IsNull)
        {
            var iconPtrValue = (nint)hIcon.Value;
            PInvoke.SendMessage(_hwnd, PInvoke.WM_SETICON, 0, iconPtrValue);
            PInvoke.SendMessage(_hwnd, PInvoke.WM_SETICON, 1, iconPtrValue);
        }

        if (EnableDarkMode)
        {
            int useDarkMode = 1;
            HRESULT hr = PInvoke.DwmSetWindowAttribute(_hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
                &useDarkMode, sizeof(int));
            if (hr.Failed)
            {
                int fallbackAttribute = 19;
                PInvoke.DwmSetWindowAttribute(_hwnd, (DWMWINDOWATTRIBUTE)fallbackAttribute, &useDarkMode, sizeof(int));
            }
        }

        if (!ShowOnlyAfterLoad)
        {
            ApplyWindowState();
        }

        _uiThreadSyncCtx = new UiThreadSynchronizationContext(_hwnd);
        SynchronizationContext.SetSynchronizationContext(_uiThreadSyncCtx);

        _ = InitializeWebViewAsync(startUrl);

        MSG msg;
        while (true)
        {
            int bRet = PInvoke.GetMessage(out msg, default, 0, 0).Value;

            if (bRet == 0 || bRet == -1)
            {
                break;
            }

            PInvoke.TranslateMessage(msg);
            PInvoke.DispatchMessage(msg);
        }

        WindowMap.TryRemove(_hwnd, out _);
    }

    public void Close()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.SendMessage(_hwnd, PInvoke.WM_CLOSE);
        }
    }

    /// <summary>
    /// Gets the live list of physical screens currently connected to the system.
    /// Computes synchronously on-demand.
    /// </summary>
    public unsafe IReadOnlyList<Display> Displays
    {
        get
        {
            var displays = new List<Display>();

            // The OS fires this callback synchronously for every screen it finds
            BOOL Callback(HMONITOR hMonitor, HDC hdc, RECT* rect, LPARAM lparam)
            {
                var mi = new MONITORINFO();
                mi.cbSize = (uint)Marshal.SizeOf<MONITORINFO>();

                if (PInvoke.GetMonitorInfo(hMonitor, ref mi))
                {
                    displays.Add(new Display
                    {
                        Bounds = new Rectangle(mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right - mi.rcMonitor.left, mi.rcMonitor.bottom - mi.rcMonitor.top), WorkingArea = new Rectangle(mi.rcWork.left, mi.rcWork.top, mi.rcWork.right - mi.rcWork.left, mi.rcWork.bottom - mi.rcWork.top), IsPrimary = (mi.dwFlags & 1) == 1 // MONITORINFOF_PRIMARY is exactly 1 natively
                    });
                }

                return true;
            }

            // Trigger the native enumeration 
            PInvoke.EnumDisplayMonitors(default, null, Callback, default);

            return displays;
        }
    }

    private void ApplyWindowState()
    {
        var cmd = _windowState switch
        {
            WindowState.Maximized => SHOW_WINDOW_CMD.SW_MAXIMIZE,
            WindowState.Minimized => SHOW_WINDOW_CMD.SW_MINIMIZE,
            _ => SHOW_WINDOW_CMD.SW_NORMAL
        };
        PInvoke.ShowWindow(_hwnd, cmd);
    }

    private static LRESULT StaticWndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (WindowMap.TryGetValue(hwnd, out var instance)) return instance.HandleMessage(msg, wParam, lParam);
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private LRESULT HandleMessage(uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_SIZE:
                int windowWidth = unchecked((short)(uint)lParam.Value);
                int windowHeight = unchecked((short)((uint)lParam.Value >> 16));
                if (_controller != null) _controller.Bounds = new Rectangle(0, 0, windowWidth, windowHeight);
                break;

            case WmSynchronizationcontextWorkAvailable:
                _uiThreadSyncCtx?.RunAvailableWorkOnCurrentThread();
                break;
            
            case PInvoke.WM_DISPLAYCHANGE:
                DisplaysChanged?.Invoke();
                break;
            
            case PInvoke.WM_CLOSE:
                try
                {
                    _controller?.Close();
                    _controller = null;
                }
                catch
                {
                    // Ignored
                }

                PInvoke.DestroyWindow(_hwnd);
                return default;

            case PInvoke.WM_DESTROY:
                try
                {
                    _controller?.Close();
                    _controller = null;
                }
                catch
                {
                    // Ignored
                }

                Closed?.Invoke();
                PInvoke.PostQuitMessage(0);
                return default;
        }

        return PInvoke.DefWindowProc(_hwnd, msg, wParam, lParam);
    }

    private async Task InitializeWebViewAsync(string targetUrl)
    {
        try
        {
            var args = new List<string>();
            if (!Options.EnableGpuAcceleration) args.Add("--disable-gpu");
            if (Options.DisableWebSecurity) args.Add("--disable-web-security");
            if (!string.IsNullOrEmpty(Options.AdditionalBrowserArguments)) args.Add(Options.AdditionalBrowserArguments);

            var envOptions = new CoreWebView2EnvironmentOptions(string.Join(" ", args));

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder,
                options: envOptions
            );

            _controller = await environment.CreateCoreWebView2ControllerAsync(_hwnd);

            foreach (var mapping in _mappings)
            {
                _controller.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    mapping.HostName,
                    mapping.FolderPath,
                    (CoreWebView2HostResourceAccessKind)mapping.AccessKind
                );
            }

            _controller.DefaultBackgroundColor = BackgroundColor;
            if (EnableDarkMode)
                _controller.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;

            var s = _controller.CoreWebView2.Settings;
            s.AreDevToolsEnabled = Options.AreDevToolsEnabled;
            s.IsScriptEnabled = Options.IsScriptEnabled;
            s.IsWebMessageEnabled = Options.IsWebMessageEnabled;
            s.IsZoomControlEnabled = Options.IsZoomControlEnabled;
            s.AreDefaultScriptDialogsEnabled = Options.AreDefaultScriptDialogsEnabled;
            s.IsBuiltInErrorPageEnabled = Options.IsBuiltInErrorPageEnabled;
            s.IsPasswordAutosaveEnabled = Options.IsPasswordAutosaveEnabled;
            s.IsGeneralAutofillEnabled = Options.IsGeneralAutofillEnabled;
            s.AreHostObjectsAllowed = Options.AreHostObjectsAllowed;
            s.IsPinchZoomEnabled = Options.IsPinchZoomEnabled;
            s.IsSwipeNavigationEnabled = Options.IsSwipeNavigationEnabled;

            s.UserAgent = s.UserAgent + (Options.IsGestureAutoplayBlocked ? " BlockGestureAutoplay" : "");

            _controller.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                var text = e.TryGetWebMessageAsString();
                if (!string.IsNullOrEmpty(text)) WebMessageReceived?.Invoke(text);
            };
            _controller.CoreWebView2.NavigationStarting += (_, e) => NavigationStarted?.Invoke(e.Uri);
            _controller.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                if (ShowOnlyAfterLoad)
                {
                    ApplyWindowState(); // Apply proper state on load instead of SW_NORMAL blindly
                    PInvoke.UpdateWindow(_hwnd);
                }

                NavigationCompleted?.Invoke(_controller.CoreWebView2.Source);
            };

            PInvoke.GetClientRect(_hwnd, out var rect);
            _controller.Bounds = new Rectangle(0, 0, rect.right, rect.bottom);
            _controller.IsVisible = true;
            _controller.CoreWebView2.Navigate(targetUrl);
        }
        catch (Exception ex)
        {
            PInvoke.MessageBox(_hwnd, $"WebView2 Exception: {ex.Message}", "Critical Failure",
                MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
            Environment.Exit(1);
        }
    }
}