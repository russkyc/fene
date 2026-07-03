namespace Russkyc.Fene;

/// <summary>
/// Configures the behavior, security, and feature flags of the WebView2 instance.
/// </summary>
public sealed class WebViewSettingsOptions
{
    public bool EnableGpuAcceleration { get; set; } = true;
    public bool DisableWebSecurity { get; set; } = false;
    public string? AdditionalBrowserArguments { get; set; }
    public bool AreDevToolsEnabled { get; set; } = true;
    public bool IsScriptEnabled { get; set; } = true;
    public bool IsWebMessageEnabled { get; set; } = true;
    public bool IsZoomControlEnabled { get; set; } = true;
    public bool AreDefaultScriptDialogsEnabled { get; set; } = true;
    public bool IsBuiltInErrorPageEnabled { get; set; } = true;
    public bool IsPasswordAutosaveEnabled { get; set; } = false;
    public bool IsGeneralAutofillEnabled { get; set; } = false;
    public bool IsGestureAutoplayBlocked { get; set; } = false;
    public bool AreHostObjectsAllowed { get; set; } = true;
    public bool IsPinchZoomEnabled { get; set; } = true;
    
    // UI and Permission controls
    public bool IsStatusBarEnabled { get; set; } = false;
    public bool AutomaticallyAllowAllPermissions { get; set; } = true;
    
    // Desktop App Illusion Controls
    public bool IsSwipeNavigationEnabled { get; set; } = false;
    public bool AreDefaultContextMenusEnabled { get; set; } = false;
    public bool AreBrowserAcceleratorKeysEnabled { get; set; } = false;
    public bool PreventDragAndDropNavigation { get; set; } = true;
}