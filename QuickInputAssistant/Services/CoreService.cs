using Microsoft.Extensions.Logging;
using QuickInputAssistant.Models;

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
        var (ok, warn) = _date.Handle();
        if (!ok && warn is not null)
            _status.Set(new StatusMessage { Tone = StatusTone.Warn, Text = $"⚠️ {warn}" });
        else
            _status.Set(new StatusMessage { Tone = StatusTone.Info, Text = $"✅ ALT+Q 日期: {_date.CurrentDate}" });
    }

    private void HandleStandardKey(char key)
    {
        // 无痕借用剪贴板
        var snap = _clipboard.Backup();
        _clipboard.SendCtrlC();

        // 轮询（在线程池上等待，不阻塞 UI）
        Task.Run(async () =>
        {
            string? newText = await _clipboard.PollForChangeAsync(snap);
            _clipboard.Restore(snap);

            if (newText is not null)
            {
                // 绑定模式
                _store.Set(key, newText);
                string displayVal = newText.Length > 20 ? newText[..20] + "…" : newText;
                _status.Set(new StatusMessage
                {
                    Tone = StatusTone.Success,
                    Text = $"✅ 设置 ALT+{key} 为 \"{displayVal}\" 成功！"
                });
                _log.LogInformation("ALT+{Key} 绑定: {Val}", key, newText);
            }
            else
            {
                // 输出模式
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

    public void Dispose()
    {
        _hotkey.HotkeyTriggered -= OnHotkeyTriggered;
    }
}
