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
    private const int WIN_W = 305;
    private const int BTN_SZ = 22;
    private const double SB_RADIUS = 13;
    private const double ROW_RADIUS = 8;

    // Segoe Fluent Icons / MDL2 Assets 字形（保持视觉一致）
    private const string ICON_KEYBOARD = ""; // KeyboardClassic
    private const string ICON_LIST     = ""; // List
    private const string ICON_UP       = ""; // ChevronUp
    private const string ICON_DOWN     = ""; // ChevronDown
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static readonly char[] Row1 = { '1', '2', '3', '4', '5', '6' };
    private static readonly char[] Row2 = { 'Q', 'W', 'E', 'R' };
    private static readonly char[] Row3 = { 'A', 'S', 'D', 'F' };
    private static readonly char[] ListLeft  = { '1', '2', '3', '4', '5', '6' };
    private static readonly char[] ListRight = { 'Q', 'W', 'E', 'R', 'A', 'S', 'D', 'F' };

    private static readonly SolidColorBrush BrCapBg     = new(Color.FromArgb(0xE0, 0x2C, 0x2C, 0x32));
    private static readonly SolidColorBrush BrCapHover  = new(Color.FromArgb(0xF0, 0x40, 0x40, 0x48));
    private static readonly SolidColorBrush BrCapBorder = new(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush BrFg        = new(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush BrFgMute    = new(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush BrAccent    = new(Color.FromArgb(0xFF, 0x4C, 0xC2, 0xFF));
    private static readonly SolidColorBrush BrSuccess   = new(Color.FromArgb(0xFF, 0x5D, 0xD2, 0x8B));
    private static readonly SolidColorBrush BrInfo      = new(Color.FromArgb(0xFF, 0x79, 0xC5, 0xFF));
    private static readonly SolidColorBrush BrWarn      = new(Color.FromArgb(0xFF, 0xFF, 0xC4, 0x6A));
    private static readonly SolidColorBrush BrIdle      = new(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush BrTransp    = new(Colors.Transparent);
    private static readonly SolidColorBrush BrHoverBg   = new(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ConfigureWindow();
        PopulateUI();
        App.StatusSvc.StatusChanged += OnStatusChanged;
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
            ScheduleRegionUpdate();
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
        // 高度足够即可，区域裁剪决定可见形状
        int h = mode switch
        {
            ViewMode.Capsule  => 36,
            ViewMode.Keyboard => 150,
            ViewMode.List     => 220,
            _ => 36,
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
            Width = 46, Height = 28,
            CornerRadius = new CornerRadius(5),
            Background = BrCapBg,
            BorderBrush = BrCapBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 2, 3, 2),
        };
        border.PointerEntered += (s, _) => ((Border)s!).Background = BrCapHover;
        border.PointerExited  += (s, _) => ((Border)s!).Background = BrCapBg;
        border.PointerPressed += (_, e) => { App.OnKeyCapClicked(key); e.Handled = true; };

        var stack = new StackPanel { Spacing = 0 };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        label.Children.Add(Txt("Alt", 7, BrFgMute, FontWeights.Medium));
        label.Children.Add(Txt("+",   7, BrFgMute, FontWeights.Medium));
        label.Children.Add(Txt(key.ToString(), 7.5, BrFg, FontWeights.Bold));
        stack.Children.Add(label);

        var val = new TextBlock
        {
            FontSize = 7.5,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = BrFg,
            MaxLines = 1,
            IsHitTestVisible = false,
        };
        stack.Children.Add(val);

        border.Child = stack;
        reg[key] = val;
        return border;
    }

    private Border MakeListRow(char key, Dictionary<char, TextBlock> reg)
    {
        var border = new Border
        {
            Padding = new Thickness(7, 3, 7, 3),
            Background = BrTransp,
        };
        border.PointerEntered += (s, _) => ((Border)s!).Background = BrHoverBg;
        border.PointerExited  += (s, _) => ((Border)s!).Background = BrTransp;
        border.PointerPressed += (_, e) => { App.OnKeyCapClicked(key); e.Handled = true; };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyTb = new TextBlock
        {
            FontSize = 8.5, Foreground = BrFgMute,
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
            FontSize = 8.5,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = BrFg,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLines = 1,
            IsHitTestVisible = false,
        };
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);

        border.Child = grid;
        reg[key] = val;
        return border;
    }

    // 三种模式都是 2 个固定尺寸按钮，用 Segoe Fluent 图标确保字形一致
    private void RebuildActionButtons()
    {
        ActionButtons.Children.Clear();
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
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
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
