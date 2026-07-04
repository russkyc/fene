namespace Russkyc.Fene;

/// <summary>
/// Configures the behavior, security, and feature flags of the WebView2 instance.
/// </summary>
public sealed class WebViewSettingsOptions
{
    public bool EnableGpuAcceleration { get; set; } = true;
    public bool DisableWebSecurity { get; set; } = true;
    public string? AdditionalBrowserArguments { get; set; }
    public bool AreDevToolsEnabled { get; set; }
    public bool IsScriptEnabled { get; set; } = true;
    public bool IsWebMessageEnabled { get; set; } = true;
    public bool IsZoomControlEnabled { get; set; }
    public bool AreDefaultScriptDialogsEnabled { get; set; }
    public bool IsBuiltInErrorPageEnabled { get; set; } = true;
    public bool IsPasswordAutosaveEnabled { get; set; }
    public bool IsGeneralAutofillEnabled { get; set; }
    public bool IsGestureAutoplayBlocked { get; set; }
    public bool AreHostObjectsAllowed { get; set; } = true;
    public bool IsPinchZoomEnabled { get; set; }
    
    // UI and Permission controls
    public bool IsStatusBarEnabled { get; set; }
    public bool AutomaticallyAllowAllPermissions { get; set; } = true;
    
    // Desktop App Illusion Controls
    public bool IsSwipeNavigationEnabled { get; set; }
    public bool AreDefaultContextMenusEnabled { get; set; }
    public bool AreBrowserAcceleratorKeysEnabled { get; set; }
    public bool PreventDragAndDropNavigation { get; set; }
}