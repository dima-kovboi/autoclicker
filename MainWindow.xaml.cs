using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AutoClicker.Helpers;
using AutoClicker.Services;
using WinForms = System.Windows.Forms;

namespace AutoClicker;

public class WindowInfo
{
    public string Title { get; set; } = "";
    public IntPtr Handle { get; set; }
    public override string ToString() => Title;
}

public partial class MainWindow : Window
{
    private readonly AutoClickerService _clicker = new();
    private AppSettings _settings = new();

    private Key _hotkeyStartStop = Key.F6;
    private Key _hotkeyReset = Key.None;

    private bool _isWaitingForStartStopKey;
    private bool _isWaitingForResetKey;

    private readonly Stopwatch _cpsStopwatch = new();
    private readonly DispatcherTimer _cpsTimer;

    private bool _minimizeToTrayEnabled;
    private bool _useMilliseconds = true;
    private bool _animationsEnabled = true;
    private bool _soundEnabled = true;

    private int _clickLimit;
    private bool _randomInterval;
    private int _randomPercent = 10;
    private string _windowTitle = "";
    private bool _realAutostart;
    private bool _isWaitingForComboKeys;
    private readonly List<int> _comboKeys = new();

    private WinForms.NotifyIcon? _trayIcon;

    private ushort _selectedKeyboardVk;

    private Storyboard? _statusPulseStoryboard;

    private IntPtr _keyboardHookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)hookStruct.VkCode;
                Key pressedKey = KeyInterop.KeyFromVirtualKey(vkCode);

                if (_hotkeyStartStop != Key.None && pressedKey == _hotkeyStartStop)
                {
                    ToggleClicker();
                    return (IntPtr)1;
                }

                if (_hotkeyReset != Key.None && pressedKey == _hotkeyReset)
                {
                    ResetClickCounter();
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    public MainWindow()
    {
        InitializeComponent();

        _cpsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _cpsTimer.Tick += CpsTimer_Tick;

        _clicker.RunningStateChanged += OnRunningStateChanged;
        _clicker.ClicksPerformed += OnClicksPerformed;

        LoadSettings();
        SyncTogglesToValues();
        UpdateHotkeyDisplay(HotkeyStartStopBtn, _hotkeyStartStop);
        IntervalSlider.Value = _clicker.Settings.IntervalMs;
        RefreshPresetsList();

        InitTrayIcon();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _keyboardProc = KeyboardHookCallback;
        IntPtr hModule = NativeMethods.GetModuleHandle(Process.GetCurrentProcess().MainModule!.ModuleName);
        _keyboardHookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _keyboardProc,
            hModule,
            0);

        Debug.WriteLine($"[AutoClicker] Keyboard hook installed: {_keyboardHookId != IntPtr.Zero}");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveSettings();

        if (_keyboardHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        _clicker.Stop();
        _clicker.Dispose();

        _trayIcon?.Visible = false;
        _trayIcon?.Dispose();

        base.OnClosed(e);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        _isWaitingForStartStopKey = false;
        _isWaitingForResetKey = false;
        _isWaitingForComboKeys = false;
        if (ComboKeyBindBtn.Content != null && ComboKeyBindBtn.Content.ToString()!.Contains("Нажмите"))
            ComboKeyBindBtn.Content = "Назначить";
    }

    // ===================== Сохранение / Загрузка настроек =====================

    private void LoadSettings()
    {
        _settings = AppSettings.Load();

        _soundEnabled = _settings.SoundEnabled;
        _animationsEnabled = _settings.AnimationsEnabled;
        _minimizeToTrayEnabled = _settings.MinimizeToTray;
        _useMilliseconds = _settings.UseMilliseconds;
        _selectedKeyboardVk = (ushort)_settings.KeyboardVk;

        _clickLimit = _settings.ClickLimit;
        _randomInterval = _settings.RandomInterval;
        _randomPercent = _settings.RandomPercent;
        _windowTitle = _settings.WindowTitle;
        _realAutostart = _settings.RealAutostart;

        SoundService.Enabled = _soundEnabled;

        if (Enum.TryParse<Key>(_settings.HotkeyStartStop, out var ks))
            _hotkeyStartStop = ks;
        if (Enum.TryParse<Key>(_settings.HotkeyReset, out var kr))
            _hotkeyReset = kr;

        _comboKeys.Clear();
        if (!string.IsNullOrWhiteSpace(_settings.ComboKeys))
        {
            foreach (var part in _settings.ComboKeys.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int vk))
                    _comboKeys.Add(vk);
            }
        }

        _clicker.Settings.IntervalMs = _settings.IntervalMs;
        if (Enum.TryParse<Services.InputType>(_settings.InputType, out var it))
            _clicker.Settings.InputDevice = it;
        if (Enum.TryParse<Services.MouseButton>(_settings.MouseButton, out var mb))
            _clicker.Settings.Button = mb;
        _clicker.Settings.KeyboardVk = (ushort)_settings.KeyboardVk;
        if (Enum.TryParse<ClickType>(_settings.ClickType, out var ct))
            _clicker.Settings.ClickType = ct;
        if (Enum.TryParse<ClickerMode>(_settings.Mode, out var cm))
            _clicker.Settings.Mode = cm;
        _clicker.Settings.ClickLimit = _clickLimit;
        _clicker.Settings.RandomInterval = _randomInterval;
        _clicker.Settings.RandomPercent = _randomPercent;
        _clicker.Settings.WindowTitle = _windowTitle;
        _clicker.Settings.ComboKeys = string.Join(",", _comboKeys);

        IntervalBox.Text = _settings.IntervalMs.ToString();
        ClickLimitBox.Text = _clickLimit.ToString();
        RandomPercentBox.Text = _randomPercent.ToString();
        UpdateComboKeyDisplay();

        if (_settings.InputType == "Keyboard")
            BtnInputKeyboard.IsChecked = true;
        else
            BtnInputMouse.IsChecked = true;

        if (_settings.MouseButton == "Right") BtnRightMouse.IsChecked = true;
        else if (_settings.MouseButton == "Middle") BtnMiddleMouse.IsChecked = true;
        else BtnLeftMouse.IsChecked = true;

        if (_settings.ClickType == "Double") BtnDoubleClick.IsChecked = true;
        else BtnSingleClick.IsChecked = true;

        if (_settings.Mode == "Hold") BtnHoldMode.IsChecked = true;
        else BtnToggleMode.IsChecked = true;

        UpdateHotkeyDisplay(HotkeyStartStopBtn, _hotkeyStartStop);
        UpdateHotkeyDisplay(HotkeyResetBtn, _hotkeyReset);

        InputType_Changed(this, new RoutedEventArgs());
    }

    private void SaveSettings()
    {
        _settings.SoundEnabled = _soundEnabled;
        _settings.AnimationsEnabled = _animationsEnabled;
        _settings.MinimizeToTray = _minimizeToTrayEnabled;
        _settings.UseMilliseconds = _useMilliseconds;
        _settings.HotkeyStartStop = _hotkeyStartStop.ToString();
        _settings.HotkeyReset = _hotkeyReset.ToString();
        _settings.KeyboardVk = _selectedKeyboardVk;
        _settings.IntervalMs = ParseInterval();

        _settings.InputType = BtnInputKeyboard.IsChecked == true ? "Keyboard" : "Mouse";
        _settings.MouseButton = GetSelectedMouseButton().ToString();
        _settings.ClickType = GetSelectedClickType().ToString();
        _settings.Mode = GetSelectedMode().ToString();

        _settings.ClickLimit = _clickLimit;
        _settings.RandomInterval = _randomInterval;
        _settings.RandomPercent = _randomPercent;
        _settings.WindowTitle = _windowTitle;
        _settings.RealAutostart = _realAutostart;
        _settings.ComboKeys = string.Join(",", _comboKeys);

        _settings.Save();
    }

    private void SyncTogglesToValues()
    {
        SetToggleVisual(SoundToggleThumb, _soundEnabled);
        SetToggleVisual(AnimationToggleThumb, _animationsEnabled);
        SetToggleVisual(RandomToggleThumb, _randomInterval);
        SetToggleVisual(TrayToggleThumb, _minimizeToTrayEnabled);
        SetToggleVisual(RealAutostartToggleThumb, _realAutostart);

        if (_useMilliseconds)
        {
            UnitToggleThumb.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            UnitToggleThumb.Margin = new Thickness(2, 0, 0, 0);
            UnitMsLabel.Foreground = FindResource("AccentBrush") as SolidColorBrush;
            UnitCpsLabel.Foreground = FindResource("SecondaryTextBrush") as SolidColorBrush;
            IntervalUnitLabel.Text = "мс";
        }
        else
        {
            UnitToggleThumb.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            UnitToggleThumb.Margin = new Thickness(0, 0, 2, 0);
            UnitMsLabel.Foreground = FindResource("SecondaryTextBrush") as SolidColorBrush;
            UnitCpsLabel.Foreground = FindResource("AccentBrush") as SolidColorBrush;
            IntervalUnitLabel.Text = "CPS";
        }
    }

    // ===================== Логика кликера =====================

    private void ToggleClicker()
    {
        Dispatcher.Invoke(() =>
        {
            if (_clicker.IsRunning)
                _clicker.Stop();
            else
            {
                ApplySettingsToService();
                _clicker.Start();
            }
        });
    }

    private void ResetClickCounter()
    {
        Dispatcher.Invoke(() =>
        {
            _clicker.Stop();
            TotalClickCount.Text = "0";
            CurrentCps.Text = "0.0";
            ClickCountText.Text = "Кликов: 0";
        });
    }

    private void ApplySettingsToService()
    {
        _clicker.Settings.IntervalMs = _useMilliseconds
            ? ParseInterval()
            : (int)(1000.0 / ParseCps());

        _clicker.Settings.InputDevice = BtnInputKeyboard.IsChecked == true
            ? Services.InputType.Keyboard
            : Services.InputType.Mouse;

        _clicker.Settings.Button = GetSelectedMouseButton();
        _clicker.Settings.ClickType = GetSelectedClickType();
        _clicker.Settings.Mode = GetSelectedMode();

        if (_clicker.Settings.InputDevice == Services.InputType.Keyboard)
        {
            if (_comboKeys.Count > 0)
            {
                _clicker.Settings.ComboKeys = string.Join(",", _comboKeys);
                _clicker.Settings.KeyboardVk = 0;
            }
            else
            {
                _clicker.Settings.ComboKeys = "";
                _clicker.Settings.KeyboardVk = _selectedKeyboardVk;
            }
        }
        else
        {
            _clicker.Settings.ComboKeys = "";
            _clicker.Settings.KeyboardVk = _selectedKeyboardVk;
        }
    }

    private int ParseInterval()
    {
        if (int.TryParse(IntervalBox.Text, out int ms) && ms >= 1)
            return Math.Min(ms, 60000);
        return 100;
    }

    private double ParseCps()
    {
        if (double.TryParse(IntervalBox.Text, out double cps) && cps > 0)
            return Math.Min(cps, 1000);
        return 10.0;
    }

    private Services.MouseButton GetSelectedMouseButton()
    {
        if (BtnRightMouse.IsChecked == true) return Services.MouseButton.Right;
        if (BtnMiddleMouse.IsChecked == true) return Services.MouseButton.Middle;
        return Services.MouseButton.Left;
    }

    private ClickType GetSelectedClickType()
        => BtnDoubleClick.IsChecked == true ? ClickType.Double : ClickType.Single;

    private ClickerMode GetSelectedMode()
        => BtnHoldMode.IsChecked == true ? ClickerMode.Hold : ClickerMode.Toggle;

    private static string KeyToString(Key key)
    {
        return key == Key.Space ? "Пробел" :
               key == Key.Enter ? "Enter" :
               key == Key.Back ? "Backspace" :
               key == Key.Tab ? "Tab" :
               key.ToString().Replace("D0", "0").Replace("D1", "1")
               .Replace("D2", "2").Replace("D3", "3").Replace("D4", "4")
               .Replace("D5", "5").Replace("D6", "6").Replace("D7", "7")
               .Replace("D8", "8").Replace("D9", "9");
    }

    // ===================== События сервиса =====================

    private void OnRunningStateChanged(bool isRunning)
    {
        Dispatcher.Invoke(() =>
        {
            if (isRunning)
            {
                StartStopButton.Content = "⏹  ОСТАНОВИТЬ";
                StatusIndicator.Fill = FindResource("AccentBrush") as SolidColorBrush;
                StatusText.Text = "Активен";
                _cpsStopwatch.Restart();
                _cpsTimer.Start();
                SoundService.PlayStart();
                StartStatusPulse();
            }
            else
            {
                StartStopButton.Content = "▶  ЗАПУСТИТЬ";
                StatusIndicator.Fill = FindResource("SecondaryTextBrush") as SolidColorBrush;
                StatusText.Text = "Остановлен";
                _cpsStopwatch.Stop();
                _cpsTimer.Stop();
                CurrentCps.Text = "0.0";
                SoundService.PlayStop();
                StopStatusPulse();
            }
        });
    }

    private void OnClicksPerformed(int count)
    {
        Dispatcher.Invoke(() =>
        {
            TotalClickCount.Text = count.ToString("N0");
            ClickCountText.Text = $"Кликов: {count:N0}";
        });
    }

    private void CpsTimer_Tick(object? sender, EventArgs e)
    {
        if (_cpsStopwatch.Elapsed.TotalSeconds > 0)
        {
            double cps = _clicker.ClickCount / _cpsStopwatch.Elapsed.TotalSeconds;
            CurrentCps.Text = cps.ToString("F1");
        }
    }

    // ===================== Горячие клавиши (UI) =====================

    private void HotkeyStartStop_Click(object sender, RoutedEventArgs e)
    {
        _isWaitingForStartStopKey = true;
        _isWaitingForResetKey = false;
        HotkeyStartStopBtn.Content = "Нажмите клавишу...";
        SoundService.PlayClick();
    }

    private void HotkeyReset_Click(object sender, RoutedEventArgs e)
    {
        _isWaitingForResetKey = true;
        _isWaitingForStartStopKey = false;
        HotkeyResetBtn.Content = "Нажмите клавишу...";
        SoundService.PlayClick();
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_isWaitingForStartStopKey)
        {
            if (e.Key == Key.None) return;
            _hotkeyStartStop = e.Key;
            UpdateHotkeyDisplay(HotkeyStartStopBtn, _hotkeyStartStop);
            _isWaitingForStartStopKey = false;
            SoundService.PlayBind();
            SaveSettings();
            e.Handled = true;
        }
        else if (_isWaitingForResetKey)
        {
            if (e.Key == Key.None) return;
            _hotkeyReset = e.Key;
            UpdateHotkeyDisplay(HotkeyResetBtn, _hotkeyReset);
            _isWaitingForResetKey = false;
            SoundService.PlayBind();
            SaveSettings();
            e.Handled = true;
        }
        else if (_isWaitingForComboKeys)
        {
            if (e.Key == Key.None) return;
            if (e.Key == Key.Enter)
            {
                _isWaitingForComboKeys = false;
                ComboKeyBindBtn.Content = "Назначить";
                _clicker.Settings.ComboKeys = string.Join(",", _comboKeys);
                UpdateComboKeyDisplay();
                SoundService.PlayBind();
                SaveSettings();
                e.Handled = true;
                return;
            }

            int vk = KeyInterop.VirtualKeyFromKey(e.Key);
            if (!_comboKeys.Contains(vk))
            {
                _comboKeys.Add(vk);
                UpdateComboKeyDisplay();
                SoundService.PlayClick();
            }
            e.Handled = true;
        }
    }

    private void UpdateHotkeyDisplay(System.Windows.Controls.Button button, Key key)
    {
        button.Content = key == Key.None ? "Не назначена" : key.ToString();
    }

    // ===================== Переключение типа ввода =====================

    private void InputType_Changed(object sender, RoutedEventArgs e)
    {
        if (PanelMouseButtons == null) return;

        if (BtnInputKeyboard.IsChecked == true)
            PanelMouseButtons.Visibility = Visibility.Collapsed;
        else
            PanelMouseButtons.Visibility = Visibility.Visible;
    }

    // ===================== Навигация =====================

    private void NavHome_Click(object sender, RoutedEventArgs e) => ShowPage(PageHome);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowPage(PageSettings);
    private void NavPresets_Click(object sender, RoutedEventArgs e) => ShowPage(PagePresets);
    private void NavAbout_Click(object sender, RoutedEventArgs e) => ShowPage(PageAbout);

    private void ShowPage(FrameworkElement page)
    {
        PageHome.Visibility = Visibility.Collapsed;
        PageSettings.Visibility = Visibility.Collapsed;
        PagePresets.Visibility = Visibility.Collapsed;
        PageAbout.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        FadeInPage(page);
        if (page == PagePresets) RefreshPresetsList();
        if (page == PageSettings) RefreshWindowList();
    }

    // ===================== Управление окном =====================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void StartStopButton_Click(object sender, RoutedEventArgs e) => ToggleClicker();
    private void ResetButton_Click(object sender, RoutedEventArgs e) => ResetClickCounter();

    // ===================== Интервал =====================

    private void IntervalBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !char.IsDigit(e.Text[0]);
    }

    private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IntervalBox == null) return;
        int value = (int)e.NewValue;
        if (_useMilliseconds)
            IntervalBox.Text = value.ToString();
        else
        {
            double cps = value > 0 ? 1000.0 / value : 1000.0;
            IntervalBox.Text = cps.ToString("F1");
        }
    }

    // ===================== Toggle-переключатели =====================

    private void SetToggleVisual(Ellipse thumb, bool enabled)
    {
        thumb.Fill = enabled
            ? FindResource("AccentBrush") as SolidColorBrush
            : FindResource("SecondaryTextBrush") as SolidColorBrush;
        thumb.HorizontalAlignment = enabled ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        thumb.Margin = enabled ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0);
    }

    private void UnitToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _useMilliseconds = !_useMilliseconds;

        if (_useMilliseconds)
        {
            UnitToggleThumb.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            UnitToggleThumb.Margin = new Thickness(2, 0, 0, 0);
            UnitMsLabel.Foreground = FindResource("AccentBrush") as SolidColorBrush;
            UnitCpsLabel.Foreground = FindResource("SecondaryTextBrush") as SolidColorBrush;
            IntervalUnitLabel.Text = "мс";
            if (double.TryParse(IntervalBox.Text, out double cps) && cps > 0)
            {
                int ms = (int)(1000.0 / cps);
                IntervalBox.Text = Math.Max(1, ms).ToString();
                IntervalSlider.Value = ms;
            }
        }
        else
        {
            UnitToggleThumb.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            UnitToggleThumb.Margin = new Thickness(0, 0, 2, 0);
            UnitMsLabel.Foreground = FindResource("SecondaryTextBrush") as SolidColorBrush;
            UnitCpsLabel.Foreground = FindResource("AccentBrush") as SolidColorBrush;
            IntervalUnitLabel.Text = "CPS";
            if (int.TryParse(IntervalBox.Text, out int ms) && ms > 0)
            {
                double cps = 1000.0 / ms;
                IntervalBox.Text = cps.ToString("F1");
                IntervalSlider.Value = ms;
            }
        }
        SaveSettings();
    }

    private void SoundToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _soundEnabled = !_soundEnabled;
        SoundService.Enabled = _soundEnabled;
        SetToggleVisual(SoundToggleThumb, _soundEnabled);
        if (_soundEnabled) SoundService.PlayClick();
        SaveSettings();
    }

    private void AnimationToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _animationsEnabled = !_animationsEnabled;
        SetToggleVisual(AnimationToggleThumb, _animationsEnabled);
        if (!_animationsEnabled) StopStatusPulse();
        SoundService.PlayClick();
        SaveSettings();
    }

    // ===================== Новые настройки =====================

    private void ClickLimitBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !char.IsDigit(e.Text[0]);
    }

    private void ClickLimitBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(ClickLimitBox.Text, out int val))
            _clickLimit = Math.Max(0, val);
        else
            _clickLimit = 0;
        ClickLimitBox.Text = _clickLimit.ToString();
        _clicker.Settings.ClickLimit = _clickLimit;
        SaveSettings();
    }

    private void RandomToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _randomInterval = !_randomInterval;
        SetToggleVisual(RandomToggleThumb, _randomInterval);
        _clicker.Settings.RandomInterval = _randomInterval;
        SoundService.PlayClick();
        SaveSettings();
    }

    private void RandomPercentBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !char.IsDigit(e.Text[0]);
    }

    private void RandomPercentBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(RandomPercentBox.Text, out int val))
            _randomPercent = Math.Clamp(val, 0, 100);
        else
            _randomPercent = 10;
        RandomPercentBox.Text = _randomPercent.ToString();
        _clicker.Settings.RandomPercent = _randomPercent;
        SaveSettings();
    }

    private void TrayToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _minimizeToTrayEnabled = !_minimizeToTrayEnabled;
        SetToggleVisual(TrayToggleThumb, _minimizeToTrayEnabled);
        SaveSettings();
    }

    private void RealAutostartToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _realAutostart = !_realAutostart;
        SetToggleVisual(RealAutostartToggleThumb, _realAutostart);
        SetAutostart(_realAutostart);
        SoundService.PlayClick();
        SaveSettings();
    }

    // ===================== Комбо-клавиши =====================

    private void ComboKeyBind_Click(object sender, RoutedEventArgs e)
    {
        _isWaitingForComboKeys = true;
        _isWaitingForStartStopKey = false;
        _isWaitingForResetKey = false;
        _comboKeys.Clear();
        ComboKeyBindBtn.Content = "Нажмите клавиши, Enter — готово";
        ComboKeyDisplay.Text = "...";
        SoundService.PlayClick();
    }

    private void UpdateComboKeyDisplay()
    {
        if (_comboKeys.Count == 0)
        {
            ComboKeyDisplay.Text = "Не назначена";
            return;
        }

        var names = _comboKeys.Select(vk =>
        {
            var key = KeyInterop.KeyFromVirtualKey(vk);
            return KeyToString(key);
        });
        ComboKeyDisplay.Text = string.Join("+", names);
    }

    // ===================== Привязка к окну =====================

    private void RefreshWindowList()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            int length = NativeMethods.GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new System.Text.StringBuilder(length + 1);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();

            if (!string.IsNullOrWhiteSpace(title))
                windows.Add(new WindowInfo { Title = title, Handle = hWnd });

            return true;
        }, IntPtr.Zero);

        var previousTitle = WindowSelectorCombo.SelectedItem as WindowInfo;

        WindowSelectorCombo.Items.Clear();
        WindowSelectorCombo.Items.Add(new WindowInfo { Title = "Глобально (без привязки)", Handle = IntPtr.Zero });
        windows.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        foreach (var w in windows)
            WindowSelectorCombo.Items.Add(w);

        if (previousTitle != null && previousTitle.Handle != IntPtr.Zero)
        {
            var match = windows.FirstOrDefault(w => w.Handle == previousTitle.Handle);
            if (match != null)
                WindowSelectorCombo.SelectedItem = match;
            else
                WindowSelectorCombo.SelectedIndex = 0;
        }
        else
        {
            WindowSelectorCombo.SelectedIndex = 0;
        }
    }

    private void WindowSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WindowSelectorCombo.SelectedItem is WindowInfo selected && selected.Handle != IntPtr.Zero)
            _windowTitle = selected.Title;
        else
            _windowTitle = "";

        _clicker.Settings.WindowTitle = _windowTitle;
        SaveSettings();
    }

    // ===================== Автозапуск =====================

    private void SetAutostart(bool enable)
    {
        try
        {
            string startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string linkPath = System.IO.Path.Combine(startupPath, "AutoClicker.lnk");

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(exePath)) return;

                var shell = (dynamic)Activator.CreateInstance(
                    Type.GetTypeFromProgID("WScript.Shell")!)!;
                var shortcut = shell.CreateShortcut(linkPath);
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(exePath);
                shortcut.Description = "AutoClicker Pro";
                shortcut.Save();
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
            else
            {
                if (System.IO.File.Exists(linkPath))
                    System.IO.File.Delete(linkPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Autostart] Error: {ex.Message}");
        }
    }

    // ===================== Системный трей =====================

    private void InitTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon();
        _trayIcon.Icon = System.Drawing.SystemIcons.Application;
        _trayIcon.Text = "AutoClicker Pro";
        _trayIcon.Visible = false;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Показать", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("-");
        var startStopItem = menu.Items.Add("Старт/Стоп", null, (_, _) => Dispatcher.Invoke(ToggleClicker));
        menu.Items.Add("-");
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(Close));
        _trayIcon.ContextMenuStrip = menu;

        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);

        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && _minimizeToTrayEnabled)
            {
                Hide();
                _trayIcon.Visible = true;
            }
        };
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        if (_trayIcon != null) _trayIcon.Visible = false;
        Activate();
    }

    // ===================== Анимации =====================

    private void StartStatusPulse()
    {
        if (!_animationsEnabled) return;
        StopStatusPulse();

        _statusPulseStoryboard = new Storyboard();
        var animation = new DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(1))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, StatusIndicator);
        Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));
        _statusPulseStoryboard.Children.Add(animation);
        _statusPulseStoryboard.Begin();
    }

    private void StopStatusPulse()
    {
        _statusPulseStoryboard?.Stop();
        _statusPulseStoryboard = null;
        StatusIndicator.Opacity = 1;
    }

    private void FadeInPage(FrameworkElement page)
    {
        if (!_animationsEnabled) return;
        page.Opacity = 0;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25))
        {
            EasingFunction = new CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        page.BeginAnimation(OpacityProperty, animation);
    }

    // ===================== Пресеты =====================

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = PresetNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            PresetNameBox.BorderBrush = FindResource("DangerBrush") as SolidColorBrush;
            return;
        }

        var presets = PresetsService.Load();

        var currentSettings = new AppSettings
        {
            IntervalMs = ParseInterval(),
            UseMilliseconds = _useMilliseconds,
            InputType = BtnInputKeyboard.IsChecked == true ? "Keyboard" : "Mouse",
            MouseButton = GetSelectedMouseButton().ToString(),
            KeyboardVk = _selectedKeyboardVk,
            ClickType = GetSelectedClickType().ToString(),
            Mode = GetSelectedMode().ToString(),
            HotkeyStartStop = _hotkeyStartStop.ToString(),
            HotkeyReset = _hotkeyReset.ToString(),
            SoundEnabled = _soundEnabled,
            AnimationsEnabled = _animationsEnabled,
            MinimizeToTray = _minimizeToTrayEnabled,
            ClickLimit = _clickLimit,
            RandomInterval = _randomInterval,
            RandomPercent = _randomPercent,
            WindowTitle = _windowTitle,
            RealAutostart = _realAutostart,
            ComboKeys = string.Join(",", _comboKeys)
        };

        var existing = presets.FirstOrDefault(p => p.Name == name);
        if (existing != null)
            existing.Settings = currentSettings;
        else
            presets.Add(new UserPreset { Name = name, Settings = currentSettings });

        PresetsService.Save(presets);
        PresetNameBox.Text = "";
        PresetNameBox.BorderBrush = FindResource("BorderBrush") as SolidColorBrush;
        RefreshPresetsList();
        SoundService.PlayBind();
    }

    private void RefreshPresetsList()
    {
        if (PresetsList == null) return;
        PresetsList.Items.Clear();
        var presets = PresetsService.Load();
        foreach (var preset in presets)
        {
            PresetsList.Items.Add(preset.Name);
        }
    }

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetsList.SelectedIndex < 0) return;

        var presets = PresetsService.Load();
        var selected = presets.FirstOrDefault(p => p.Name == PresetsList.SelectedItem.ToString());
        if (selected == null) return;

        ApplyPreset(selected.Settings);
        SoundService.PlayBind();
    }

    private void PresetsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetsList.SelectedIndex < 0) return;

        var presets = PresetsService.Load();
        var name = PresetsList.SelectedItem.ToString();
        presets.RemoveAll(p => p.Name == name);
        PresetsService.Save(presets);
        RefreshPresetsList();
        SoundService.PlayClick();
    }

    private void ApplyPreset(AppSettings s)
    {
        _useMilliseconds = s.UseMilliseconds;
        _soundEnabled = s.SoundEnabled;
        _animationsEnabled = s.AnimationsEnabled;
        _minimizeToTrayEnabled = s.MinimizeToTray;
        _selectedKeyboardVk = (ushort)s.KeyboardVk;

        _clickLimit = s.ClickLimit;
        _randomInterval = s.RandomInterval;
        _randomPercent = s.RandomPercent;
        _windowTitle = s.WindowTitle;
        _realAutostart = s.RealAutostart;

        SoundService.Enabled = _soundEnabled;

        if (Enum.TryParse<Key>(s.HotkeyStartStop, out var ks)) _hotkeyStartStop = ks;
        if (Enum.TryParse<Key>(s.HotkeyReset, out var kr)) _hotkeyReset = kr;

        _comboKeys.Clear();
        if (!string.IsNullOrWhiteSpace(s.ComboKeys))
        {
            foreach (var part in s.ComboKeys.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int vk))
                    _comboKeys.Add(vk);
            }
        }

        IntervalBox.Text = s.IntervalMs.ToString();
        IntervalSlider.Value = s.IntervalMs;
        ClickLimitBox.Text = _clickLimit.ToString();
        RandomPercentBox.Text = _randomPercent.ToString();
        UpdateComboKeyDisplay();

        if (s.InputType == "Keyboard") BtnInputKeyboard.IsChecked = true;
        else BtnInputMouse.IsChecked = true;

        if (s.MouseButton == "Right") BtnRightMouse.IsChecked = true;
        else if (s.MouseButton == "Middle") BtnMiddleMouse.IsChecked = true;
        else BtnLeftMouse.IsChecked = true;

        if (s.ClickType == "Double") BtnDoubleClick.IsChecked = true;
        else BtnSingleClick.IsChecked = true;

        if (s.Mode == "Hold") BtnHoldMode.IsChecked = true;
        else BtnToggleMode.IsChecked = true;

        UpdateHotkeyDisplay(HotkeyStartStopBtn, _hotkeyStartStop);
        UpdateHotkeyDisplay(HotkeyResetBtn, _hotkeyReset);

        _clicker.Settings.ClickLimit = _clickLimit;
        _clicker.Settings.RandomInterval = _randomInterval;
        _clicker.Settings.RandomPercent = _randomPercent;
        _clicker.Settings.WindowTitle = _windowTitle;
        _clicker.Settings.ComboKeys = string.Join(",", _comboKeys);

        SyncTogglesToValues();
        InputType_Changed(this, new RoutedEventArgs());
        SaveSettings();
    }

    // ===================== Ссылки (About) =====================

    private void LinkGitHub_Click(object sender, MouseButtonEventArgs e)
        => Process.Start(new ProcessStartInfo("https://github.com/dima-kovboi") { UseShellExecute = true });

    private void LinkTelegram_Click(object sender, MouseButtonEventArgs e)
        => Process.Start(new ProcessStartInfo("https://t.me/dimadeploy") { UseShellExecute = true });

    private void LinkTelegramChannel_Click(object sender, MouseButtonEventArgs e)
        => Process.Start(new ProcessStartInfo("https://t.me/dima_kovboi") { UseShellExecute = true });
}
