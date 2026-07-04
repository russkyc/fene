namespace Russkyc.Fene;

/// <summary>
/// Configures the behavior, security, and feature flags of the WebView2 instance.
/// </summary>
public sealed class WebViewSettingsOptions
{
    public bool EnableGpuAcceleration { get; set; } = true;
    public bool DisableWebSecurity { get; set; } = true;
    public string? AdditionalBrowserArguments { get; set; }
    public bool AreDevToolsEnabled { get; set; } = true;
    public bool IsScriptEnabled { get; set; } = true;
    public bool IsWebMessageEnabled { get; set; } = true;
    public bool IsZoomControlEnabled { get; set; } = true;
    public bool AreDefaultScriptDialogsEnabled { get; set; } = true;
    public bool IsBuiltInErrorPageEnabled { get; set; } = true;
    public bool IsPasswordAutosaveEnabled { get; set; } = true;
    public bool IsGeneralAutofillEnabled { get; set; } = true;
    public bool IsGestureAutoplayBlocked { get; set; } = true;
    public bool AreHostObjectsAllowed { get; set; } = true;
    public bool IsPinchZoomEnabled { get; set; } = true;
    
    // UI and Permission controls
    public bool IsStatusBarEnabled { get; set; } = false;
    public bool AutomaticallyAllowAllPermissions { get; set; } = true;
    
    // Desktop App Illusion Controls
    public bool IsSwipeNavigationEnabled { get; set; } = true;
    public bool AreDefaultContextMenusEnabled { get; set; } = true;
    public bool AreBrowserAcceleratorKeysEnabled { get; set; } = true;
    public bool PreventDragAndDropNavigation { get; set; } = true;
}