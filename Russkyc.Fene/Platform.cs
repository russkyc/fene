using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;
using Microsoft.Win32;

namespace Russkyc.Fene;

/// <summary>
/// Provides native OS utilities for desktop applications without relying on heavyweight UI frameworks.
/// </summary>
public static class Platform
{
    // Native COM Class IDs mapped cleanly for Activator instantiation
    private static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    private static readonly Guid ClsidFileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");

    /// <summary>
    /// Opens the native Windows file picker for a single file.
    /// </summary>
    public static unsafe string? ShowOpenFileDialog(string title = "Open File", string filter = "All Files|*.*", WebViewWindow? owner = null)
    {
        var dialogType = Type.GetTypeFromCLSID(ClsidFileOpenDialog);
        var dialog = (IFileOpenDialog)Activator.CreateInstance(dialogType!)!;

        fixed (char* pTitle = title) dialog.SetTitle(new PCWSTR(pTitle));
        ApplyFilter(dialog, filter);

        try
        {
            dialog.Show(new HWND(owner?.Handle ?? IntPtr.Zero));
            dialog.GetResult(out IShellItem item);
            return ExtractPath(item);
        }
        catch (COMException ex) when ((uint)ex.ErrorCode == 0x800704C7) // ERROR_CANCELLED
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the native Windows file picker configured for multiple file selection.
    /// </summary>
    public static unsafe string[] ShowOpenMultipleFilesDialog(string title = "Open Files", string filter = "All Files|*.*", WebViewWindow? owner = null)
    {
        var dialogType = Type.GetTypeFromCLSID(ClsidFileOpenDialog);
        var dialog = (IFileOpenDialog)Activator.CreateInstance(dialogType!)!;

        fixed (char* pTitle = title) dialog.SetTitle(new PCWSTR(pTitle));
        ApplyFilter(dialog, filter);

        dialog.GetOptions(out FILEOPENDIALOGOPTIONS options);
        dialog.SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_ALLOWMULTISELECT);

        try
        {
            dialog.Show(new HWND(owner?.Handle ?? IntPtr.Zero));
            dialog.GetResults(out IShellItemArray itemArray);

            itemArray.GetCount(out uint count);
            var results = new string[count];

            for (uint i = 0; i < count; i++)
            {
                itemArray.GetItemAt(i, out IShellItem item);
                results[i] = ExtractPath(item) ?? string.Empty;
            }

            return results;
        }
        catch (COMException ex) when ((uint)ex.ErrorCode == 0x800704C7)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Opens the native Windows file save dialog.
    /// </summary>
    public static unsafe string? ShowSaveFileDialog(string title = "Save File", string filter = "All Files|*.*", string defaultExtension = "", WebViewWindow? owner = null)
    {
        var dialogType = Type.GetTypeFromCLSID(ClsidFileSaveDialog);
        var dialog = (IFileSaveDialog)Activator.CreateInstance(dialogType!)!;

        fixed (char* pTitle = title) dialog.SetTitle(new PCWSTR(pTitle));
        if (!string.IsNullOrEmpty(defaultExtension))
        {
            fixed (char* pExt = defaultExtension) dialog.SetDefaultExtension(new PCWSTR(pExt));
        }

        ApplyFilter(dialog, filter);

        try
        {
            dialog.Show(new HWND(owner?.Handle ?? IntPtr.Zero));
            dialog.GetResult(out IShellItem item);
            return ExtractPath(item);
        }
        catch (COMException ex) when ((uint)ex.ErrorCode == 0x800704C7)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the native Windows folder picker dialog.
    /// </summary>
    public static unsafe string? ShowFolderBrowserDialog(string title = "Select Folder", WebViewWindow? owner = null)
    {
        var dialogType = Type.GetTypeFromCLSID(ClsidFileOpenDialog);
        var dialog = (IFileOpenDialog)Activator.CreateInstance(dialogType!)!;

        fixed (char* pTitle = title) dialog.SetTitle(new PCWSTR(pTitle));

        dialog.GetOptions(out FILEOPENDIALOGOPTIONS options);
        dialog.SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM);

        try
        {
            dialog.Show(new HWND(owner?.Handle ?? IntPtr.Zero));
            dialog.GetResult(out IShellItem item);
            return ExtractPath(item);
        }
        catch (COMException ex) when ((uint)ex.ErrorCode == 0x800704C7)
        {
            return null;
        }
    }

    /// <summary>
    /// Retrieves an exhaustive list of system-wide and user-installed font family names 
    /// directly from the Windows Registry, capturing fonts injected by modern font managers.
    /// </summary>
    public static IEnumerable<string> GetInstalledFonts()
    {
        // HashSet prevents duplicates if a font exists in both HKLM and HKCU
        var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ExtractFontsFromRegistryKey(RegistryKey rootKey, string path)
        {
            using var key = rootKey.OpenSubKey(path);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                // The registry stores them as "Arial (TrueType)", we just want "Arial"
                var cleanName = valueName;
                int parenIndex = cleanName.IndexOf(" (", StringComparison.Ordinal);
                if (parenIndex > 0)
                {
                    cleanName = cleanName.Substring(0, parenIndex);
                }

                fonts.Add(cleanName);
            }
        }

        // 1. Capture Machine-wide System Fonts (Admin installs)
        ExtractFontsFromRegistryKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");

        // 2. Capture Per-User Fonts (Font managers, Adobe Fonts, User installs)
        ExtractFontsFromRegistryKey(Registry.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\Fonts");

        return fonts.OrderBy(f => f);
    }

    /// <summary>
    /// Opens a URL in the user's default web browser, or opens a local file 
    /// in its associated default Windows application (e.g., opening a .pdf in Acrobat).
    /// </summary>
    public static void OpenInDefaultApp(string pathOrUrl)
    {
        try
        {
            // UseShellExecute = true forces Windows to evaluate the string and launch 
            // the associated default program for the protocol or file extension.
            Process.Start(new ProcessStartInfo
            {
                FileName = pathOrUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Silently ignore or handle cases where no default app is registered
        }
    }

    /// <summary>
    /// Queries the Windows Registry to determine if the user's OS is currently using Dark Mode.
    /// Useful for syncing Blazor themes with the native window frame.
    /// </summary>
    public static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int lightTheme)
            {
                return lightTheme == 0; // 0 means Dark Mode, 1 means Light Mode
            }
        }
        catch
        {
            // Ignored
        }

        return false; // Default to light theme if registry read fails
    }

    /// <summary>
    /// Triggers a native Win32 blocking message box. Useful for critical alerts that 
    /// need to look like native OS dialogs rather than HTML modals.
    /// </summary>
    public static unsafe void ShowMessageBox(string message, string title = "Information", bool isError = false, WebViewWindow? owner = null)
    {
        var style = isError ? MESSAGEBOX_STYLE.MB_ICONERROR : MESSAGEBOX_STYLE.MB_ICONINFORMATION;

        fixed (char* pMsg = message)
        fixed (char* pTitle = title)
        {
            PInvoke.MessageBox(
                new HWND(owner?.Handle ?? IntPtr.Zero),
                new PCWSTR(pMsg),
                new PCWSTR(pTitle),
                style | MESSAGEBOX_STYLE.MB_OK
            );
        }
    }

    private static unsafe void ApplyFilter(IFileDialog dialog, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return;

        var parts = filter.Split('|');
        if (parts.Length % 2 != 0) return;

        var specs = new COMDLG_FILTERSPEC[parts.Length / 2];
        var handles = new List<GCHandle>();

        try
        {
            for (int i = 0; i < parts.Length; i += 2)
            {
                // Pin strings in memory so the garbage collector doesn't move them 
                // while the native COM dialog is reading the pointers.
                var nameHandle = GCHandle.Alloc(parts[i], GCHandleType.Pinned);
                var specHandle = GCHandle.Alloc(parts[i + 1], GCHandleType.Pinned);

                handles.Add(nameHandle);
                handles.Add(specHandle);

                specs[i / 2] = new COMDLG_FILTERSPEC
                {
                    pszName = new PCWSTR((char*)nameHandle.AddrOfPinnedObject()),
                    pszSpec = new PCWSTR((char*)specHandle.AddrOfPinnedObject())
                };
            }

            fixed (COMDLG_FILTERSPEC* pSpecs = specs)
            {
                dialog.SetFileTypes((uint)specs.Length, pSpecs);
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                handle.Free();
            }
        }
    }

    private static unsafe string? ExtractPath(IShellItem item)
    {
        item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out PWSTR pathPtr);
        if (pathPtr.Value == null) return null;

        string path = new string(pathPtr.Value);

        // Free the native memory allocated by the shell
        PInvoke.CoTaskMemFree(pathPtr.Value);

        return path;
    }
}