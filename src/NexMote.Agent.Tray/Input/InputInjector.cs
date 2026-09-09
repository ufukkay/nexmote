using System.Drawing;
using System.Runtime.InteropServices;

namespace NexMote.Agent.Tray;

/// <summary>
/// Uzaktan gelen fare ve klavye girdilerini önce SYSTEM yetkili Girdi Yardımcısına (Named Pipe) ileten,
/// yardımcının ulaşılamadığı durumlarda standart Win32 SendInput API'sine geri düşen (fallback) girdi enjektörü.
/// Kilit ekranı (Winlogon) ve UAC pencerelerinde de sorunsuz enjeksiyon sağlar.
/// </summary>
internal static class InputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheelFlag = 0x0800;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint KeyboardKeyUp = 0x0002;
    private const uint KeyboardExtendedKey = 0x0001;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static Rectangle GetVirtualScreenBounds(Rectangle displayBounds)
    {
        var vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (vWidth <= 0 || vHeight <= 0)
        {
            return displayBounds.Width > 0 ? displayBounds : new Rectangle(0, 0, 1920, 1080);
        }

        return new Rectangle(vLeft, vTop, vWidth, vHeight);
    }

    public static void MoveMouse(int displayIndex, int x, int y)
    {
        DesktopHelper.AttachToActiveDesktop(force: false);

        var displayBounds = ScreenCapture.GetDisplayBoundsPublic(displayIndex);
        var globalX = displayBounds.Left + x;
        var globalY = displayBounds.Top + y;

        var virtualBounds = GetVirtualScreenBounds(displayBounds);
        var clampedX = Math.Clamp(globalX, virtualBounds.Left, virtualBounds.Right - 1);
        var clampedY = Math.Clamp(globalY, virtualBounds.Top, virtualBounds.Bottom - 1);

        if (SetCursorPos(clampedX, clampedY))
        {
            // SetCursorPos doğrudan başarılı olduysa çakışan ve titremeye yol açan mükerrer SendInput çağrısı yapma!
            return;
        }

        // SetCursorPos başarısız olduysa (UAC veya masaüstü geçişi olabilir) masaüstünü zorla yenile ve tekrar dene
        DesktopHelper.AttachToActiveDesktop(force: true);
        if (SetCursorPos(clampedX, clampedY))
        {
            return;
        }

        // SetCursorPos hala başarısızsa standart Win32 sanal masaüstü normalizasyon formülü ile SendInput'a düş
        var vWidth = Math.Max(1, virtualBounds.Width);
        var vHeight = Math.Max(1, virtualBounds.Height);
        var normalizedX = Math.Clamp((int)Math.Round((double)(clampedX - virtualBounds.Left) * 65536.0 / vWidth), 0, 65535);
        var normalizedY = Math.Clamp((int)Math.Round((double)(clampedY - virtualBounds.Top) * 65536.0 / vHeight), 0, 65535);

        var input = new INPUT
        {
            Type = InputMouse,
            Data = new INPUTUNION
            {
                Mouse = new MOUSEINPUT
                {
                    Dx = normalizedX,
                    Dy = normalizedY,
                    Flags = MouseMove | MouseAbsolute | MouseVirtualDesk,
                    MouseData = 0
                }
            }
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
        {
            try
            {
                mouse_event(MouseMove | MouseAbsolute | MouseVirtualDesk, normalizedX, normalizedY, 0, UIntPtr.Zero);
            }
            catch { }
        }
    }

    public static void MouseButton(string? button, bool isDown)
    {
        DesktopHelper.AttachToActiveDesktop(force: false);

        var flags = (button?.ToLowerInvariant(), isDown) switch
        {
            ("left", true) => MouseLeftDown,
            ("left", false) => MouseLeftUp,
            ("right", true) => MouseRightDown,
            ("right", false) => MouseRightUp,
            ("middle", true) => MouseMiddleDown,
            ("middle", false) => MouseMiddleUp,
            _ => 0u
        };

        if (flags != 0)
        {
            SendMouse(flags, 0);
        }
    }

    public static void MouseWheel(int delta)
    {
        DesktopHelper.AttachToActiveDesktop(force: false);

        if (delta != 0)
        {
            SendMouse(MouseWheelFlag, delta);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static void Keyboard(int keyCode, bool isDown)
    {
        DesktopHelper.AttachToActiveDesktop(force: false);

        if (keyCode is <= 0 or > ushort.MaxValue)
        {
            return;
        }

        var isExtended = IsExtendedKey(keyCode);
        var scanCode = (ushort)MapVirtualKey((uint)keyCode, 0);
        var flags = (isDown ? 0u : KeyboardKeyUp) | (isExtended ? KeyboardExtendedKey : 0u);

        var input = new INPUT
        {
            Type = InputKeyboard,
            Data = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = (ushort)keyCode,
                    ScanCode = scanCode,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
        {
            DesktopHelper.AttachToActiveDesktop(force: true);
            if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
            {
                try
                {
                    keybd_event((byte)keyCode, (byte)scanCode, flags, UIntPtr.Zero);
                }
                catch { }
            }
        }
    }

    public static void ReleaseAllModifiers()
    {
        int[] modifierKeys = [160, 161, 162, 163, 164, 165, 91, 92]; // L/R Shift, Ctrl, Alt, Win
        foreach (var vk in modifierKeys)
        {
            Keyboard(vk, false);
        }
    }

    private static bool IsExtendedKey(int keyCode)
    {
        return keyCode is 37 or 38 or 39 or 40 or 33 or 34 or 35 or 36 or 44 or 45 or 46 or 91 or 92 or 93 or 111 or 144 or 163 or 165;
    }

    private static void SendMouse(uint flags, int mouseData)
    {
        var input = new INPUT
        {
            Type = InputMouse,
            Data = new INPUTUNION
            {
                Mouse = new MOUSEINPUT
                {
                    Flags = flags,
                    MouseData = mouseData
                }
            }
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
        {
            DesktopHelper.AttachToActiveDesktop(force: true);
            if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
            {
                try
                {
                    mouse_event(flags, 0, 0, mouseData, UIntPtr.Zero);
                }
                catch { }
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

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

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
