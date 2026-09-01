using System.Runtime.InteropServices;

namespace NexMote.Agent.Tray;

/// <summary>
/// Uzaktan gelen fare ve klavye girdilerini önce SYSTEM yetkili Girdi Yardımcısına (Named Pipe) ileten,
/// yardımcının ulaşılamadığı durumlarda standart Win32 SendInput API'sine geri düşen (fallback) girdi enjektörü.
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

    public static void MoveMouse(int displayIndex, int x, int y)
    {
        DesktopHelper.AttachToActiveDesktop();

        var displayBounds = ScreenCapture.GetDisplayBoundsPublic(displayIndex);
        var globalX = displayBounds.Left + x;
        var globalY = displayBounds.Top + y;

        var virtualBounds = SystemInformation.VirtualScreen;
        var clampedX = Math.Clamp(globalX, virtualBounds.Left, virtualBounds.Right - 1);
        var clampedY = Math.Clamp(globalY, virtualBounds.Top, virtualBounds.Bottom - 1);

        SetCursorPos(clampedX, clampedY);

        var normalizedX = (int)Math.Round((double)(clampedX - virtualBounds.Left) * 65535 / Math.Max(1, virtualBounds.Width - 1));
        var normalizedY = (int)Math.Round((double)(clampedY - virtualBounds.Top) * 65535 / Math.Max(1, virtualBounds.Height - 1));

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
        DesktopHelper.AttachToActiveDesktop();

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
        DesktopHelper.AttachToActiveDesktop();

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
        DesktopHelper.AttachToActiveDesktop();

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
            try
            {
                keybd_event((byte)keyCode, (byte)scanCode, flags, UIntPtr.Zero);
            }
            catch { }
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
            try
            {
                mouse_event(flags, 0, 0, mouseData, UIntPtr.Zero);
            }
            catch { }
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
