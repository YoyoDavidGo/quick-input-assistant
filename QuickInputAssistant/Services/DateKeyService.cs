using Microsoft.Extensions.Logging;
using QuickInputAssistant.PInvoke;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace QuickInputAssistant.Services;

/// <summary>
/// Alt+Q 状态机：单击输出日期，双击（300ms内）撤销旧输出并日期+1。
/// 格式 YY/MM/DD，初始值为昨天。
/// </summary>
public sealed class DateKeyService
{
    private static readonly Regex DateRegex = new(@"^\d{2}/\d{2}/\d{2}$", RegexOptions.Compiled);

    private readonly ILogger<DateKeyService> _log;
    private readonly BindingStore _store;
    private readonly InputService _input;

    private enum State { Idle, WaitDouble }
    private State _state = State.Idle;
    private System.Threading.Timer? _doubleTimer;
    private int _lastOutputLen;
    private readonly object _stateLock = new();

    public DateKeyService(ILogger<DateKeyService> logger, BindingStore store, InputService input)
    {
        _log = logger;
        _store = store;
        _input = input;

        // 初始值：若未绑定则设昨天
        if (string.IsNullOrEmpty(_store.Get('Q')))
        {
            string yesterday = FormatDate(DateTime.Today.AddDays(-1));
            _store.Set('Q', yesterday);
            _log.LogInformation("Alt+Q 初始日期: {Date}", yesterday);
        }
    }

    // ── 公开接口 ──────────────────────────────────────────────────────

    /// <summary>返回当前绑定日期字符串（供 UI 显示）。</summary>
    public string CurrentDate => _store.Get('Q');

    /// <summary>
    /// 热键触发入口。
    /// 返回 (ok, warningMsg)：ok=false 表示格式非法且已报警。
    /// </summary>
    public (bool ok, string? warning) Handle()
    {
        lock (_stateLock)
        {
            switch (_state)
            {
                case State.Idle:
                    return HandleFirst();
                case State.WaitDouble:
                    return HandleSecond();
                default:
                    return (true, null);
            }
        }
    }

    /// <summary>尝试绑定新日期（来自剪贴板复制）。格式非法则拒绝。</summary>
    public (bool ok, string? warning) TryBind(string value)
    {
        if (!DateRegex.IsMatch(value))
            return (false, $"ALT+Q 仅支持 YY/MM/DD 格式，当前内容: \"{value}\"");

        if (!TryParseDate(value, out _))
            return (false, $"ALT+Q 日期非法: \"{value}\"");

        _store.Set('Q', value);
        _log.LogInformation("Alt+Q 绑定: {Date}", value);
        return (true, null);
    }

    /// <summary>UI 微调器调用，直接步进 delta 天（±1），返回新日期字符串。</summary>
    public string Step(int delta)
    {
        string next = ComputeNext(_store.Get('Q'), delta);
        _store.Set('Q', next);
        return next;
    }

    // ── 状态机实现 ────────────────────────────────────────────────────

    private (bool ok, string? warning) HandleFirst()
    {
        string date = _store.Get('Q');

        bool hasFocus = HasEditableFocus();
        if (hasFocus)
        {
            _input.TypeString(date);
            _lastOutputLen = date.Length;
        }

        // 启动 300ms 双击等待
        _state = State.WaitDouble;
        _doubleTimer = new System.Threading.Timer(_ =>
        {
            lock (_stateLock)
            {
                _state = State.Idle;
                _doubleTimer?.Dispose();
                _doubleTimer = null;
            }
        }, null, 300, Timeout.Infinite);

        return (true, null);
    }

    private (bool ok, string? warning) HandleSecond()
    {
        // 取消双击定时器
        _doubleTimer?.Dispose();
        _doubleTimer = null;
        _state = State.Idle;

        // 撤销第一次输出
        if (HasEditableFocus() && _lastOutputLen > 0)
            _input.SendBackspaces(_lastOutputLen);

        // 日期 +1
        string newDate = ComputeNext(_store.Get('Q'), +1);
        _store.Set('Q', newDate);

        // 输出新日期
        if (HasEditableFocus())
            _input.TypeString(newDate);

        _log.LogInformation("Alt+Q 日期顺延至: {Date}", newDate);
        return (true, null);
    }

    // ── 工具方法 ──────────────────────────────────────────────────────

    private static string ComputeNext(string current, int delta)
    {
        if (!TryParseDate(current, out DateTime d))
            d = DateTime.Today;
        return FormatDate(d.AddDays(delta));
    }

    private static string FormatDate(DateTime d)
    {
        int yy = d.Year % 100;
        return $"{yy:D2}/{d.Month:D2}/{d.Day:D2}";
    }

    private static bool TryParseDate(string s, out DateTime result)
    {
        result = default;
        if (!DateRegex.IsMatch(s)) return false;
        int yy = int.Parse(s[..2]);
        int mm = int.Parse(s[3..5]);
        int dd = int.Parse(s[6..]);
        try { result = new DateTime(2000 + yy, mm, dd); return true; }
        catch { return false; }
    }

    private bool HasEditableFocus()
    {
        IntPtr fg = User32.GetForegroundWindow();
        uint tid = User32.GetWindowThreadProcessId(fg, out _);
        var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!User32.GetGUIThreadInfo(tid, ref gti))
        {
            _log.LogWarning("GetGUIThreadInfo 失败, tid={Tid}", tid);
            return false;
        }
        _log.LogDebug("GUIThreadInfo: hwndFocus={Focus}, hwndCaret={Caret}",
            gti.hwndFocus, gti.hwndCaret);
        // hwndCaret 在现代应用(UWP/WinUI Notepad 等)中不可靠，改用 hwndFocus
        return gti.hwndFocus != IntPtr.Zero;
    }
}
