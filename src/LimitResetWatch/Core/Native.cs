using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace LimitResetWatch;

internal static partial class Native
{
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_EXITSIZEMOVE = 0x0232;
    public const int HTCAPTION = 2;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_BORDER_COLOR = 34;
    const int DWMWCP_ROUND = 2;
    const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Windows 11 の角丸と枠線の色を付ける。対応していない OS では false。</summary>
    public static bool TryRoundCorners(IntPtr hwnd, Color border)
    {
        var corner = DWMWCP_ROUND;
        if (DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int)) != 0) return false;
        var colorRef = border.R | (border.G << 8) | (border.B << 16);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr hMem);

    /// <summary>DPAPI（現在のユーザー）で暗号化されたデータを復号する。</summary>
    public static byte[] Unprotect(byte[] protectedData)
    {
        var handle = GCHandle.Alloc(protectedData, GCHandleType.Pinned);
        try
        {
            var input = new DataBlob { Size = protectedData.Length, Data = handle.AddrOfPinnedObject() };
            if (!CryptUnprotectData(ref input, 0, 0, 0, 0, CRYPTPROTECT_UI_FORBIDDEN, out var output))
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
