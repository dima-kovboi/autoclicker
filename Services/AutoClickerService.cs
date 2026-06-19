using System.Diagnostics;
using System.Runtime.InteropServices;
using AutoClicker.Helpers;

namespace AutoClicker.Services;

public enum ClickerMode { Toggle, Hold }
public enum InputType { Mouse, Keyboard }
public enum MouseButton { Left, Right, Middle }
public enum ClickType { Single, Double }

public class ClickerSettings
{
    public int IntervalMs { get; set; } = 100;
    public InputType InputDevice { get; set; } = InputType.Mouse;
    public MouseButton Button { get; set; } = MouseButton.Left;
    public ushort KeyboardVk { get; set; } = 0x41;
    public ClickType ClickType { get; set; } = ClickType.Single;
    public ClickerMode Mode { get; set; } = ClickerMode.Toggle;
    public int ClickLimit { get; set; } = 0;
    public bool RandomInterval { get; set; }
    public int RandomPercent { get; set; } = 10;
    public string ComboKeys { get; set; } = "";
    public string WindowTitle { get; set; } = "";
}

public class MacroStep
{
    public string Action { get; set; } = "";
    public int Value { get; set; }
    public int DelayMs { get; set; }
}

/// <summary>
/// Сервис автокликера.
/// Мышь — mouse_event. Клавиатура — SendInput (надёжно для всех приложений).
/// </summary>
public class AutoClickerService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _clickTask;
    private bool _isRunning;
    private readonly Random _random = new();

    public ClickerSettings Settings { get; set; } = new();
    public event Action<bool>? RunningStateChanged;
    public event Action<int>? ClicksPerformed;

    private int _clickCount;
    public int ClickCount => _clickCount;
    public bool IsRunning => _isRunning;

    public List<MacroStep> Macros { get; set; } = new();
    public bool IsRecording { get; private set; }
    private CancellationTokenSource? _recordCts;
    private Task? _recordTask;

    public void Start()
    {
        if (_isRunning) return;
        _cts = new CancellationTokenSource();
        _clickCount = 0;
        _isRunning = true;
        _clickTask = Task.Run(() => ClickLoop(_cts.Token), _cts.Token);
        RunningStateChanged?.Invoke(true);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _cts?.Cancel();
        try { _clickTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _isRunning = false;
        RunningStateChanged?.Invoke(false);
    }

    private void ClickLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (Settings.ClickLimit > 0 && _clickCount >= Settings.ClickLimit)
            {
                Stop();
                break;
            }

            if (Settings.InputDevice == InputType.Mouse)
                PerformMouseClick(Settings.Button, Settings.ClickType);
            else if (!string.IsNullOrWhiteSpace(Settings.ComboKeys))
                PerformComboKeys(Settings.ComboKeys);
            else
                PerformKeyPress(Settings.KeyboardVk, Settings.ClickType);

            _clickCount++;
            ClicksPerformed?.Invoke(_clickCount);

            int delay = Settings.IntervalMs;
            if (Settings.RandomInterval && Settings.RandomPercent > 0)
            {
                int variation = Settings.IntervalMs * Settings.RandomPercent / 100;
                delay = Settings.IntervalMs + _random.Next(-variation, variation + 1);
                if (delay < 1) delay = 1;
            }

            try { token.WaitHandle.WaitOne(delay); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    // ===================== Combo Keys =====================

    public void PerformComboKeys(string comboKeys)
    {
        if (string.IsNullOrWhiteSpace(comboKeys)) return;

        var vkCodes = comboKeys.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var keys = new List<(ushort vk, ushort scan)>();

        foreach (var part in vkCodes)
        {
            if (int.TryParse(part.Trim(), out int vk))
            {
                ushort scan = (ushort)NativeMethods.MapVirtualKey((uint)vk, 0);
                keys.Add(((ushort)vk, scan));
            }
        }

        foreach (var (vk, scan) in keys)
            SendKeyInput(vk, scan, false);

        for (int i = keys.Count - 1; i >= 0; i--)
            SendKeyInput(keys[i].vk, keys[i].scan, true);
    }

    // ===================== Window Targeting =====================

    public IntPtr FindWindowByTitle(string title)
    {
        return NativeMethods.FindWindow(null, title);
    }

    public void PostKeyPressToWindow(IntPtr hWnd, ushort vk)
    {
        ushort scanCode = (ushort)NativeMethods.MapVirtualKey(vk, 0);
        IntPtr downLParam = NativeMethods.MakeKeyLParam(scanCode, false);
        IntPtr upLParam = NativeMethods.MakeKeyLParam(scanCode, true);

        NativeMethods.PostMessage(hWnd, NativeMethods.WM_KEYDOWN, (IntPtr)vk, downLParam);
        NativeMethods.PostMessage(hWnd, NativeMethods.WM_KEYUP, (IntPtr)vk, upLParam);
    }

    public void PostMouseClickToWindow(IntPtr hWnd)
    {
        NativeMethods.PostMessage(hWnd, NativeMethods.WM_KEYDOWN, (IntPtr)0x01, IntPtr.Zero);
        NativeMethods.PostMessage(hWnd, NativeMethods.WM_KEYUP, (IntPtr)0x01, IntPtr.Zero);
    }

    // ===================== Macro Recording =====================

    public void RecordMacro()
    {
        if (IsRecording) return;
        Macros.Clear();
        IsRecording = true;
        _recordCts = new CancellationTokenSource();
        _recordTask = Task.Run(() => RecordLoop(_recordCts.Token), _recordCts.Token);
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        _recordCts?.Cancel();
        try { _recordTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        IsRecording = false;
    }

    private void RecordLoop(CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        long lastTimestamp = 0;

        while (!token.IsCancellationRequested)
        {
            Thread.Sleep(10);

            bool leftDown = NativeMethods.GetAsyncKeyState(0x01) < 0;
            bool rightDown = NativeMethods.GetAsyncKeyState(0x02) < 0;

            long now = stopwatch.ElapsedMilliseconds;
            int delay = (int)(now - lastTimestamp);

            if (leftDown)
            {
                Macros.Add(new MacroStep { Action = "mouse", Value = 0, DelayMs = delay });
                lastTimestamp = now;
            }
            else if (rightDown)
            {
                Macros.Add(new MacroStep { Action = "mouse", Value = 2, DelayMs = delay });
                lastTimestamp = now;
            }

            for (int vk = 0x08; vk <= 0xFE; vk++)
            {
                if (vk == 0x01 || vk == 0x02) continue;
                if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    Macros.Add(new MacroStep { Action = "key", Value = vk, DelayMs = delay });
                    lastTimestamp = now;
                    break;
                }
            }
        }
    }

    // ===================== Macro Playback =====================

    public void PlayMacro()
    {
        foreach (var step in Macros)
        {
            if (step.DelayMs > 0)
                Thread.Sleep(step.DelayMs);

            switch (step.Action)
            {
                case "key":
                    PerformKeyPress((ushort)step.Value, ClickType.Single);
                    break;
                case "mouse":
                    var btn = step.Value switch
                    {
                        0 => MouseButton.Left,
                        2 => MouseButton.Right,
                        _ => MouseButton.Middle
                    };
                    PerformMouseClick(btn, ClickType.Single);
                    break;
                case "delay":
                    if (step.DelayMs <= 0 && step.Value > 0)
                        Thread.Sleep(step.Value);
                    break;
            }
        }
    }

    // ===================== Мышь =====================

    private static void PerformMouseClick(MouseButton button, ClickType clickType)
    {
        uint downFlag, upFlag;
        switch (button)
        {
            case MouseButton.Left:
                downFlag = NativeMethods.MOUSEEVENTF_LEFTDOWN;
                upFlag = NativeMethods.MOUSEEVENTF_LEFTUP;
                break;
            case MouseButton.Right:
                downFlag = NativeMethods.MOUSEEVENTF_RIGHTDOWN;
                upFlag = NativeMethods.MOUSEEVENTF_RIGHTUP;
                break;
            case MouseButton.Middle:
                downFlag = NativeMethods.MOUSEEVENTF_MIDDLEDOWN;
                upFlag = NativeMethods.MOUSEEVENTF_MIDDLEUP;
                break;
            default:
                return;
        }

        NativeMethods.mouse_event(downFlag, 0, 0, 0, IntPtr.Zero);
        NativeMethods.mouse_event(upFlag, 0, 0, 0, IntPtr.Zero);

        if (clickType == ClickType.Double)
        {
            Thread.Sleep(30);
            NativeMethods.mouse_event(downFlag, 0, 0, 0, IntPtr.Zero);
            NativeMethods.mouse_event(upFlag, 0, 0, 0, IntPtr.Zero);
        }
    }

    // ===================== Клавиатура через SendInput =====================

    /// <summary>
    /// SendInput — стандартный способ симуляции клавиатуры.
    /// Работает в большинстве приложений. Учитывает текущую раскладку клавиатуры.
    /// </summary>
    private static void PerformKeyPress(ushort vk, ClickType clickType)
    {
        // Получаем скан-код по VK-коду
        ushort scanCode = (ushort)NativeMethods.MapVirtualKey(vk, 0);

        // Нажатие клавиши
        SendKeyInput(vk, scanCode, false);

        if (clickType == ClickType.Double)
        {
            Thread.Sleep(30);
            SendKeyInput(vk, scanCode, true);  // отпускание
            Thread.Sleep(30);
            SendKeyInput(vk, scanCode, false); // второе нажатие
            Thread.Sleep(30);
        }

        // Отпускание клавиши
        SendKeyInput(vk, scanCode, true);
    }

    private static void SendKeyInput(ushort vk, ushort scanCode, bool keyUp)
    {
        var input = new NativeMethods.INPUT
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Union = new NativeMethods.INPUTUNION
            {
                Ki = new NativeMethods.KEYBDINPUT
                {
                    Vk = vk,
                    Scan = scanCode,
                    Flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };

        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
