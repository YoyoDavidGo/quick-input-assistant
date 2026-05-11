using Microsoft.Extensions.Logging;
using QuickInputAssistant.PInvoke;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace QuickInputAssistant.Services;

/// <summary>
/// 低层键盘钩子，拦截 14 个 Alt 组合键，投递到工作队列，钩子回调 &lt;1ms。
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    // 14 个目标虚拟键码
    private static readonly HashSet<uint> TargetVKs = new()
    {
        (uint)VK.VK_1, (uint)VK.VK_2, (uint)VK.VK_3,
        (uint)VK.VK_4, (uint)VK.VK_5, (uint)VK.VK_6,
        (uint)VK.VK_Q, (uint)VK.VK_W, (uint)VK.VK_E, (uint)VK.VK_R,
        (uint)VK.VK_A, (uint)VK.VK_S, (uint)VK.VK_D, (uint)VK.VK_F,
    };

    // VK → 字符映射
    private static readonly Dictionary<uint, char> VkToChar = new()
    {
        { (uint)VK.VK_1,'1'}, { (uint)VK.VK_2,'2'}, { (uint)VK.VK_3,'3'},
        { (uint)VK.VK_4,'4'}, { (uint)VK.VK_5,'5'}, { (uint)VK.VK_6,'6'},
        { (uint)VK.VK_Q,'Q'}, { (uint)VK.VK_W,'W'}, { (uint)VK.VK_E,'E'},
        { (uint)VK.VK_R,'R'}, { (uint)VK.VK_A,'A'}, { (uint)VK.VK_S,'S'},
        { (uint)VK.VK_D,'D'}, { (uint)VK.VK_F,'F'},
    };

    public event Action<char>? HotkeyTriggered;

    private readonly ILogger<HotkeyService> _log;
    private readonly Channel<char> _queue;
    private readonly CancellationTokenSource _cts = new();
    private IntPtr _hook;
    private User32.HookProc? _hookProc; // 防止 GC 回收委托

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _log = logger;
        _queue = Channel.CreateBounded<char>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
        });

        InstallHook();
        _ = Task.Run(ProcessQueueAsync);
    }

    private void InstallHook()
    {
        _hookProc = LowLevelKeyboardProc;
        IntPtr hMod = User32.GetModuleHandle(null);
        _hook = User32.SetWindowsHookEx(WH.KEYBOARD_LL, _hookProc, hMod, 0);
        if (_hook == IntPtr.Zero)
            _log.LogError("SetWindowsHookEx 失败: {Err}", Marshal.GetLastWin32Error());
        else
            _log.LogInformation("键盘钩子安装成功");
    }

    // 物理 Alt 状态自维护 + 新鲜度判断：
    // - _altPhysicallyDown 跟踪物理 Alt 事件（非注入）
    // - _altActiveAtUtc 在 Alt down 和每次拦截热键时刷新
    // - 新鲜窗口 5 秒：超过 5 秒未活动则视为 Alt 不再有效（防"打字母 a 误触发"）
    //   连续按 Alt+热键时每次都刷新，长按 Alt 操作期间不会失效
    private bool _altPhysicallyDown;
    private DateTime _altActiveAtUtc;
    private static readonly TimeSpan AltFreshWindow = TimeSpan.FromSeconds(5);

    private IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return User32.CallNextHookEx(_hook, nCode, wParam, lParam);

        var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        // 忽略我们自己 SendInput 注入的事件
        const uint LLKHF_INJECTED = 0x10;
        if ((kb.flags & LLKHF_INJECTED) != 0)
            return User32.CallNextHookEx(_hook, nCode, wParam, lParam);

        bool isAltVk = kb.vkCode == (uint)VK.MENU || kb.vkCode == 0xA4 /* VK_LMENU */ || kb.vkCode == 0xA5 /* VK_RMENU */;
        bool isDown  = wParam == (IntPtr)WM.KEYDOWN || wParam == (IntPtr)WM.SYSKEYDOWN;
        bool isUp    = wParam == (IntPtr)WM.KEYUP   || wParam == (IntPtr)WM.SYSKEYUP;

        // 跟踪物理 Alt 状态
        if (isAltVk)
        {
            if (isDown && !_altPhysicallyDown)
            {
                _altPhysicallyDown = true;
                _altActiveAtUtc = DateTime.UtcNow;
            }
            else if (isUp)
            {
                _altPhysicallyDown = false;
            }
        }

        if (isDown && TargetVKs.Contains(kb.vkCode))
        {
            // 必须同时满足：Alt 状态为按下 AND 新鲜（距离最近 Alt 活动 < 5 秒）
            // 防止 Alt up 事件丢失导致 _altPhysicallyDown 卡 true，
            // 用户后续打字母误被拦截
            bool altFresh = _altPhysicallyDown &&
                            DateTime.UtcNow - _altActiveAtUtc < AltFreshWindow;

            if (altFresh && VkToChar.TryGetValue(kb.vkCode, out char c))
            {
                _altActiveAtUtc = DateTime.UtcNow;  // 拦截成功，刷新活动时间
                _queue.Writer.TryWrite(c);
                InjectFakeKey();
                return (IntPtr)1;
            }
        }
        return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static void InjectFakeKey()
    {
        const ushort VK_F24 = 0x87;
        // F24 down/up 打断 Alt-only 序列防菜单激活
        // + Alt up 让应用视角下 Alt 松开（支持长按 Alt 多按 + PostMessage WM_CHAR 立即处理）
        var inputs = new INPUT[]
        {
            new() { type = INPUT_TYPE.KEYBOARD,
                    u = { ki = new KEYBDINPUT { wVk = VK_F24, dwFlags = KEYEVENTF.KEYDOWN } } },
            new() { type = INPUT_TYPE.KEYBOARD,
                    u = { ki = new KEYBDINPUT { wVk = VK_F24, dwFlags = KEYEVENTF.KEYUP } } },
            new() { type = INPUT_TYPE.KEYBOARD,
                    u = { ki = new KEYBDINPUT { wVk = (ushort)VK.MENU, dwFlags = KEYEVENTF.KEYUP } } },
        };
        User32.SendInput(3, inputs, Marshal.SizeOf<INPUT>());
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (char key in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    _log.LogDebug("热键触发: Alt+{Key}", key);
                    HotkeyTriggered?.Invoke(key);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "处理热键 Alt+{Key} 时出错", key);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogError(ex, "ProcessQueueAsync 异常"); }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public void Dispose()
    {
        _cts.Cancel();
        if (_hook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _log.LogInformation("键盘钩子已卸载");
        }
        _cts.Dispose();
    }
}
