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

    private IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM.KEYDOWN || wParam == (IntPtr)WM.SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (TargetVKs.Contains(kb.vkCode))
            {
                // 检查 Alt 是否按下（GetAsyncKeyState 高位为 1 表示按下）
                short altState = GetAsyncKeyState(VK.MENU);
                if ((altState & 0x8000) != 0 && VkToChar.TryGetValue(kb.vkCode, out char c))
                {
                    // 不阻塞：投递到队列后立即返回
                    _queue.Writer.TryWrite(c);
                    return (IntPtr)1; // 拦截按键，阻止传递
                }
            }
        }
        return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
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
