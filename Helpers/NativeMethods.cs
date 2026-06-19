using System.Runtime.InteropServices;

namespace AutoClicker.Helpers;

/// <summary>
/// WinAPI импорты: симуляция ввода, глобальные хуки, PostMessage для Roblox.
/// </summary>
internal static class NativeMethods
{
    // ===================== Типы входных событий =====================
    internal const uint INPUT_MOUSE = 0;
    internal const uint INPUT_KEYBOARD = 1;

    // ===================== Флаги мыши =====================
    internal const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP     = 0x0004;
    internal const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    internal const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    internal const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    internal const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;

    // ===================== Флаги клавиатуры =====================
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    // ===================== Хук клавиатуры =====================
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;

    // ===================== Window Messages для PostMessage =====================
    internal const int WM_CHAR = 0x0102;
    // WM_KEYDOWN и WM_KEYUP уже определены выше

    // ===================== Структуры =====================

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        internal uint Type;
        internal INPUTUNION Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] internal MOUSEINPUT Mi;
        [FieldOffset(0)] internal KEYBDINPUT Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        internal int Dx, Dy, MouseData;
        internal uint DwFlags, Time;
        internal IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        internal ushort Vk, Scan;
        internal uint Flags, Time;
        internal IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        internal uint VkCode, ScanCode, Flags, Time;
        internal IntPtr DwExtraInfo;
    }

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ===================== user32.dll =====================

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    internal static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);

    // ===================== PostMessage / FindWindow (для Roblox) =====================

    /// <summary>
    /// Отправляет сообщение окну напрямую. Работает даже когда приложение не в фокусе.
    /// Roblox обрабатывает WM_KEYDOWN/WM_KEYUP/WM_CHAR.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Ищет окно по заголовку.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    /// <summary>
    /// Ищет окно по PID процесса.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// Получает PID окна.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>Проверяет, является ли handle действительным окном.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    // ===================== Вспомогательные методы =====================

    internal static uint KeyToVirtualKey(System.Windows.Input.Key key) => (uint)(int)key;
    internal static ushort GetScanCode(uint vk) => (ushort)MapVirtualKey(vk, 0);

    /// <summary>
    /// Создаёт lParam для WM_KEYDOWN/WM_KEYUP.
    /// scanCode — скан-код клавиши, extended — расширенная клавиша (стрелки, Home и т.д.)
    /// </summary>
    internal static IntPtr MakeKeyLParam(uint scanCode, bool keyUp, bool extended = false)
    {
        uint lParam = 1; // repeat count = 1
        lParam |= scanCode << 16; // scan code
        if (extended) lParam |= 1u << 24; // extended key flag
        if (keyUp) lParam |= 1u << 31; // transition state (key up)
        lParam |= 1u << 30; // context code (0 = pressed)
        return (IntPtr)lParam;
    }
}
