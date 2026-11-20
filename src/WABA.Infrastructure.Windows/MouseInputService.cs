using System.Runtime.InteropServices;
using WABA.Domain.Models;

namespace WABA.Infrastructure.Windows;

public sealed record MouseClickResult(
    bool Success,
    int ClientX,
    int ClientY,
    int ScreenX,
    int ScreenY,
    string Message);

/// <summary>
/// Sends real left clicks to client-relative points in the selected game window.
/// The fast path focuses the game once, then sends a sequence of validated clicks.
/// </summary>
public sealed class MouseInputService
{
    private const uint InputMouse = 0;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    public MouseClickResult ClickClientPoint(GameWindowInfo target, int clientX, int clientY)
    {
        var focus = FocusTarget(target, 120);
        if (!focus.Success)
            return focus;

        return ClickClientPointFast(target, clientX, clientY);
    }

    /// <summary>
    /// Brings the target window to the foreground once before a batch.
    /// </summary>
    public MouseClickResult FocusTarget(GameWindowInfo target, int settleDelayMs = 120)
    {
        if (!WindowCatalog.TryGetClientBounds(
                target.Handle, out var left, out var top, out var width, out var height))
        {
            return Failed("Jendela target tidak aktif atau sedang diminimalkan.");
        }

        SetForegroundWindow(target.Handle);
        if (settleDelayMs > 0)
            Thread.Sleep(Math.Clamp(settleDelayMs, 0, 1000));

        if (GetForegroundWindow() != target.Handle)
            return Failed("Jendela game belum menjadi foreground. Tidak ada klik yang dikirim.");

        return new MouseClickResult(
            true, 0, 0, left, top,
            $"Fokus diarahkan ke jendela game ({width}×{height}).");
    }

    /// <summary>
    /// Sends one click without refocusing or adding a per-click focus delay.
    /// Intended for a reviewed batch after FocusTarget has succeeded.
    /// </summary>
    public MouseClickResult ClickClientPointFast(GameWindowInfo target, int clientX, int clientY)
    {
        if (!WindowCatalog.TryGetClientBounds(
                target.Handle, out var left, out var top, out var width, out var height))
        {
            return Failed("Jendela target tidak aktif atau sedang diminimalkan.");
        }

        if (clientX < 0 || clientY < 0 || clientX >= width || clientY >= height)
            return Failed($"Koordinat di luar area game ({width}×{height}).");

        if (GetForegroundWindow() != target.Handle)
            return Failed("Fokus jendela game berubah. Antrean dihentikan sebelum klik berikutnya.");

        var screenX = left + clientX;
        var screenY = top + clientY;

        if (!SetCursorPos(screenX, screenY))
            return Failed("Windows gagal memindahkan pointer ke koordinat target.");

        var inputs = new[]
        {
            new INPUT
            {
                Type = InputMouse,
                Data = new INPUTUNION
                {
                    Mouse = new MOUSEINPUT { Flags = MouseEventLeftDown }
                }
            },
            new INPUT
            {
                Type = InputMouse,
                Data = new INPUTUNION
                {
                    Mouse = new MOUSEINPUT { Flags = MouseEventLeftUp }
                }
            }
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
        {
            return new MouseClickResult(
                false, clientX, clientY, screenX, screenY,
                "Input tidak terkirim lengkap. Periksa hak akses/UIPI dan fokus jendela game.");
        }

        return new MouseClickResult(
            true, clientX, clientY, screenX, screenY,
            $"Klik terkirim ke client ({clientX}, {clientY}) / screen ({screenX}, {screenY}).");
    }

    private static MouseClickResult Failed(string message)
        => new(false, 0, 0, 0, 0, message);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
