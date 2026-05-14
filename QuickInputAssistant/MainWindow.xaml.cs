using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using QuickInputAssistant.Models;
using QuickInputAssistant.PInvoke;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;

namespace QuickInputAssistant;

public sealed partial class MainWindow : Window
{
    private enum ViewMode { Capsule, Keyboard, List }

    private readonly ILogger<MainWindow> _log = App.LoggerFactory.CreateLogger<MainWindow>();
    private readonly Dictionary<char, TextBlock> _kbValues = new();
    private readonly Dictionary<char, TextBlock> _listValues = new();
    private ViewMode _mode = ViewMode.Keyboard;
    private IntPtr _hwnd;
    private bool _regionUpdateQueued;

    // 自定义实时拖拽状态
    private bool _dragging;
    private POINT _dragStartCursor;
    private PointInt32 _dragStartWin;
    private UIElement? _dragElement;

    // 三种模式统一窗口宽度（其他尺寸全部由 XAML 测量得到）
    private const int WIN_W = 430;
    private const int BTN_SZ = 26;
    private const double SB_RADIUS = 15;
    private const double ROW_RADIUS = 10;

    // Segoe Fluent Icons / MDL2 Assets 字形（保持视觉一致）
    private const string ICON_KEYBOARD = ""; // KeyboardClassic
    private const string ICON_LIST     = ""; // List
    private const string ICON_UP       = ""; // ChevronUp
    private const string ICON_DOWN     = ""; // ChevronDown
    private const string ICON_SETTINGS = ""; // Setting (gear)
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // 状态栏齿轮按钮左侧显示当前预设名（由 RebuildActionButtons 创建）
    private TextBlock? _presetNameText;

    // 编辑态期间所有外部点击都路由到这两个委托：左键=提交、右键=取消
    private Action? _activeEditCommit;
    private Action? _activeEditCancel;

    private static readonly char[] Row1 = { '1', '2', '3', '4', '5', '6' };
    private static readonly char[] Row2 = { 'Q', 'W', 'E', 'R' };
    private static readonly char[] Row3 = { 'A', 'S', 'D', 'F' };
    private static readonly char[] ListLeft  = { '1', '2', '3', '4', '5', '6' };
    private static readonly char[] ListRight = { 'Q', 'W', 'E', 'R', 'A', 'S', 'D', 'F' };

    // 主题相关 brush：颜色由 ApplyThemeColors() 在运行时填充
    private static readonly SolidColorBrush BrCapBg     = new();
    private static readonly SolidColorBrush BrCapHover  = new();
    private static readonly SolidColorBrush BrCapBorder = new();
    private static readonly SolidColorBrush BrFg        = new();
    private static readonly SolidColorBrush BrFgMute    = new();
    private static readonly SolidColorBrush BrAccent    = new();
    private static readonly SolidColorBrush BrSuccess   = new();
    private static readonly SolidColorBrush BrInfo      = new();
    private static readonly SolidColorBrush BrWarn      = new();
    private static readonly SolidColorBrush BrIdle      = new();
    private static readonly SolidColorBrush BrHoverBg   = new();
    private static readonly SolidColorBrush BrSurface1  = new(); // 状态栏 / 列表面板背景
    private static readonly SolidColorBrush BrSurface2  = new(); // 键盘行背景
    private static readonly SolidColorBrush BrDivider   = new(); // 列表竖向分隔线
    private static readonly SolidColorBrush BrFlyoutBg     = new();
    private static readonly SolidColorBrush BrFlyoutBorder = new();
    // 透明常量，不随主题变化
    private static readonly SolidColorBrush BrTransp    = new(Colors.Transparent);

    private enum AppTheme  { Dark, Light }
    private enum ThemeMode { Dark, Light, Auto }
    private AppTheme  _currentTheme = AppTheme.Dark;
    private ThemeMode _themeMode    = ThemeMode.Dark;

    /// <summary>检测系统主题：true=系统当前是深色</summary>
    private static bool SystemIsDark()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (k?.GetValue("AppsUseLightTheme") is int v) return v == 0;
        }
        catch { }
        return false;
    }

    /// <summary>把 ThemeMode 解析为实际渲染的 AppTheme（Auto = 系统反向）</summary>
    private static AppTheme ResolveTheme(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark  => AppTheme.Dark,
        ThemeMode.Light => AppTheme.Light,
        ThemeMode.Auto  => SystemIsDark() ? AppTheme.Light : AppTheme.Dark,
        _               => AppTheme.Dark,
    };

    private record ThemeColors(
        Color CapBg, Color CapHover, Color CapBorder, Color Fg, Color FgMute,
        Color Accent, Color Success, Color Info, Color Warn, Color Idle,
        Color HoverBg, Color Surface1, Color Surface2, Color Divider,
        Color FlyoutBg, Color FlyoutBorder);

    private static readonly ThemeColors DarkColors = new(
        CapBg:        Color.FromArgb(0xE0, 0x2C, 0x2C, 0x32),
        CapHover:     Color.FromArgb(0xF0, 0x40, 0x40, 0x48),
        CapBorder:    Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
        Fg:           Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF),
        FgMute:       Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF),
        Accent:       Color.FromArgb(0xFF, 0x4C, 0xC2, 0xFF),
        Success:      Color.FromArgb(0xFF, 0x5D, 0xD2, 0x8B),
        Info:         Color.FromArgb(0xFF, 0x79, 0xC5, 0xFF),
        Warn:         Color.FromArgb(0xFF, 0xFF, 0xC4, 0x6A),
        Idle:         Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF),
        HoverBg:      Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
        Surface1:     Color.FromArgb(0xF0, 0x14, 0x14, 0x16),
        Surface2:     Color.FromArgb(0xD0, 0x12, 0x12, 0x16),
        Divider:      Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF),
        FlyoutBg:     Color.FromArgb(0xF2, 0x1A, 0x1A, 0x1E),
        FlyoutBorder: Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    private static readonly ThemeColors LightColors = new(
        CapBg:        Color.FromArgb(0xF0, 0xF4, 0xF4, 0xF7),
        CapHover:     Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE4),
        CapBorder:    Color.FromArgb(0x33, 0x00, 0x00, 0x00),
        Fg:           Color.FromArgb(0xF5, 0x00, 0x00, 0x00),
        FgMute:       Color.FromArgb(0x99, 0x00, 0x00, 0x00),
        Accent:       Color.FromArgb(0xFF, 0x00, 0x67, 0xC0),
        Success:      Color.FromArgb(0xFF, 0x0E, 0x70, 0x32),
        Info:         Color.FromArgb(0xFF, 0x00, 0x5A, 0x9E),
        Warn:         Color.FromArgb(0xFF, 0x9D, 0x5D, 0x00),
        Idle:         Color.FromArgb(0x99, 0x00, 0x00, 0x00),
        HoverBg:      Color.FromArgb(0x1A, 0x00, 0x00, 0x00),
        Surface1:     Color.FromArgb(0xF2, 0xF4, 0xF4, 0xF7),
        Surface2:     Color.FromArgb(0xE0, 0xEC, 0xEC, 0xF0),
        Divider:      Color.FromArgb(0x18, 0x00, 0x00, 0x00),
        FlyoutBg:     Color.FromArgb(0xF6, 0xFA, 0xFA, 0xFC),
        FlyoutBorder: Color.FromArgb(0x33, 0x00, 0x00, 0x00));

    static MainWindow() { ApplyThemeColors(AppTheme.Dark); }

    private static void ApplyThemeColors(AppTheme theme)
    {
        var c = theme == AppTheme.Dark ? DarkColors : LightColors;
        BrCapBg.Color     = c.CapBg;
        BrCapHover.Color  = c.CapHover;
        BrCapBorder.Color = c.CapBorder;
        BrFg.Color        = c.Fg;
        BrFgMute.Color    = c.FgMute;
        BrAccent.Color    = c.Accent;
        BrSuccess.Color   = c.Success;
        BrInfo.Color      = c.Info;
        BrWarn.Color      = c.Warn;
        BrIdle.Color      = c.Idle;
        BrHoverBg.Color   = c.HoverBg;
        BrSurface1.Color  = c.Surface1;
        BrSurface2.Color  = c.Surface2;
        BrDivider.Color   = c.Divider;
        BrFlyoutBg.Color  = c.FlyoutBg;
        BrFlyoutBorder.Color = c.FlyoutBorder;
    }

    private void SetThemeMode(ThemeMode mode)
    {
        _themeMode = mode;
        _currentTheme = ResolveTheme(mode);
        ApplyThemeColors(_currentTheme);
        if (RootGrid != null)
            ApplyThemeToShell();
        App.StoreSvc.Theme = mode.ToString();
    }

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // 真透明背景：WinUIEx 的 TransparentTintBackdrop 让窗口背景真正透明，
        // XAML 圆角 Border 由 WinUI3 用 DirectX 抗锯齿渲染，无白边无锯齿
        try { this.SystemBackdrop = new WinUIEx.TransparentTintBackdrop(); }
        catch (Exception ex) { _log.LogWarning(ex, "TransparentTintBackdrop 设置失败"); }
        // 从存储加载主题模式（默认 Dark）
        _themeMode = Enum.TryParse<ThemeMode>(App.StoreSvc.Theme, ignoreCase: true, out var m) ? m : ThemeMode.Dark;
        _currentTheme = ResolveTheme(_themeMode);
        ApplyThemeColors(_currentTheme);

        ConfigureWindow();
        ApplyThemeToShell();
        PopulateUI();
        App.StatusSvc.StatusChanged += OnStatusChanged;
        App.StoreSvc.ActiveSlotChanged += OnActiveSlotChanged;
    }

    private void ApplyThemeToShell()
    {
        // 设置由 XAML 引用的 shell 元素背景
        RootGrid.RequestedTheme = _currentTheme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        // brush 颜色变化已经自动重绘，下面只是首次绑定
        StatusBar.Background      = BrSurface1;
        KBRow1Border.Background   = BrSurface2;
        KBRow2Border.Background   = BrSurface2;
        KBRow3Border.Background   = BrSurface2;
        ListContent.Background    = BrSurface1;
        ListDivider.Background    = BrDivider;
    }

    private void OnActiveSlotChanged(int slot)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_presetNameText != null) _presetNameText.Text = App.StoreSvc.GetSlotName(slot);
            RefreshValues();
        });
    }

    private void ConfigureWindow()
    {
        AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "快捷输入助手";

        try
        {
            int style = User32.GetWindowLong(_hwnd, GWL.STYLE);
            style &= ~(WS.CAPTION | WS.THICKFRAME | WS.SYSMENU);
            User32.SetWindowLong(_hwnd, GWL.STYLE, style);

            int exStyle = User32.GetWindowLong(_hwnd, GWL.EXSTYLE);
            exStyle |= WS_EX.NOACTIVATE | WS_EX.TOOLWINDOW;
            User32.SetWindowLong(_hwnd, GWL.EXSTYLE, exStyle);

            // 禁用 Windows 11 默认窗口圆角（避免和 SetWindowRgn 叠加产生白边）
            int doNotRound = 1; // DWMWCP_DONOTROUND
            User32.DwmSetWindowAttribute(_hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref doNotRound, 4);
        }
        catch (Exception ex) { _log.LogWarning(ex, "设置窗口样式失败"); }

        AppWindow.Resize(new SizeInt32(WIN_W, 200));

        RootGrid.Loaded += (_, _) =>
        {
            ResizeForMode(_mode);
            PositionWindow();
            ForceTopmost();
            // 不再用 SetWindowRgn 裁剪窗口形状（GDI 区域硬切产生锯齿）。
            // TransparentTintBackdrop 让窗口真透明，可见形状由 XAML 圆角 Border 决定，
            // WinUI3 的 DirectX 渲染对边缘做抗锯齿，圆角光滑。
        };

        // 每次激活时重新置顶（防止其他窗口/系统操作把它压下去）
        this.Activated += (_, _) => ForceTopmost();

        Closed += (_, _) => App.Cleanup();
        _log.LogInformation("MainWindow 初始化完成 (三态窗口)");
    }

    private void ForceTopmost()
    {
        try
        {
            bool ok = User32.SetWindowPos(_hwnd, HWND_Z.TOPMOST, 0, 0, 0, 0,
                SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                _log.LogWarning("SetWindowPos(TOPMOST) 失败, error={Err}", err);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "ForceTopmost 异常"); }
    }

    private void PositionWindow()
    {
        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var work = display.WorkArea;
            var size = AppWindow.Size;
            AppWindow.Move(new PointInt32(
                work.X + work.Width - size.Width - 20,
                work.Y + work.Height / 3));
        }
        catch (Exception ex) { _log.LogWarning(ex, "定位窗口失败"); }
    }

    private void ResizeForMode(ViewMode mode)
    {
        double scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        // 状态栏 30 + gap 4 + 键盘行(38+padding 6=44)*3 + 行间距 2*2 = 30+4+132+4 = 170
        int h = mode switch
        {
            ViewMode.Capsule  => 40,
            ViewMode.Keyboard => 185,
            ViewMode.List     => 260,
            _ => 40,
        };
        AppWindow.Resize(new SizeInt32((int)(WIN_W * scale), (int)(h * scale)));
    }

    // ── 区域裁剪：用 XAML 实测尺寸构建窗口形状 ──────────────────────
    private void ScheduleRegionUpdate()
    {
        if (_regionUpdateQueued) return;
        _regionUpdateQueued = true;
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            if (handler != null) RootGrid.LayoutUpdated -= handler;
            _regionUpdateQueued = false;
            UpdateWindowRegion();
        };
        RootGrid.LayoutUpdated += handler;
    }

    private void UpdateWindowRegion()
    {
        if (_hwnd == IntPtr.Zero || RootGrid.XamlRoot == null) return;
        try
        {
            double scale = RootGrid.XamlRoot.RasterizationScale;

            // 状态栏始终显示
            IntPtr total = MakeElementRgn(StatusBar, scale, SB_RADIUS);

            void OrIn(FrameworkElement fe, double radius)
            {
                if (fe.Visibility != Visibility.Visible || fe.ActualWidth <= 0) return;
                var rgn = MakeElementRgn(fe, scale, radius);
                User32.CombineRgn(total, total, rgn, RGN.OR);
                User32.DeleteObject(rgn);
            }

            if (_mode == ViewMode.Keyboard)
            {
                OrIn(KBRow1Border, ROW_RADIUS);
                OrIn(KBRow2Border, ROW_RADIUS);
                OrIn(KBRow3Border, ROW_RADIUS);
            }
            else if (_mode == ViewMode.List)
            {
                OrIn(ListContent, ROW_RADIUS);
            }

            User32.SetWindowRgn(_hwnd, total, true);
            // total 已交给 OS，不释放
        }
        catch (Exception ex) { _log.LogWarning(ex, "更新窗口区域失败"); }
    }

    private IntPtr MakeElementRgn(FrameworkElement fe, double scale, double radius)
    {
        var pt = fe.TransformToVisual(RootGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
        // region 比元素 bounding box 各方向扩 1px，包住 XAML 圆角边缘的抗锯齿过渡像素
        // （region 是硬切的，若边界恰好穿过半透明像素会显出白边）
        int x1 = (int)Math.Floor(pt.X * scale) - 1;
        int y1 = (int)Math.Floor(pt.Y * scale) - 1;
        int x2 = (int)Math.Ceiling((pt.X + fe.ActualWidth) * scale) + 1;
        int y2 = (int)Math.Ceiling((pt.Y + fe.ActualHeight) * scale) + 1;
        int r  = ((int)Math.Round(radius * scale) + 1) * 2;
        return User32.CreateRoundRectRgn(x1, y1, x2, y2, r, r);
    }

    // ── UI Population ────────────────────────────────────────────────────

    private void PopulateUI()
    {
        foreach (char k in Row1) KBRow1.Children.Add(MakeKeyCap(k, _kbValues));
        foreach (char k in Row2) KBRow2.Children.Add(MakeKeyCap(k, _kbValues));
        foreach (char k in Row3) KBRow3.Children.Add(MakeKeyCap(k, _kbValues));

        foreach (char k in ListLeft)  ListCol1.Children.Add(MakeListRow(k, _listValues));
        foreach (char k in ListRight) ListCol2.Children.Add(MakeListRow(k, _listValues));

        RebuildActionButtons();
        RefreshValues();
    }

    private Border MakeKeyCap(char key, Dictionary<char, TextBlock> reg)
    {
        var border = new Border
        {
            Width = 64, Height = 38,
            CornerRadius = new CornerRadius(6),
            Background = BrCapBg,
            BorderBrush = BrCapBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 3, 5, 3),
        };
        border.PointerEntered += (s, _) => ((Border)s!).Background = BrCapHover;
        border.PointerExited  += (s, _) => ((Border)s!).Background = BrCapBg;

        // label 行永远可见
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        var tbAlt = Txt("Alt",            9, BrFgMute, FontWeights.Medium); tbAlt.VerticalAlignment = VerticalAlignment.Center;
        var tbPlus= Txt("+",              9, BrFgMute, FontWeights.Medium); tbPlus.VerticalAlignment = VerticalAlignment.Center;
        var tbKey = Txt(key.ToString(),   9, BrFg,     FontWeights.Bold);   tbKey.VerticalAlignment  = VerticalAlignment.Center;
        label.Children.Add(tbAlt); label.Children.Add(tbPlus); label.Children.Add(tbKey);

        var val = new TextBlock
        {
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = BrFg,
            MaxLines = 1,
            IsHitTestVisible = false,
        };

        // editBox：完全透明，覆盖 val 区域，不改变键帽外观
        var editBox = new TextBox
        {
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Foreground = BrFg,
            Background = BrTransp,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            AcceptsReturn = false,
            Visibility = Visibility.Collapsed,
        };
        ApplyTransparentTextBoxStyle(editBox);

        // 锁死高度 14px（≈ FontSize=10 的自然行高），防止 TextBox 内部模板撑大父容器挤掉 label
        var valContainer = new Grid { Height = 14 };
        valContainer.Children.Add(val);
        valContainer.Children.Add(editBox);

        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(label);
        stack.Children.Add(valContainer);
        border.Child = stack;

        border.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(null).Properties;
            // 编辑态下键帽点击仅作「确认/取消」，不输出绑定字符
            if (_activeEditCommit != null)
            {
                if (props.IsRightButtonPressed) _activeEditCancel?.Invoke();
                else _activeEditCommit?.Invoke();
                e.Handled = true;
                return;
            }
            if (props.IsRightButtonPressed) BeginEdit(key, val, editBox, border);
            else App.OnKeyCapClicked(key);
            e.Handled = true;
        };

        reg[key] = val;
        return border;
    }

    private static void HideTextBoxDeleteButton(TextBox tb)
    {
        // WinUI 3 TextBox 内置 DeleteButton（X 按钮），单纯设 Visibility 会被 VisualState 反复重置。
        // 同时把 MaxWidth/MinWidth/Width 全归零，从布局层面剥夺它的空间
        if (FindDescendantByName(tb, "DeleteButton") is FrameworkElement btn)
        {
            btn.MinWidth = 0;
            btn.MaxWidth = 0;
            btn.Width    = 0;
            btn.Visibility = Visibility.Collapsed;
        }
    }

    private static FrameworkElement? FindDescendantByName(DependencyObject parent, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            var deeper = FindDescendantByName(child, name);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static void ApplyTransparentTextBoxStyle(TextBox tb)
    {
        // 覆盖 WinUI 3 TextBox 主题资源：背景/边框/前景全透明化，
        // 否则即使设了 Background/Foreground 属性，焦点/悬浮状态会被主题刷子覆盖
        tb.Resources["TextControlBorderBrush"]            = BrTransp;
        tb.Resources["TextControlBorderBrushPointerOver"] = BrTransp;
        tb.Resources["TextControlBorderBrushFocused"]     = BrTransp;
        tb.Resources["TextControlBorderBrushDisabled"]    = BrTransp;
        tb.Resources["TextControlBackground"]             = BrTransp;
        tb.Resources["TextControlBackgroundPointerOver"]  = BrTransp;
        tb.Resources["TextControlBackgroundFocused"]      = BrTransp;
        tb.Resources["TextControlBackgroundDisabled"]     = BrTransp;
        // 前景：必须显式覆盖，否则文字不可见
        tb.Resources["TextControlForeground"]             = BrFg;
        tb.Resources["TextControlForegroundPointerOver"]  = BrFg;
        tb.Resources["TextControlForegroundFocused"]      = BrFg;
        tb.Resources["TextControlForegroundDisabled"]     = BrFg;
        // 去掉 TextBox 内部主题 padding（默认上下各 5-6px）和最小高度（默认 32px），
        // 否则会把整个键帽撑满，挤掉上方 label
        tb.Resources["TextControlThemePadding"]           = new Thickness(0);
        tb.MinHeight = 0;
        tb.MinWidth  = 0;
    }

    private Border MakeListRow(char key, Dictionary<char, TextBlock> reg)
    {
        var border = new Border
        {
            Padding = new Thickness(9, 4, 9, 4),
            Background = BrTransp,
        };
        border.PointerEntered += (s, _) => ((Border)s!).Background = BrHoverBg;
        border.PointerExited  += (s, _) => ((Border)s!).Background = BrTransp;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyTb = new TextBlock
        {
            FontSize = 11, Foreground = BrFgMute,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        keyTb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Alt+" });
        keyTb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = key.ToString(),
            FontWeight = FontWeights.Bold,
            Foreground = BrFg,
        });
        Grid.SetColumn(keyTb, 0);
        grid.Children.Add(keyTb);

        var val = new TextBlock
        {
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = BrFg,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLines = 1,
            IsHitTestVisible = false,
        };

        var valEdit = new TextBox
        {
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Foreground = BrFg,
            Background = BrTransp,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            AcceptsReturn = false,
            Visibility = Visibility.Collapsed,
        };
        ApplyTransparentTextBoxStyle(valEdit);

        // 锁死高度 16px（≈ FontSize=11 的自然行高）
        var valContainer = new Grid { Height = 16 };
        valContainer.Children.Add(val);
        valContainer.Children.Add(valEdit);
        Grid.SetColumn(valContainer, 1);
        grid.Children.Add(valContainer);

        border.Child = grid;

        border.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(null).Properties;
            if (_activeEditCommit != null)
            {
                if (props.IsRightButtonPressed) _activeEditCancel?.Invoke();
                else _activeEditCommit?.Invoke();
                e.Handled = true;
                return;
            }
            if (props.IsRightButtonPressed) BeginEdit(key, val, valEdit, border);
            else App.OnKeyCapClicked(key);
            e.Handled = true;
        };

        reg[key] = val;
        return border;
    }

    // 三种模式都是 2 个固定尺寸按钮（加预设名 + 齿轮固定在前），用 Segoe Fluent 图标确保字形一致
    private void RebuildActionButtons()
    {
        ActionButtons.Children.Clear();

        // 预设名（齿轮左侧）
        _presetNameText = new TextBlock
        {
            Text = App.StoreSvc.GetSlotName(App.StoreSvc.ActiveSlot),
            FontSize = 11,
            Foreground = BrFgMute,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            MaxWidth = 90,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        ActionButtons.Children.Add(_presetNameText);

        // 齿轮（设置）按钮 - 始终显示
        ActionButtons.Children.Add(MakeGearButton());

        // 模式切换 / 折叠按钮
        if (_mode == ViewMode.Capsule)
        {
            ActionButtons.Children.Add(MakeActionBtn(ICON_LIST, OnSwitchToList));
            ActionButtons.Children.Add(MakeActionBtn(ICON_DOWN, OnSwitchToKB));
        }
        else if (_mode == ViewMode.Keyboard)
        {
            ActionButtons.Children.Add(MakeActionBtn(ICON_LIST, OnSwitchToList));
            ActionButtons.Children.Add(MakeActionBtn(ICON_UP,   OnCollapse));
        }
        else if (_mode == ViewMode.List)
        {
            ActionButtons.Children.Add(MakeActionBtn(ICON_KEYBOARD, OnSwitchToKB));
            ActionButtons.Children.Add(MakeActionBtn(ICON_UP,       OnCollapse));
        }
    }

    private Border MakeGearButton()
    {
        var b = new Border
        {
            Width = BTN_SZ, Height = BTN_SZ,
            CornerRadius = new CornerRadius(3),
            Background = BrTransp,
        };
        b.PointerPressed += (s, e) =>
        {
            // 每次点击重建，主题切换后立即生效
            BuildSettingsFlyout().ShowAt((FrameworkElement)s!);
            e.Handled = true;
        };
        b.PointerEntered += (s, _) => ((Border)s!).Background = BrHoverBg;
        b.PointerExited  += (s, _) => ((Border)s!).Background = BrTransp;
        b.Child = new TextBlock
        {
            Text = ICON_SETTINGS,
            FontFamily = IconFont,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = BrFg,
            IsHitTestVisible = false,
        };
        return b;
    }

    // ── 弹窗样式工厂：让 Flyout / MenuFlyout 符合当前主题 ───────────
    private Style MakeDarkMenuStyle()
    {
        var s = new Style(typeof(MenuFlyoutPresenter));
        s.Setters.Add(new Setter(Control.BackgroundProperty,      BrFlyoutBg));
        s.Setters.Add(new Setter(Control.BorderBrushProperty,     BrFlyoutBorder));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.CornerRadiusProperty,    new CornerRadius(8)));
        s.Setters.Add(new Setter(Control.FontSizeProperty,        11.0));
        s.Setters.Add(new Setter(Control.PaddingProperty,         new Thickness(4)));
        s.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty,
            _currentTheme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light));
        return s;
    }

    private Style MakeDarkFlyoutStyle()
    {
        var s = new Style(typeof(FlyoutPresenter));
        s.Setters.Add(new Setter(Control.BackgroundProperty,      BrFlyoutBg));
        s.Setters.Add(new Setter(Control.BorderBrushProperty,     BrFlyoutBorder));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.CornerRadiusProperty,    new CornerRadius(10)));
        s.Setters.Add(new Setter(Control.PaddingProperty,         new Thickness(12)));
        s.Setters.Add(new Setter(Control.FontSizeProperty,        11.0));
        s.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty,
            _currentTheme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light));
        return s;
    }

    private MenuFlyout BuildSettingsFlyout()
    {
        var flyout = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = MakeDarkMenuStyle(),
            ShouldConstrainToRootBounds = false,
        };

        var help = MakeMenuItem("帮助");
        help.Click += OnHelpClicked;
        flyout.Items.Add(help);

        var themeSub = new MenuFlyoutSubItem { Text = "切换主题", FontSize = 11, MinHeight = 0, Padding = new Thickness(10, 4, 10, 4) };
        flyout.Items.Add(themeSub);

        var presetSub = new MenuFlyoutSubItem { Text = "预设管理", FontSize = 11, MinHeight = 0, Padding = new Thickness(10, 4, 10, 4) };
        flyout.Items.Add(presetSub);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var exit = MakeMenuItem("退出应用");
        exit.Click += OnExitClicked;
        flyout.Items.Add(exit);

        // 每次打开时重建主题/预设子菜单（反映当前选择）
        flyout.Opening += (_, _) =>
        {
            BuildThemeSubmenu(themeSub);
            BuildPresetSubmenu(presetSub);
        };
        return flyout;
    }

    private void BuildThemeSubmenu(MenuFlyoutSubItem parent)
    {
        parent.Items.Clear();
        void Add(string text, ThemeMode mode)
        {
            var item = MakeMenuItem((_themeMode == mode ? "● " : "    ") + text);
            item.Click += (_, _) =>
            {
                SetThemeMode(mode);
                App.StatusSvc?.Set(new StatusMessage
                {
                    Tone = StatusTone.Info,
                    Text = mode switch
                    {
                        ThemeMode.Dark  => "已切换为深色主题",
                        ThemeMode.Light => "已切换为浅色主题",
                        _               => $"已跟随系统（当前为{(_currentTheme == AppTheme.Dark ? "深" : "浅")}色）",
                    },
                });
            };
            parent.Items.Add(item);
        }
        Add("深色主题", ThemeMode.Dark);
        Add("浅色主题", ThemeMode.Light);
        Add("跟随系统", ThemeMode.Auto);
    }

    private void BuildPresetSubmenu(MenuFlyoutSubItem parent)
    {
        parent.Items.Clear();
        int active = App.StoreSvc.ActiveSlot;
        for (int i = 0; i < App.StoreSvc.SlotTotal; i++)
        {
            int slot = i;
            var item = MakeMenuItem((slot == active ? "● " : "    ") + App.StoreSvc.GetSlotName(slot));
            item.Click += (_, _) => App.StoreSvc.SwitchSlot(slot);
            parent.Items.Add(item);
        }
        parent.Items.Add(new MenuFlyoutSeparator());
        var rename = MakeMenuItem("重命名当前预设…");
        rename.Click += OnRenameCurrentClicked;
        parent.Items.Add(rename);
    }

    private static MenuFlyoutItem MakeMenuItem(string text) => new()
    {
        Text = text,
        FontSize = 11,
        MinHeight = 0,
        Padding = new Thickness(10, 4, 10, 4),
    };

    private void OnHelpClicked(object sender, RoutedEventArgs e)
    {
        string text =
            "【输出绑定文字】\n" +
            "• 按 Alt+1~6 / Q-R / A-F 任一组合键，输出绑定的文字到当前焦点窗口\n" +
            "• 或直接单击 UI 上的按键\n\n" +
            "【日期键 Alt+Q】\n" +
            "• 单击：输出今日日期 (YY/MM/DD)\n" +
            "• 双击：撤销前一次并改为 +1 天\n\n" +
            "【修改绑定】\n" +
            "• 鼠标右击 UI 上的按键 → 直接在键帽内编辑文字 → 回车保存\n" +
            "• 或在外部应用先选中文字 → 按 Alt+键 自动绑定\n\n" +
            "【预设】\n" +
            "• 4 套独立绑定，可命名、可切换\n" +
            "• 齿轮菜单 → 预设管理";
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = BrFg,
            LineHeight = 17,
        };
        var sv = new ScrollViewer { MaxHeight = 320, MaxWidth = 360, Content = tb };
        var flyout = new Flyout
        {
            Content = sv,
            FlyoutPresenterStyle = MakeDarkFlyoutStyle(),
            ShouldConstrainToRootBounds = false,
        };
        flyout.ShowAt(StatusBar);
    }


    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    private void OnRenameCurrentClicked(object sender, RoutedEventArgs e)
    {
        int active = App.StoreSvc.ActiveSlot;
        var tb = new TextBox
        {
            Text = App.StoreSvc.GetSlotName(active),
            Width = 200,
            FontSize = 11,
            PlaceholderText = $"预设{active + 1}",
        };
        var okBtn     = new Button { Content = "确定", FontSize = 11, MinWidth = 56, Padding = new Thickness(8, 3, 8, 3) };
        var cancelBtn = new Button { Content = "取消", FontSize = 11, MinWidth = 56, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0) };
        var btnRow    = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        var panel = new StackPanel { Spacing = 6, MinWidth = 220 };
        panel.Children.Add(new TextBlock { Text = "重命名预设", FontSize = 11, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(tb);
        panel.Children.Add(btnRow);

        var flyout = new Flyout
        {
            Content = panel,
            FlyoutPresenterStyle = MakeDarkFlyoutStyle(),
            ShouldConstrainToRootBounds = false,
        };
        void DoOk()
        {
            App.StoreSvc.RenameSlot(active, tb.Text);
            if (_presetNameText != null) _presetNameText.Text = App.StoreSvc.GetSlotName(active);
            flyout.Hide();
            SetEditMode(false);
            App.StatusSvc?.Set(new StatusMessage { Tone = StatusTone.Success, Text = $"预设已重命名为 \"{App.StoreSvc.GetSlotName(active)}\"" });
        }
        void DoCancel() { flyout.Hide(); SetEditMode(false); }
        okBtn.Click     += (_, _) => DoOk();
        cancelBtn.Click += (_, _) => DoCancel();
        tb.KeyDown += (_, ke) =>
        {
            if (ke.Key == Windows.System.VirtualKey.Enter)      { DoOk();     ke.Handled = true; }
            else if (ke.Key == Windows.System.VirtualKey.Escape){ DoCancel(); ke.Handled = true; }
        };
        flyout.Closed += (_, _) => SetEditMode(false);

        SetEditMode(true);  // 解除 WS_EX_NOACTIVATE 以便 TextBox 接收键盘输入
        flyout.ShowAt(StatusBar);
        tb.Loaded += (_, _) =>
        {
            tb.Focus(FocusState.Programmatic);
            tb.SelectAll();
        };
    }

    private Border MakeActionBtn(string icon, PointerEventHandler handler)
    {
        var b = new Border
        {
            Width = BTN_SZ, Height = BTN_SZ,
            CornerRadius = new CornerRadius(3),
            Background = BrTransp,
        };
        b.PointerPressed += (s, e) => { handler(s!, e); e.Handled = true; };
        b.PointerEntered += (s, _) => ((Border)s!).Background = BrHoverBg;
        b.PointerExited  += (s, _) => ((Border)s!).Background = BrTransp;
        b.Child = new TextBlock
        {
            Text = icon,
            FontFamily = IconFont,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = BrFg,
            IsHitTestVisible = false,
        };
        return b;
    }

    private void SwitchMode(ViewMode mode)
    {
        _mode = mode;
        ContentArea.Visibility = mode == ViewMode.Capsule ? Visibility.Collapsed : Visibility.Visible;
        KBContent.Visibility   = mode == ViewMode.Keyboard ? Visibility.Visible : Visibility.Collapsed;
        ListContent.Visibility = mode == ViewMode.List     ? Visibility.Visible : Visibility.Collapsed;
        RebuildActionButtons();
        ResizeForMode(mode);
        ScheduleRegionUpdate();
        RefreshValues();
    }

    private void OnStatusChanged(StatusMessage msg)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            UpdateStatus(msg);
            RefreshValues();
        });
    }

    private void UpdateStatus(StatusMessage msg)
    {
        var brush = msg.Tone switch
        {
            StatusTone.Success => BrSuccess,
            StatusTone.Info    => BrInfo,
            StatusTone.Warn    => BrWarn,
            _                  => BrIdle,
        };
        StatusDot.Fill = brush;
        StatusText.Text = msg.Text;
        StatusText.Foreground = brush;
    }

    private void SetEditMode(bool editing)
    {
        try
        {
            int ex = User32.GetWindowLong(_hwnd, GWL.EXSTYLE);
            if (editing) { ex &= ~WS_EX.NOACTIVATE; User32.SetWindowLong(_hwnd, GWL.EXSTYLE, ex); User32.SetForegroundWindow(_hwnd); }
            else         { ex |=  WS_EX.NOACTIVATE; User32.SetWindowLong(_hwnd, GWL.EXSTYLE, ex); }
        }
        catch (Exception e) { _log.LogWarning(e, "SetEditMode 失败"); }
    }

    private void BeginEdit(char key, UIElement displayView, TextBox editBox, Border capBorder)
    {
        string current = key == 'Q' ? (App.DateSvc?.CurrentDate ?? "") : App.StoreSvc.Get(key);

        // 编辑态视觉：键帽边框换 accent 色加粗
        var origBrush = capBorder.BorderBrush;
        var origThickness = capBorder.BorderThickness;
        capBorder.BorderBrush = BrAccent;
        capBorder.BorderThickness = new Thickness(1);

        displayView.Opacity = 0;  // 保留布局高度
        editBox.Text = current;
        editBox.Visibility = Visibility.Visible;
        SetEditMode(true);

        // VisualState 会反复把 DeleteButton 设回 Visible，必须每次 TextChanged/GotFocus 重新隐藏
        void OnTextChangedHide(object s, TextChangedEventArgs _) => HideTextBoxDeleteButton(editBox);
        void OnGotFocusHide(object s, RoutedEventArgs _)        => HideTextBoxDeleteButton(editBox);
        editBox.TextChanged += OnTextChangedHide;
        editBox.GotFocus    += OnGotFocusHide;

        // 延迟 Focus：让 SetForegroundWindow 消息先到达窗口；同时此时 TextBox 模板已应用，可隐藏内置 X 按钮
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            HideTextBoxDeleteButton(editBox);
            editBox.Focus(FocusState.Programmatic);
            editBox.SelectAll();
        });

        bool done = false;

        // 编辑态期间监听 RootGrid 点击：左键外击 = 提交，右键外击 = 取消
        void OnRootOutsideClick(object s, PointerRoutedEventArgs e)
        {
            if (done) return;
            var props = e.GetCurrentPoint(null).Properties;
            if (props.IsRightButtonPressed) { Cancel(); e.Handled = true; }
            else if (props.IsLeftButtonPressed) { Commit(); e.Handled = true; }
        }
        RootGrid.PointerPressed += OnRootOutsideClick;

        void EndEdit()
        {
            editBox.KeyDown -= OnKeyDown;
            editBox.LostFocus -= OnLostFocus;
            editBox.TextChanged -= OnTextChangedHide;
            editBox.GotFocus    -= OnGotFocusHide;
            RootGrid.PointerPressed -= OnRootOutsideClick;
            _activeEditCommit = null;
            _activeEditCancel = null;
            editBox.Visibility = Visibility.Collapsed;
            displayView.Opacity = 1;
            capBorder.BorderBrush = origBrush;
            capBorder.BorderThickness = origThickness;
            SetEditMode(false);
        }

        void Commit()
        {
            if (done) return;
            done = true;
            string newVal = editBox.Text.Trim();
            bool ok = true;
            string? warn = null;
            if (key == 'Q')
            {
                (ok, warn) = App.DateSvc!.TryBind(newVal);
            }
            else
            {
                App.StoreSvc.Set(key, newVal);
            }
            EndEdit();
            RefreshValues();
            if (ok)
            {
                App.StatusSvc?.Set(new StatusMessage
                {
                    Tone = StatusTone.Success,
                    Text = string.IsNullOrEmpty(newVal)
                        ? $"已清空 ALT+{key} 绑定"
                        : $"设置 ALT+{key} 为 \"{newVal}\" 成功",
                });
            }
            else
            {
                App.StatusSvc?.Set(new StatusMessage { Tone = StatusTone.Warn, Text = warn ?? "格式错误" });
            }
        }

        void Cancel() { if (done) return; done = true; EndEdit(); }

        _activeEditCommit = Commit;
        _activeEditCancel = Cancel;

        void OnKeyDown(object s, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Escape) { Cancel(); e.Handled = true; }
        }

        void OnLostFocus(object s, RoutedEventArgs e)
        {
            if (done) return;
            // 用户外击导致失焦时鼠标键通常仍处于按下状态：右键 → 取消，其他（含左键、Tab）→ 提交
            bool rightDown = (User32.GetAsyncKeyState(VK.RBUTTON) & 0x8000) != 0;
            if (rightDown) Cancel();
            else Commit();
        }

        editBox.KeyDown += OnKeyDown;
        editBox.LostFocus += OnLostFocus;
    }

    private void RefreshValues()
    {
        foreach (var (key, tb) in _kbValues)   SetValue(tb, key);
        foreach (var (key, tb) in _listValues) SetValue(tb, key);
    }

    private void SetValue(TextBlock tb, char key)
    {
        string val = App.StoreSvc.Get(key);
        if (string.IsNullOrEmpty(val))
        {
            tb.Text = "点击绑定";
            tb.FontFamily = new FontFamily("Segoe UI");
            tb.FontStyle  = Windows.UI.Text.FontStyle.Italic;
            tb.Foreground = BrFgMute;
        }
        else
        {
            tb.Text = val;
            tb.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            tb.FontStyle  = Windows.UI.Text.FontStyle.Normal;
            tb.Foreground = key == 'Q' ? BrAccent : BrFg;
        }
    }

    // ── 实时拖拽：CapturePointer + GetCursorPos + AppWindow.Move ─────
    private void OnDragAreaPressed(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(null).Properties;
        // 编辑态：拖拽区点击也作为「确认/取消」，禁用拖拽
        if (_activeEditCommit != null)
        {
            if (props.IsRightButtonPressed) _activeEditCancel?.Invoke();
            else _activeEditCommit?.Invoke();
            e.Handled = true;
            return;
        }
        // 右键不参与拖拽
        if (props.IsRightButtonPressed) return;
        if (sender is not UIElement el) return;
        if (!el.CapturePointer(e.Pointer)) return;
        User32.GetCursorPos(out _dragStartCursor);
        _dragStartWin = AppWindow.Position;
        _dragElement = el;
        _dragging = true;
        e.Handled = true;
    }

    private void OnDragAreaMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        if (!User32.GetCursorPos(out var cur)) return;
        AppWindow.Move(new PointInt32(
            _dragStartWin.X + (cur.X - _dragStartCursor.X),
            _dragStartWin.Y + (cur.Y - _dragStartCursor.Y)));
        e.Handled = true;
    }

    private void OnDragAreaReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(e);
    }

    private void OnDragAreaCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(e);
    }

    private void EndDrag(PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _dragElement?.ReleasePointerCapture(e.Pointer);
        _dragElement = null;
        e.Handled = true;
    }

    private void OnCollapse(object sender, PointerRoutedEventArgs e)
        => SwitchMode(ViewMode.Capsule);

    private void OnSwitchToList(object sender, PointerRoutedEventArgs e)
        => SwitchMode(ViewMode.List);

    private void OnSwitchToKB(object sender, PointerRoutedEventArgs e)
        => SwitchMode(ViewMode.Keyboard);

    private static TextBlock Txt(string text, double size, SolidColorBrush fg, Windows.UI.Text.FontWeight weight)
        => new()
        {
            Text = text, FontSize = size, Foreground = fg, FontWeight = weight,
            IsHitTestVisible = false,
        };
}
