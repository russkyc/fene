using System.Collections.Concurrent;
using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Russkyc.Fene;

/// <summary>
/// Represents a native window hosting a WebView2 control.
/// </summary>
public class Window(
    string title = "WebView Window",
    int? width = null,
    int? height = null,
    int? minWidth = null,
    int? minHeight = null)
{
    public const uint WmSynchronizationcontextWorkAvailable = PInvoke.WM_USER + 1;
    public const uint WmProcessWorkQueue = PInvoke.WM_USER + 2;
    private const uint WmGetMinMaxInfo = 0x0024; // Native Win32 constant for window sizing constraints
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcHitTest = 0x0084;

    private static readonly HWND HwndTopmost = new(-1);
    private static readonly HWND HwndNotopmost = new(-2);

    private static readonly ConcurrentDictionary<HWND, Window> WindowMap = new();

    private HWND _hwnd;
    private HWND _ownerHwnd;
    private CoreWebView2Controller? _controller;
    private UiThreadSynchronizationContext? _uiThreadSyncCtx;
    private readonly ConcurrentQueue<Action> _workQueue = new();

    // Internal Win32 Structs mapped specifically to avoid CsWin32 generation requirements
    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Minmaxinfo
    {
        public Win32Point ptReserved;
        public Win32Point ptMaxSize;
        public Win32Point ptMaxPosition;
        public Win32Point ptMinTrackSize;
        public Win32Point ptMaxTrackSize;
    }

    private struct HostMapping
    {
        public string HostName { get; set; }
        public string FolderPath { get; set; }
        public HostResourceAccessKind AccessKind { get; set; }
    }
    
    private bool _isClosingApproved;
    private readonly List<HostMapping> _mappings = new();

    public WindowStartPosition StartPosition { get; set; } = WindowStartPosition.OsDefault;
    public Color BackgroundColor { get; set; } = Color.Transparent;
    public bool EnableDarkMode { get; set; } = false;
    public string? IconPath { get; set; } = null;
    public string? UserDataFolder { get; set; } = null;
    public bool ShowOnlyAfterLoad { get; set; } = false;
    public bool IsBorderless { get; set; } = false;

    public int? Width { get; set; } = width;
    public int? Height { get; set; } = height;
    public int? MinWidth { get; set; } = minWidth;
    public int? MinHeight { get; set; } = minHeight;

    /// <summary>
    /// Gets or sets the custom User-Agent string for this window.
    /// </summary>
    public string? UserAgentOverride { get; set; }

    public int? X { get; set; }
    public int? Y { get; set; }

    public unsafe IntPtr Handle => (IntPtr)_hwnd.Value;

    private WindowState _windowState = WindowState.Normal;

    public WindowState WindowState
    {
        get => _windowState;
        set
        {
            _windowState = value;
            if (!_hwnd.IsNull) ApplyWindowState();
        }
    }

    private bool _isTopMost;

    public bool IsTopMost
    {
        get => _isTopMost;
        set
        {
            _isTopMost = value;
            if (!_hwnd.IsNull) ApplyTopMostState();
        }
    }

    public WebViewSettingsOptions Options { get; } = new();

    public event Action<string>? WebMessageReceived;
    public event Action<JsonDocument>? WebMessageJsonReceived;
    public event Action<string>? NavigationStarted;
    public event Action<string>? NavigationCompleted;
    public event Action? Closed;
    public event Action? DisplaysChanged;

    public Func<Task<bool>>? ClosingAsync { get; set; }

    public void MapVirtualHost(string hostName, string folderPath, HostResourceAccessKind accessKind)
    {
        _mappings.Add(new HostMapping { HostName = hostName, FolderPath = folderPath, AccessKind = accessKind });
    }

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
    /// Sets the window position.
    /// </summary>
    public void SetLocation(int x, int y)
    {
        X = x;
        Y = y;
        if (!_hwnd.IsNull)
        {
            PInvoke.SetWindowPos(_hwnd, HWND.Null, x, y, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
        }
    }

    /// <summary>
    /// Brings the window to the foreground.
    /// </summary>
    public void BringToFront()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.SetForegroundWindow(_hwnd);
        }
    }

    /// <summary>
    /// Clears all browsing data from the associated profile.
    /// </summary>
    public Task ClearBrowsingDataAsync()
    {
        var tcs = new TaskCompletionSource();

        if (_controller == null || _uiThreadSyncCtx == null)
        {
            tcs.SetResult();
            return tcs.Task;
        }

        _uiThreadSyncCtx.Post(async void (_) =>
        {
            try
            {
                await _controller.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);

        return tcs.Task;
    }

    // Centralized queue method that wakes the message pump
    private void EnqueueWork(Action action)
    {
        _workQueue.Enqueue(action);

        if (!_hwnd.IsNull)
        {
            PInvoke.PostMessage(_hwnd, WmProcessWorkQueue, 0, 0);
        }
    }

    /// <summary>
    /// Retrieves cookies for the specified URI. If no URI is provided, returns all cookies.
    /// </summary>
    public Task<List<Cookie>> GetCookiesAsync(string uri = "")
    {
        var tcs = new TaskCompletionSource<List<Cookie>>();

        EnqueueWork(async void () =>
        {
            try
            {
                if (_controller == null)
                {
                    tcs.SetResult(new List<Cookie>());
                    return;
                }

                var targetUri = string.IsNullOrEmpty(uri) ? _controller.CoreWebView2.Source : uri;

                var wvCookies = await _controller.CoreWebView2.CookieManager.GetCookiesAsync(targetUri);
                var netCookies = new List<Cookie>();

                foreach (var wvCookie in wvCookies)
                {
                    try
                    {
                        netCookies.Add(wvCookie.ToSystemNetCookie());
                    }
                    catch
                    {
                        // ignored
                    }
                }

                tcs.SetResult(netCookies);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Generates a CookieContainer from the current window's cookies.
    /// </summary>
    public async Task<CookieContainer> GetCookieContainerAsync(string uri = "")
    {
        var cookies = await GetCookiesAsync(uri);
        var container = new CookieContainer();

        foreach (var cookie in cookies)
        {
            if (string.IsNullOrEmpty(cookie.Domain)) continue;

            container.Add(cookie);
        }

        return container;
    }

    /// <summary>
    /// Registers a specialized strongly-typed handler for incoming JSON structural objects.
    /// </summary>
    public void OnWebMessageReceived<T>(Action<T?> handler, JsonSerializerOptions? options = null) where T : class
    {
        WebMessageJsonReceived += (jsonDoc) =>
        {
            try
            {
                // Deserialize the root element directly into the requested target layout model
                var targetObject = jsonDoc.Deserialize<T>(options ?? new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                handler(targetObject);
            }
            catch
            {
                // Silently skip or log malformed payloads that do not map to Type T
            }
        };
    }

    internal unsafe void ShowAndRun(string startUrl, Window? owner = null)
    {
        var perMonitorV2 = new DPI_AWARENESS_CONTEXT((void*)-4);
        PInvoke.SetProcessDpiAwarenessContext(perMonitorV2);
#if DEBUG
        PInvoke.AllocConsole();
#endif
        if (owner != null)
        {
            _ownerHwnd = new HWND(owner.Handle);
        }

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

        // 1. Establish default layout choices
        var startX = PInvoke.CW_USEDEFAULT;
        var startY = PInvoke.CW_USEDEFAULT;
        var startWidth = Width ?? PInvoke.CW_USEDEFAULT;
        var startHeight = Height ?? PInvoke.CW_USEDEFAULT;

        // 2. If dimensions are explicitly defined, calculate requested positions
        if (Width.HasValue && Height.HasValue)
        {
            if (StartPosition == WindowStartPosition.CenterScreen)
            {
                int screenWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN);
                int screenHeight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN);
                startX = (screenWidth - Width.Value) / 2;
                startY = (screenHeight - Height.Value) / 2;
            }
            else if (StartPosition == WindowStartPosition.CenterOwner && owner != null)
            {
                PInvoke.GetWindowRect(new HWND(owner.Handle), out var ownerRect);
                int ownerWidth = ownerRect.right - ownerRect.left;
                int ownerHeight = ownerRect.bottom - ownerRect.top;
                startX = ownerRect.left + (ownerWidth - Width.Value) / 2;
                startY = ownerRect.top + (ownerHeight - Height.Value) / 2;
            }
            else if (X.HasValue && Y.HasValue)
            {
                startX = X.Value;
                startY = Y.Value;
            }
        }

        // Both window frames use OVERLAPPEDWINDOW structurally to retain native drop shadows, 
        // aero snapping, and window minimize/maximize animations under the hood.
        var style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW;
        IntPtr ownerHandle = owner?.Handle ?? IntPtr.Zero;

        fixed (char* windowNamePtr = title)
        fixed (char* classNamePtr = className)
        {
            _hwnd = PInvoke.CreateWindowEx(
                0,
                classNamePtr,
                windowNamePtr,
                style,
                startX,
                startY,
                startWidth,
                startHeight,
                new HWND(ownerHandle),
                default,
                hInstance,
                null);
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
            if (IsTopMost) ApplyTopMostState();
        }

        if (IsBorderless)
        {
            // Extend the bottom margin by 1 pixel. This triggers the Desktop Window Manager (DWM)
            // to compose native drop-shadows and corner-rounding layouts manually around our window frame.
            var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 1 };
            PInvoke.DwmExtendFrameIntoClientArea(_hwnd, &margins);

            // Forces Win32 to issue an active WM_NCCALCSIZE frame recalculation pass immediately
            PInvoke.SetWindowPos(_hwnd, HWND.Null, 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER |
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
                SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
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

    public unsafe IReadOnlyList<Display> Displays
    {
        get
        {
            var displays = new List<Display>();

            BOOL Callback(HMONITOR hMonitor, HDC hdc, RECT* rect, LPARAM lparam)
            {
                var mi = new MONITORINFO();
                mi.cbSize = (uint)Marshal.SizeOf<MONITORINFO>();

                if (PInvoke.GetMonitorInfo(hMonitor, ref mi))
                {
                    displays.Add(new Display
                    {
                        Bounds = new Rectangle(mi.rcMonitor.left, mi.rcMonitor.top,
                            mi.rcMonitor.right - mi.rcMonitor.left, mi.rcMonitor.bottom - mi.rcMonitor.top),
                        WorkingArea = new Rectangle(mi.rcWork.left, mi.rcWork.top, mi.rcWork.right - mi.rcWork.left,
                            mi.rcWork.bottom - mi.rcWork.top),
                        IsPrimary = (mi.dwFlags & 1) == 1
                    });
                }

                return true;
            }

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

    private void ApplyTopMostState()
    {
        var placementFlags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE;
        var insertAfterTarget = _isTopMost ? HwndTopmost : HwndNotopmost;

        PInvoke.SetWindowPos(_hwnd, insertAfterTarget, 0, 0, 0, 0, placementFlags);
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
            case WmNcCalcSize:
                if (IsBorderless && wParam.Value != 0)
                {
                    unsafe
                    {
                        RECT* rgrc = (RECT*)lParam.Value;
                        
                        // Save the absolute top position of the window box before the OS modifies it
                        var originalTop = rgrc[0].top;

                        // Let the OS calculate standard client area frame allocations (Left, Right, Bottom borders)
                        var lresult = PInvoke.DefWindowProc(_hwnd, msg, wParam, lParam);

                        // 3. Reclaim the title bar space by pulling the client area top back to the edge.
                        if (PInvoke.IsZoomed(_hwnd))
                        {
                            // When maximized, Windows pushes window borders off-screen by about 8px.
                            // We adjust the top back down slightly so your Blazor title bar isn't cut off.
                            int framePadding = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYFRAME) + 
                                               PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER);
                            rgrc[0].top = originalTop + framePadding;
                        }
                        else
                        {
                            // When normal/windowed, pull the content safely to the absolute top edge
                            rgrc[0].top = originalTop;
                        }

                        return lresult;
                    }
                }
                break;
                
            case WmNcHitTest:
                if (IsBorderless)
                {
                    // Let the native Win32 window manager evaluate the standard transparent borders first.
                    // This seamlessly handles left, right, bottom, and all diagonal corner resizes!
                    var hitResult = PInvoke.DefWindowProc(_hwnd, msg, wParam, lParam);
                    
                    // Since our client area now goes to the very top, we must manually handle the top resize edge.
                    if (hitResult.Value == 1) // 1 == HTCLIENT (Mouse is inside the app workspace)
                    {
                        // Unpack screen space mouse coordinates
                        int y = unchecked((short)((uint)lParam.Value >> 16));

                        // Convert to local window coordinates
                        PInvoke.GetWindowRect(_hwnd, out var rect);
                        int localY = y - rect.top;

                        // Define standard Windows top-edge resize thickness (approx 5-7 pixels)
                        const int topResizeThickness = 6;

                        if (localY < topResizeThickness && !PInvoke.IsZoomed(_hwnd))
                        {
                            return (LRESULT)12; // 12 == HTTOP (Tells Windows to change cursor to vertical resize arrow)
                        }

                        return (LRESULT)1; // 1 == HTCLIENT (Pass input normally to WebView2 / app-region: drag)
                    }
                    
                    return hitResult;
                }
                break;

            case WmGetMinMaxInfo:
                if (MinWidth.HasValue || MinHeight.HasValue)
                {
                    unsafe
                    {
                        var mmi = (Minmaxinfo*)lParam.Value;
                        if (MinWidth.HasValue) mmi->ptMinTrackSize.X = MinWidth.Value;
                        if (MinHeight.HasValue) mmi->ptMinTrackSize.Y = MinHeight.Value;
                    }

                    return default;
                }

                break;

            case WmProcessWorkQueue:
                while (_workQueue.TryDequeue(out var action))
                {
                    action();
                }

                return default;

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
                // If there's an async hook registered and it hasn't approved the close yet, intercept it.
                if (ClosingAsync != null && !_isClosingApproved)
                {
                    _ = EvaluateClosingAsync();
                    return default; // Handled: Prevents the window from closing right now
                }

                try
                {
                    _controller?.Close();
                    _controller = null;
                }
                catch
                {
                    // ignored
                }

                // destroying this window so the OS naturally passes active focus back to it.
                if (!_ownerHwnd.IsNull)
                {
                    PInvoke.EnableWindow(_ownerHwnd, true);
                    PInvoke.SetForegroundWindow(_ownerHwnd);
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
                    // ignored
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
            
            // Natively pass HTML app-region attributes straight to Win32 hit-testing mechanics
            try
            {
                s.IsNonClientRegionSupportEnabled = true;
            }
            catch (NotSupportedException)
            {
                // Fallback for systems running older WebView2 runtimes where the API isn't present
            }
            
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
            s.IsStatusBarEnabled = Options.IsStatusBarEnabled;
            s.AreDefaultContextMenusEnabled = Options.AreDefaultContextMenusEnabled;
            s.AreBrowserAcceleratorKeysEnabled = Options.AreBrowserAcceleratorKeysEnabled;

            if (!string.IsNullOrEmpty(UserAgentOverride))
            {
                s.UserAgent = UserAgentOverride;
            }
            else
            {
                s.UserAgent = s.UserAgent + (Options.IsGestureAutoplayBlocked ? " BlockGestureAutoplay" : "");
            }

            if (Options.AutomaticallyAllowAllPermissions)
            {
                _controller.CoreWebView2.PermissionRequested += (_, e) =>
                {
                    e.State = CoreWebView2PermissionState.Allow;
                };
            }

            if (Options.PreventDragAndDropNavigation)
            {
                await _controller.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "window.addEventListener('dragover', e => e.preventDefault()); window.addEventListener('drop', e => e.preventDefault());"
                );
            }

            _controller.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                // Capture the raw text safely from WebView2 context
                var rawText = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(rawText)) return;

                // 1. Invoke standard raw string intercept listeners
                WebMessageReceived?.Invoke(rawText);

                // 2. Safely parse JSON structure to feed strongly-typed handlers
                if (WebMessageJsonReceived != null)
                {
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(rawText);
                        WebMessageJsonReceived.Invoke(jsonDoc);
                    }
                    catch (JsonException)
                    {
                        // The string was a plain word or token, not structural JSON; ignore fallback parse loops safely
                    }
                }
            };
            _controller.CoreWebView2.NavigationStarting += (_, e) => NavigationStarted?.Invoke(e.Uri);
            _controller.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                if (ShowOnlyAfterLoad)
                {
                    ApplyWindowState();
                    if (IsTopMost) ApplyTopMostState();
                    PInvoke.UpdateWindow(_hwnd);
                }

                NavigationCompleted?.Invoke(_controller.CoreWebView2.Source);
            };

            PInvoke.GetClientRect(_hwnd, out var rect);
            _controller.Bounds = new Rectangle(0, 0, rect.right, rect.bottom);
            _controller.IsVisible = true;

            if (IsBorderless)
            {
                // We listen to window size events and keep a 4-6 pixel input passthrough zone on the borders
                _controller.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true;
            }

            _controller.CoreWebView2.Navigate(targetUrl);
        }
        catch (Exception ex)
        {
            PInvoke.MessageBox(_hwnd, $"WebView2 Exception: {ex.Message}", "Critical Failure",
                MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
            Environment.Exit(1);
        }
    }

    private async Task EvaluateClosingAsync()
    {
        if (ClosingAsync == null) return;

        try
        {
            // Execute the consumer's async check (e.g., showing a web dialog or checking unsaved state)
            bool shouldClose = await ClosingAsync();
            
            if (shouldClose)
            {
                _isClosingApproved = true;
                // Post the close message back to the queue to re-enter safely on the next loop cycle
                PInvoke.PostMessage(_hwnd, PInvoke.WM_CLOSE, 0, 0);
            }
        }
        catch (Exception)
        {
            // Fallback: If the hook crashes, you can choose to force close or swallow.
            // For safety, we force allow close here, but adapt as your error policy dictates:
            _isClosingApproved = true;
            PInvoke.PostMessage(_hwnd, PInvoke.WM_CLOSE, 0, 0);
        }
    }
}