using Microsoft.Extensions.Logging;
using QuickInputAssistant.Models;
using QuickInputAssistant.PInvoke;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickInputAssistant.Services;

/// <summary>
/// 核心业务编排：热键触发 → 黑名单 → 路由到 DateKey 或 标准流程。
/// </summary>
internal sealed class CoreService : IDisposable
{
    private readonly ILogger<CoreService> _log;
    private readonly HotkeyService   _hotkey;
    private readonly ClipboardService _clipboard;
    private readonly BindingStore     _store;
    private readonly InputService     _input;
    private readonly DateKeyService   _date;
    private readonly BlacklistService _blacklist;
    private readonly StatusService    _status;

    public CoreService(
        ILogger<CoreService> logger,
        HotkeyService hotkey,
        ClipboardService clipboard,
        BindingStore store,
        InputService input,
        DateKeyService date,
        BlacklistService blacklist,
        StatusService status)
    {
        _log = logger;
        _hotkey    = hotkey;
        _clipboard = clipboard;
        _store     = store;
        _input     = input;
        _date      = date;
        _blacklist = blacklist;
        _status    = status;

        _hotkey.HotkeyTriggered += OnHotkeyTriggered;
    }

    // ── 核心流程 ──────────────────────────────────────────────────────

    private void OnHotkeyTriggered(char key)
    {
        // 1. 黑名单检查
        string? blocked = _blacklist.GetBlockedProcessName();
        if (blocked is not null)
        {
            _status.Set(StatusMessage.Paused(blocked));
            return;
        }

        // 2. 路由
        if (key == 'Q')
            HandleDateKey();
        else
            HandleStandardKey(key);
    }

    private void HandleDateKey()
    {
        // 双击窗口内（第二次按 Alt+Q）→ 直接走日期状态机做 +1，跳过剪贴板探测
        if (_date.IsInDoubleClickWindow)
        {
            InvokeDateHandle();
            return;
        }

        // 快速路径：Edit 控件 + 无选区 → 直接 _date.Handle()
        if (TryDirectDateOutput())
            return;

        // 同步剪贴板探测（不启动 Task）——保证 ProcessQueueAsync 串行处理每次 Alt+Q，
        // 避免多个 Task 并发导致状态错乱和多次输出
        var snap = _clipboard.Backup();
        _clipboard.SendCtrlC();
        string? newText = _clipboard.PollForChangeAsync(snap, timeoutMs: 80, intervalMs: 10)
            .GetAwaiter().GetResult();
        _clipboard.Restore(snap);

        if (newText is not null)
        {
            var (ok, warn) = _date.TryBind(newText.Trim());
            if (ok)
            {
                _status.Set(new StatusMessage { Tone = StatusTone.Success, Text = $"✅ ALT+Q 日期已绑定为 {_date.CurrentDate}" });
                return;
            }
            _log.LogInformation("ALT+Q 选中文本格式非法（{Warn}），回退到日期状态机", warn);
        }
        InvokeDateHandle();
    }

    private void InvokeDateHandle()
    {
        var (ok, warn) = _date.Handle();
        if (!ok && warn is not null)
            _status.Set(new StatusMessage { Tone = StatusTone.Warn, Text = $"⚠️ {warn}" });
        else
            _status.Set(new StatusMessage { Tone = StatusTone.Info, Text = $"✅ ALT+Q 日期: {_date.CurrentDate}" });
    }

    /// <summary>
    /// Alt+Q 快速路径：Win32 Edit 控件且无选区时直接走日期状态机，跳过 80ms 剪贴板探测。
    /// </summary>
    private bool TryDirectDateOutput()
    {
        try
        {
            IntPtr fg = User32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint tid = User32.GetWindowThreadProcessId(fg, out _);
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (!User32.GetGUIThreadInfo(tid, ref gti) || gti.hwndFocus == IntPtr.Zero)
                return false;
            var cls = new StringBuilder(64);
            if (User32.GetClassName(gti.hwndFocus, cls, 64) == 0) return false;
            if (!cls.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase)) return false;

            IntPtr sel = User32.SendMessage(gti.hwndFocus, 0x00B0, IntPtr.Zero, IntPtr.Zero);
            uint raw = (uint)(sel.ToInt64() & 0xFFFF_FFFF);
            int selStart = (int)(raw & 0xFFFF);
            int selEnd   = (int)(raw >> 16);
            if (selStart != selEnd) return false; // 有选区 → 走绑定路径

            InvokeDateHandle();
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "TryDirectDateOutput 异常");
            return false;
        }
    }

    private void HandleStandardKey(char key)
    {
        _log.LogInformation("热键触发: ALT+{Key}", key);

        // 快速路径：Win32 Edit 控件且无选区 → 跳过 Ctrl+C 和轮询，直接输出
        if (TryDirectOutput(key))
            return;

        // 通用路径：备份剪贴板 → Ctrl+C → 轮询判断绑定/输出
        var snap = _clipboard.Backup();
        _log.LogInformation("ALT+{Key} 备份完成, TextBefore=\"{Text}\"", key, snap.TextBefore);
        _clipboard.SendCtrlC();

        Task.Run(async () =>
        {
            // 80ms 超时（较旧的 150ms），Ctrl+C 在现代系统上 <50ms 即可完成
            string? newText = await _clipboard.PollForChangeAsync(snap, timeoutMs: 80, intervalMs: 10);
            _clipboard.Restore(snap);

            if (newText is not null)
            {
                _log.LogInformation("ALT+{Key} 检测到选中: \"{Val}\"", key, newText);
                _store.Set(key, newText);
                string displayVal = newText.Length > 20 ? newText[..20] + "…" : newText;
                _status.Set(new StatusMessage
                {
                    Tone = StatusTone.Success,
                    Text = $"✅ 设置 ALT+{key} 为 \"{displayVal}\" 成功！"
                });
            }
            else
            {
                _log.LogInformation("ALT+{Key} 无选中，进入输出模式", key);
                string val = _store.Get(key);
                if (!string.IsNullOrEmpty(val))
                {
                    _input.TypeString(val);
                    _status.Set(new StatusMessage { Tone = StatusTone.Info, Text = $"⚡ 已输出 ALT+{key} 内容" });
                }
                else
                {
                    _status.Set(new StatusMessage { Tone = StatusTone.Info, Text = $"💡 ALT+{key} 尚未绑定" });
                }
            }
        });
    }

    /// <summary>
    /// 快速路径：若前台焦点控件是 Win32 Edit 且无文本选区，直接输出绑定内容，
    /// 完全跳过 Ctrl+C + 剪贴板轮询，消除 80ms 等待。
    /// 返回 true 表示已处理（调用方无需再走通用路径）。
    /// </summary>
    private bool TryDirectOutput(char key)
    {
        try
        {
            // 获取前台线程焦点控件
            IntPtr fg = User32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            uint tid = User32.GetWindowThreadProcessId(fg, out _);
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (!User32.GetGUIThreadInfo(tid, ref gti) || gti.hwndFocus == IntPtr.Zero)
                return false;

            // 只对 Win32 "Edit" 类控件做快速判断
            var cls = new StringBuilder(64);
            if (User32.GetClassName(gti.hwndFocus, cls, 64) == 0) return false;
            if (!cls.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase)) return false;

            // EM_GETSEL (0x00B0)：返回值低 16 位 = selStart，高 16 位 = selEnd
            IntPtr sel = User32.SendMessage(gti.hwndFocus, 0x00B0, IntPtr.Zero, IntPtr.Zero);
            uint raw = (uint)(sel.ToInt64() & 0xFFFF_FFFF);
            int selStart = (int)(raw & 0xFFFF);
            int selEnd   = (int)(raw >> 16);

            if (selStart != selEnd) return false; // 有选区 → 走绑定路径

            // 无选区 → 直接输出
            string val = _store.Get(key);
            if (!string.IsNullOrEmpty(val))
            {
                _log.LogInformation("ALT+{Key} Edit 快速输出", key);
                _input.TypeString(val);
                _status.Set(new StatusMessage { Tone = StatusTone.Info, Text = $"⚡ 已输出 ALT+{key} 内容" });
            }
            else
            {
                _status.Set(new StatusMessage { Tone = StatusTone.Info, Text = $"💡 ALT+{key} 尚未绑定" });
            }
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "TryDirectOutput 异常");
            return false;
        }
    }

    public void Dispose()
    {
        _hotkey.HotkeyTriggered -= OnHotkeyTriggered;
    }
}
