using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Russkyc.Fene;

internal static class Native
{
    // On 64-bit Windows, the real entry point inside user32.dll is named SetClassLongPtrW
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", CharSet = CharSet.Unicode)]
    public static extern nint SetClassLongPtr64(HWND hWnd, GET_CLASS_LONG_INDEX nIndex, nint dwNewLong);
}