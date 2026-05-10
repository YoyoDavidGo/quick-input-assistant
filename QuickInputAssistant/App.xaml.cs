using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using QuickInputAssistant.Services;
using Serilog;
using System.IO;
using System.Threading;

namespace QuickInputAssistant;

public partial class App : Application
{
    private static Mutex? _mutex;
    private MainWindow? _mainWindow;
    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    // 服务单例（生命周期跟随应用）
    public static InputService    InputSvc    { get; private set; } = null!;
    public static BindingStore    StoreSvc    { get; private set; } = null!;
    public static StatusService   StatusSvc   { get; private set; } = null!;
    public static DateKeyService  DateSvc     { get; private set; } = null!;
    private static HotkeyService?   _hotkeySvc;
    private static CoreService?     _coreSvc;

    public App()
    {
        InitializeComponent();
        SetupLogging();
        SetupExceptionHandlers();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Information("OnLaunched 开始");

        if (!EnsureSingleInstance())
        {
            Log.Information("已有实例运行，退出");
            Current.Exit();
            return;
        }
        Log.Information("单实例检查通过");

        try { InitServices(); Log.Information("服务初始化完成"); }
        catch (Exception ex) { Log.Fatal(ex, "InitServices 失败"); throw; }

        try { _mainWindow = new MainWindow(); Log.Information("MainWindow 创建成功"); }
        catch (Exception ex) { Log.Fatal(ex, "MainWindow 创建失败"); throw; }

        try { _mainWindow.Activate(); Log.Information("MainWindow 激活成功"); }
        catch (Exception ex) { Log.Fatal(ex, "MainWindow 激活失败"); throw; }
    }

    private static void InitServices()
    {
        InputSvc  = new InputService(LoggerFactory.CreateLogger<InputService>());
        StoreSvc  = new BindingStore(LoggerFactory.CreateLogger<BindingStore>());
        StatusSvc = new StatusService();
        DateSvc   = new DateKeyService(LoggerFactory.CreateLogger<DateKeyService>(), StoreSvc, InputSvc);

        var clipboard  = new ClipboardService(LoggerFactory.CreateLogger<ClipboardService>(), InputSvc);
        var blacklist  = new BlacklistService(LoggerFactory.CreateLogger<BlacklistService>());
        _hotkeySvc     = new HotkeyService(LoggerFactory.CreateLogger<HotkeyService>());

        _coreSvc = new CoreService(
            LoggerFactory.CreateLogger<CoreService>(),
            _hotkeySvc, clipboard, StoreSvc, InputSvc, DateSvc, blacklist, StatusSvc);
    }

    private static bool EnsureSingleInstance()
    {
        _mutex = new Mutex(true, "Global\\QuickInputAssistant_v2", out bool created);
        if (!created)
        {
            _mutex = null;
            return false;
        }
        return true;
    }

    private static void SetupLogging()
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickInputAssistant", "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(logDir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                retainedFileCountLimit: 7,
                buffered: false)          // 立即刷盘，便于调试崩溃
            .CreateLogger();

        LoggerFactory = new LoggerFactory().AddSerilog(Log.Logger);
        Log.Information("QuickInputAssistant 启动");
    }

    private static void SetupExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "未捕获异常 (AppDomain)");

        Current.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.Exception, "未捕获异常 (Application)");
            e.Handled = true;
        };
    }

    public static void Cleanup()
    {
        _coreSvc?.Dispose();
        _hotkeySvc?.Dispose();
        StoreSvc?.Flush();
        StoreSvc?.Dispose();
        StatusSvc?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Log.Information("QuickInputAssistant 退出");
        Log.CloseAndFlush();
    }
}
