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
    /// Alt+Q 快速路径：已知控件类（Edit / Scintilla）且无选区时直接走日期状态机，跳过 80ms 剪贴板探测。
    /// </summary>
    private bool TryDirectDateOutput()
    {
        bool? hasSel = ProbeKnownSelection();
        if (hasSel == null) return false;          // 未知控件类 → 走通用路径
        if (hasSel.Value)   return false;          // 有选区 → 走绑定路径
        InvokeDateHandle();
        return true;
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

            // Notepad++/VSCode 等编辑器在无选区时 Ctrl+C 会复制当前行（含换行），
            // 剪贴板"变化"但 trim 后为空白 → 视为无选区，避免把空白当成绑定值覆盖
            if (newText is not null && string.IsNullOrWhiteSpace(newText))
            {
                _log.LogInformation("ALT+{Key} 剪贴板变化但内容为空白，按无选区处理", key);
                newText = null;
            }

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
    /// 快速路径：若前台焦点控件类已知（Edit / Scintilla），直接读取选区状态：
    ///   有选区 → 走绑定路径；无选区 → 直接输出绑定。
    /// 完全跳过 Ctrl+C + 剪贴板轮询，消除 80ms 等待，且避免 Notepad++ 等编辑器
    /// 无选区 Ctrl+C 复制当前行导致的误绑定。
    /// 返回 true 表示已处理。
    /// </summary>
    private bool TryDirectOutput(char key)
    {
        bool? hasSel = ProbeKnownSelection();
        if (hasSel == null) return false;     // 未知控件类 → 走通用路径
        if (hasSel.Value)   return false;     // 有选区 → 走绑定路径

        // 无选区 → 直接输出
        string val = _store.Get(key);
        if (!string.IsNullOrEmpty(val))
        {
            _log.LogInformation("ALT+{Key} 已知控件 快速输出", key);
            _input.TypeString(val);
            _status.Set(new StatusMessage { Tone = StatusTone.Info, Text = $"⚡ 已输出 ALT+{key} 内容" });
        }
        else
        {
            _status.Set(new StatusMessage { Tone = StatusTone.Info, Text = $"💡 ALT+{key} 尚未绑定" });
        }
        return true;
    }

    /// <summary>
    /// 探测前台焦点控件的选区状态：
    ///   null = 未知/不可探测控件类（如 Chromium、UWP），调用方应回退到剪贴板探测
    ///   true = 有选区
    ///   false = 无选区
    /// </summary>
    private bool? ProbeKnownSelection()
    {
        try
        {
            IntPtr fg = User32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return null;
            uint tid = User32.GetWindowThreadProcessId(fg, out _);
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (!User32.GetGUIThreadInfo(tid, ref gti) || gti.hwndFocus == IntPtr.Zero)
                return null;

            var cls = new StringBuilder(64);
            if (User32.GetClassName(gti.hwndFocus, cls, 64) == 0) return null;
            string className = cls.ToString();

            if (className.Equals("Edit", StringComparison.OrdinalIgnoreCase))
            {
                // EM_GETSEL = 0x00B0：低 16 = selStart，高 16 = selEnd
                IntPtr sel = User32.SendMessage(gti.hwndFocus, 0x00B0, IntPtr.Zero, IntPtr.Zero);
                uint raw = (uint)(sel.ToInt64() & 0xFFFF_FFFF);
                return (raw & 0xFFFF) != (raw >> 16);
            }
            // Scintilla（Notepad++、SciTE 等）：SCI_GETSELECTIONSTART/END
            if (className.Contains("Scintilla", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr start = User32.SendMessage(gti.hwndFocus, 2143, IntPtr.Zero, IntPtr.Zero);
                IntPtr end   = User32.SendMessage(gti.hwndFocus, 2144, IntPtr.Zero, IntPtr.Zero);
                return start.ToInt64() != end.ToInt64();
            }
            return null;  // 未知控件类 → 走通用剪贴板探测
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ProbeKnownSelection 异常");
            return null;
        }
    }

    public void Dispose()
    {
        _hotkey.HotkeyTriggered -= OnHotkeyTriggered;
    }
}
