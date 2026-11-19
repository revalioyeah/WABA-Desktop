using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WABA.Domain.Models;

namespace WABA.Infrastructure.Windows;

public sealed class WindowCatalog
{
    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    public IReadOnlyList<GameWindowInfo> GetVisibleWindows()
    {
        var windows = new List<GameWindowInfo>();
        var ownProcessId = Environment.ProcessId;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var titleLength = GetWindowTextLength(hWnd);
            if (titleLength <= 0)
                return true;

            var titleBuffer = new StringBuilder(titleLength + 1);
            GetWindowText(hWnd, titleBuffer, titleBuffer.Capacity);
            var title = titleBuffer.ToString().Trim();

            if (string.IsNullOrWhiteSpace(title))
                return true;

            GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == ownProcessId)
                return true;

            if (!GetClientRect(hWnd, out var clientRect))
                return true;

            var width = clientRect.Right - clientRect.Left;
            var height = clientRect.Bottom - clientRect.Top;

            // Ignore tooltips and tiny utility windows.
            if (width < 320 || height < 240)
                return true;

            var clientOrigin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(hWnd, ref clientOrigin))
                return true;

            string processName = "Unknown";
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                // The window may close while enumeration is running.
            }

            windows.Add(new GameWindowInfo(
                hWnd,
                title,
                processName,
                width,
                height,
                clientOrigin.X,
                clientOrigin.Y));

            return true;
        }, nint.Zero);

        return windows
            .OrderByDescending(w =>
                w.Title.Contains("Warung", StringComparison.OrdinalIgnoreCase) ||
                w.Title.Contains("Google Play", StringComparison.OrdinalIgnoreCase))
            .ThenBy(w => w.Title)
            .ToList();
    }

    internal static bool TryGetClientBounds(
        nint hWnd,
        out int screenLeft,
        out int screenTop,
        out int width,
        out int height)
    {
        screenLeft = screenTop = width = height = 0;

        if (!IsWindow(hWnd) || IsIconic(hWnd))
            return false;

        if (!GetClientRect(hWnd, out var rect))
            return false;

        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;

        var origin = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref origin))
            return false;

        screenLeft = origin.X;
        screenTop = origin.Y;
        return width > 0 && height > 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hWnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
}
