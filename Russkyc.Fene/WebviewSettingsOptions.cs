
namespace Russkyc.Fene;

public sealed class WebViewSettingsOptions
{
    // --- Environment / Command-line Core Settings ---
    public bool EnableGpuAcceleration { get; set; } = true;
    public bool DisableWebSecurity { get; set; } = false;
    public string? AdditionalBrowserArguments { get; set; } = null;

    // --- Standard Web Capabilities ---
    public bool AreDevToolsEnabled { get; set; } = true;
    public bool IsScriptEnabled { get; set; } = true;
    public bool IsWebMessageEnabled { get; set; } = true;
    public bool IsZoomControlEnabled { get; set; } = true;
    public bool AreDefaultScriptDialogsEnabled { get; set; } = true;
    public bool IsBuiltInErrorPageEnabled { get; set; } = true;
    public bool IsPasswordAutosaveEnabled { get; set; } = false;
    public bool IsGeneralAutofillEnabled { get; set; } = false;

    // --- Gesture & Media Settings ---
    public bool IsGestureAutoplayBlocked { get; set; } = false;

    // --- Security & Framework Restrictions ---
    public bool AreHostObjectsAllowed { get; set; } = true;
    public bool IsPinchZoomEnabled { get; set; } = true;
    public bool IsSwipeNavigationEnabled { get; set; } = true;
}