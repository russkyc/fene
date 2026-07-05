using System.Drawing;

namespace Russkyc.Fene;

public sealed class WindowBuilder
{
    private readonly string _title;
    private int? _width;
    private int? _height;
    
    private int? _minWidth;
    private int? _minHeight;
    private int? _x;
    private int? _y;
    private WindowStartPosition _startPosition = WindowStartPosition.OsDefault;
    private Color _backgroundColor = Color.Transparent;
    private bool _enableDarkMode;
    private string? _iconPath;
    private string? _userDataFolder;
    private bool _showOnlyAfterLoad = true;
    private bool _isBorderless;
    private string? _userAgentOverride;
    private WindowState _windowState = WindowState.Normal;
    private bool _isTopMost;

    private readonly List<(string HostName, string FolderPath, HostResourceAccessKind AccessKind)> _virtualHosts = new();
    private readonly WebViewSettingsOptions _configureOptions = new();

    private WindowBuilder(string title, int? width, int? height)
    {
        _title = title;
        _width = width;
        _height = height;
    }

    public static WindowBuilder Create(string title = "WebView Window", int? width = null, int? height = null)
    {
        return new WindowBuilder(title, width, height);
    }
    
    public Window BuildKiosk()
    {
        _isBorderless = true;
        _isTopMost = true;
        _showOnlyAfterLoad = true;
        _windowState = WindowState.Maximized;
        return Build();
    }

    public WindowBuilder WithSize(int width, int height)
    {
        _width = width;
        _height = height;
        return this;
    }

    public WindowBuilder WithMinSize(int minWidth, int minHeight)
    {
        _minWidth = minWidth;
        _minHeight = minHeight;
        return this;
    }

    public WindowBuilder WithPosition(int x, int y)
    {
        _startPosition = WindowStartPosition.OsDefault; // Manual overrides bypass generic presets
        _x = x;
        _y = y;
        return this;
    }

    public WindowBuilder WithStartPosition(WindowStartPosition startPosition)
    {
        _startPosition = startPosition;
        return this;
    }

    public WindowBuilder WithBackgroundColor(Color color)
    {
        _backgroundColor = color;
        return this;
    }

    public WindowBuilder UseDarkMode(bool enabled = true)
    {
        _enableDarkMode = enabled;
        return this;
    }

    public WindowBuilder WithIcon(string path)
    {
        _iconPath = path;
        return this;
    }

    public WindowBuilder WithUserDataFolder(string path)
    {
        _userDataFolder = path;
        return this;
    }

    public WindowBuilder ShowOnlyAfterLoad(bool enabled = true)
    {
        _showOnlyAfterLoad = enabled;
        return this;
    }

    public WindowBuilder MakeBorderless(bool enabled = true)
    {
        _isBorderless = enabled;
        return this;
    }

    public WindowBuilder WithUserAgent(string userAgent)
    {
        _userAgentOverride = userAgent;
        return this;
    }

    public WindowBuilder WithInitialState(WindowState state)
    {
        _windowState = state;
        return this;
    }

    public WindowBuilder MakeTopMost(bool enabled = true)
    {
        _isTopMost = enabled;
        return this;
    }

    public WindowBuilder MapVirtualHost(string hostName, string folderPath, HostResourceAccessKind accessKind = HostResourceAccessKind.Allow)
    {
        _virtualHosts.Add((hostName, folderPath, accessKind));
        return this;
    }

    public WindowBuilder ConfigureSettings(Action<WebViewSettingsOptions> action)
    {
        action(_configureOptions);
        return this;
    }

    public Window Build()
    {
        var window = new Window(_title, _width, _height, _minWidth, _minHeight)
        {
            X = _x,
            Y = _y,
            StartPosition = _startPosition,
            BackgroundColor = _backgroundColor,
            EnableDarkMode = _enableDarkMode,
            IconPath = _iconPath,
            UserDataFolder = _userDataFolder,
            ShowOnlyAfterLoad = _showOnlyAfterLoad,
            IsBorderless = _isBorderless,
            UserAgentOverride = _userAgentOverride,
            WindowState = _windowState,
            IsTopMost = _isTopMost
        };

        var o = _configureOptions;
        var t = window.Options;
        
        t.EnableGpuAcceleration = o.EnableGpuAcceleration;
        t.DisableWebSecurity = o.DisableWebSecurity;
        t.AdditionalBrowserArguments = o.AdditionalBrowserArguments;
        t.AreDevToolsEnabled = o.AreDevToolsEnabled;
        t.IsScriptEnabled = o.IsScriptEnabled;
        t.IsWebMessageEnabled = o.IsWebMessageEnabled;
        t.IsZoomControlEnabled = o.IsZoomControlEnabled;
        t.AreDefaultScriptDialogsEnabled = o.AreDefaultScriptDialogsEnabled;
        t.IsBuiltInErrorPageEnabled = o.IsBuiltInErrorPageEnabled;
        t.IsPasswordAutosaveEnabled = o.IsPasswordAutosaveEnabled;
        t.IsGeneralAutofillEnabled = o.IsGeneralAutofillEnabled;
        t.IsGestureAutoplayBlocked = o.IsGestureAutoplayBlocked;
        t.AreHostObjectsAllowed = o.AreHostObjectsAllowed;
        t.IsPinchZoomEnabled = o.IsPinchZoomEnabled;
        t.IsStatusBarEnabled = o.IsStatusBarEnabled;
        t.AutomaticallyAllowAllPermissions = o.AutomaticallyAllowAllPermissions;
        t.IsSwipeNavigationEnabled = o.IsSwipeNavigationEnabled;
        t.AreDefaultContextMenusEnabled = o.AreDefaultContextMenusEnabled;
        t.AreBrowserAcceleratorKeysEnabled = o.AreBrowserAcceleratorKeysEnabled;
        t.PreventDragAndDropNavigation = o.PreventDragAndDropNavigation;

        foreach (var host in _virtualHosts)
        {
            window.MapVirtualHost(host.HostName, host.FolderPath, host.AccessKind);
        }

        return window;
    }
}