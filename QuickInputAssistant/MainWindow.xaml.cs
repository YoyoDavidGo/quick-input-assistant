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

    // Key layout
    private static readonly char[] Row1 = { '1', '2', '3', '4', '5', '6' };
    private static readonly char[] Row2 = { 'Q', 'W', 'E', 'R' };
    private static readonly char[] Row3 = { 'A', 'S', 'D', 'F' };
    private static readonly char[] ListLeft  = { '1', '2', '3', '4', '5', '6' };
    private static readonly char[] ListRight = { 'Q', 'W', 'E', 'R', 'A', 'S', 'D', 'F' };

    // Brushes (reused)
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

    // ── Window Configuration ─────────────────────────────────────────────

    private void ConfigureWindow()
    {
        // Presenter
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

        // 去标题栏 + WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
        try
        {
            int style = User32.GetWindowLong(_hwnd, GWL.STYLE);
            style &= ~(WS.CAPTION | WS.THICKFRAME | WS.SYSMENU);
            User32.SetWindowLong(_hwnd, GWL.STYLE, style);

            int exStyle = User32.GetWindowLong(_hwnd, GWL.EXSTYLE);
            exStyle |= WS_EX.NOACTIVATE | WS_EX.TOOLWINDOW;
            User32.SetWindowLong(_hwnd, GWL.EXSTYLE, exStyle);
        }
        catch (Exception ex) { _log.LogWarning(ex, "设置窗口样式失败"); }

        // Initial size
        AppWindow.Resize(new SizeInt32(300, 150));

        // Re-size and position after layout is ready
        RootGrid.Loaded += (_, _) =>
        {
            ResizeForMode(_mode);
            PositionWindow();
        };

        Closed += (_, _) => App.Cleanup();
        _log.LogInformation("MainWindow 初始化完成 (三态窗口)");
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
        catch (Exception ex)
        {
            _log.LogWarning(ex, "定位窗口失败");
        }
    }

    private void ResizeForMode(ViewMode mode)
    {
        double scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        var (w, h) = mode switch
        {
            ViewMode.Capsule  => (236, 40),
            ViewMode.Keyboard => (310, 165),
            ViewMode.List     => (260, 245),
            _ => (310, 165),
        };
        AppWindow.Resize(new SizeInt32((int)(w * scale), (int)(h * scale)));
    }

    // ── UI Population ────────────────────────────────────────────────────

    private void PopulateUI()
    {
        // Keyboard key caps
        foreach (char k in Row1) KBRow1.Children.Add(MakeKeyCap(k, _kbValues));
        foreach (char k in Row2) KBRow2.Children.Add(MakeKeyCap(k, _kbValues));
        foreach (char k in Row3) KBRow3.Children.Add(MakeKeyCap(k, _kbValues));

        // List rows
        foreach (char k in ListLeft)  ListCol1.Children.Add(MakeListRow(k, _listValues));
        foreach (char k in ListRight) ListCol2.Children.Add(MakeListRow(k, _listValues));

        // Action buttons
        RebuildActionButtons();

        // Fill initial values
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

        var stack = new StackPanel { Spacing = 0 };

        // Label: Alt+K
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        label.Children.Add(Txt("Alt", 7, BrFgMute, FontWeights.Medium));
        label.Children.Add(Txt("+",   7, BrFgMute, FontWeights.Medium));
        label.Children.Add(Txt(key.ToString(), 7.5, BrFg, FontWeights.Bold));
        stack.Children.Add(label);

        // Value
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

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Key label: Alt+K
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

        // Value
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

    private void RebuildActionButtons()
    {
        ActionButtons.Children.Clear();
        if (_mode == ViewMode.Keyboard)
        {
            ActionButtons.Children.Add(MakeActionBtn("☰", OnSwitchToList));
            ActionButtons.Children.Add(MakeActionBtn("︿", OnCollapse));
        }
        else if (_mode == ViewMode.List)
        {
            ActionButtons.Children.Add(MakeActionBtn("⌨", OnSwitchToKB));
            ActionButtons.Children.Add(MakeActionBtn("︿", OnCollapse));
        }
    }

    private Border MakeActionBtn(string icon, PointerEventHandler handler)
    {
        var b = new Border
        {
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(3),
            Background = BrTransp,
        };
        b.PointerPressed += handler;
        b.PointerEntered += (s, _) => ((Border)s!).Background = BrHoverBg;
        b.PointerExited  += (s, _) => ((Border)s!).Background = BrTransp;
        b.Child = new TextBlock
        {
            Text = icon, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false,
        };
        return b;
    }

    // ── Mode Switching ───────────────────────────────────────────────────

    private void SwitchMode(ViewMode mode)
    {
        _mode = mode;
        CapsuleView.Visibility  = mode == ViewMode.Capsule ? Visibility.Visible : Visibility.Collapsed;
        ExpandedView.Visibility = mode != ViewMode.Capsule ? Visibility.Visible : Visibility.Collapsed;
        KBContent.Visibility    = mode == ViewMode.Keyboard ? Visibility.Visible : Visibility.Collapsed;
        ListContent.Visibility  = mode == ViewMode.List     ? Visibility.Visible : Visibility.Collapsed;
        RebuildActionButtons();
        ResizeForMode(mode);
        RefreshValues();
    }

    // ── Status & Value Updates ───────────────────────────────────────────

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

        CapsuleDot.Fill = brush;
        CapsuleStatusText.Text = msg.Text;
        CapsuleStatusText.Foreground = brush;
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

    // ── Event Handlers ───────────────────────────────────────────────────

    private void OnDragAreaPressed(object sender, PointerRoutedEventArgs e)
    {
        User32.ReleaseCapture();
        User32.SendMessage(_hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
    }

    private void OnExpandClick(object sender, PointerRoutedEventArgs e)
        => SwitchMode(ViewMode.Keyboard);

    private void OnCollapse(object sender, PointerRoutedEventArgs e)
        => SwitchMode(ViewMode.Capsule);

    private void OnSwitchToList(object sender, PointerRoutedEventArgs e)
        => SwitchMode(ViewMode.List);

    private void OnSwitchToKB(object sender, PointerRoutedEventArgs e)
        => SwitchMode(ViewMode.Keyboard);

    // ── Helpers ──────────────────────────────────────────────────────────

    private static TextBlock Txt(string text, double size, SolidColorBrush fg, Windows.UI.Text.FontWeight weight)
        => new()
        {
            Text = text, FontSize = size, Foreground = fg, FontWeight = weight,
            IsHitTestVisible = false,
        };
}
