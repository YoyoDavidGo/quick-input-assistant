using Microsoft.Extensions.Logging;
using QuickInputAssistant.PInvoke;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickInputAssistant.Services;

/// <summary>
/// 用 SendInput + KEYEVENTF_UNICODE 输出任意 Unicode 字符串，以及模拟 Ctrl+C / Backspace。
/// </summary>
public sealed class InputService
{
    private readonly ILogger<InputService> _log;

    public InputService(ILogger<InputService> logger) => _log = logger;

    // ── 公开接口 ──────────────────────────────────────────────────────

    /// <summary>
    /// 直接 PostMessage WM_CHAR 到焦点控件，整串字符一次性进入应用消息队列，
    /// 应用一次 GetMessage 循环就批量接收，瞬间出现，不一字一字。
    /// 失败（无焦点控件 / 跨进程被拒）时回退到 SendInput Unicode。
    /// </summary>
    public void TypeString(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            ExitMenuModeIfNeeded();
            if (TryPostWmChar(text)) return;
            FallbackSendInputUnicode(text);
        }
        catch (Exception ex) { _log.LogError(ex, "TypeString 异常 (text len={Len})", text.Length); }
    }

    /// <summary>
    /// 原子操作：先删除 eraseCount 个字符再输出 newText。
    /// 策略：Shift+Left × N 选中前 N 字符 → 剪贴板写入新文本 → Ctrl+V 一次替换。
    /// 视觉效果：选区短暂高亮 → 瞬间替换为新文本，不再"逐字删除/逐字输入"。
    /// </summary>
    public void EraseAndType(int eraseCount, string newText)
    {
        if (eraseCount <= 0 && string.IsNullOrEmpty(newText)) return;

        try
        {
            ExitMenuModeIfNeeded();

            // 只删不输出
            if (eraseCount > 0 && string.IsNullOrEmpty(newText))
            {
                FallbackSendBackspaces(eraseCount);
                return;
            }
            // 只输出不删
            if (eraseCount <= 0)
            {
                TypeString(newText);
                return;
            }

            // 同时删除 + 输出：剪贴板 + Ctrl+V
            // 不备份/恢复剪贴板：tiptop 等应用处理 Ctrl+V 的时机可能晚于恢复时机，
            // 导致应用粘贴的是恢复后的旧剪贴板内容（用户原内容），日期不递增。
            // 代价：用户的剪贴板被新日期替代——使用 Alt+Q 双击功能的合理 trade-off
            if (!SetClipboardUnicode(newText))
            {
                _log.LogWarning("剪贴板写入失败，EraseAndType 回退到 SendInput 字符流");
                var fb = new List<INPUT>(eraseCount * 2 + newText.Length * 2);
                for (int i = 0; i < eraseCount; i++)
                {
                    fb.Add(MakeVkKey(VK.BACK, down: true));
                    fb.Add(MakeVkKey(VK.BACK, down: false));
                }
                foreach (char c in newText)
                {
                    fb.Add(MakeUnicodeKey(c, down: true));
                    fb.Add(MakeUnicodeKey(c, down: false));
                }
                Send(fb.ToArray(), "EraseAndType(fallback)");
                return;
            }

            // Backspace × N + Ctrl+V 一次性 SendInput
            var inputs = new List<INPUT>(eraseCount * 2 + 4);
            for (int i = 0; i < eraseCount; i++)
            {
                inputs.Add(MakeVkKey(VK.BACK, down: true));
                inputs.Add(MakeVkKey(VK.BACK, down: false));
            }
            inputs.Add(MakeVkKey(VK.CONTROL, down: true));
            inputs.Add(MakeVkKey(VK.KEY_V,   down: true));
            inputs.Add(MakeVkKey(VK.KEY_V,   down: false));
            inputs.Add(MakeVkKey(VK.CONTROL, down: false));
            Send(inputs.ToArray(), "EraseAndType-BackspacePaste");
        }
        catch (Exception ex) { _log.LogError(ex, "EraseAndType 异常"); }
    }

    private bool TryPostWmChar(string text)
    {
        IntPtr fg = User32.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        uint tid = User32.GetWindowThreadProcessId(fg, out _);
        if (tid == 0) return false;

        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!User32.GetGUIThreadInfo(tid, ref info)) return false;

        IntPtr target = info.hwndFocus != IntPtr.Zero ? info.hwndFocus : fg;
        if (target == IntPtr.Zero) return false;

        const uint WM_CHAR = 0x0102;
        foreach (char c in text)
        {
            if (!User32.PostMessage(target, WM_CHAR, (IntPtr)c, IntPtr.Zero))
                return false;
        }
        // 短暂让出，让目标应用主线程消化已 Post 的 WM_CHAR，
        // 防止后续 SendBackspaces（SendInput 实键路径）抢在字符到达前删除原有内容
        Thread.Sleep(20);
        return true;
    }

    private void FallbackSendInputUnicode(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(MakeUnicodeKey(c, down: true));
            inputs.Add(MakeUnicodeKey(c, down: false));
        }
        Send(inputs.ToArray(), "TypeString(fallback)");
    }

    /// <summary>
    /// 发送 n 次 Backspace。
    /// 优先走 PostMessage WM_KEYDOWN/WM_KEYUP VK_BACK 到焦点控件：
    /// 与 TypeString 的 PostMessage WM_CHAR 走同一消息队列，FIFO 保证调用顺序，
    /// 避免 SendInput 实键路径和 PostMessage 路径时序错乱导致的"先叠加再删除"视觉问题。
    /// </summary>
    public void SendBackspaces(int n)
    {
        if (n <= 0) return;
        try
        {
            if (TryPostBackspaces(n)) return;
            FallbackSendBackspaces(n);
        }
        catch (Exception ex) { _log.LogError(ex, "SendBackspaces 异常 n={N}", n); }
    }

    private bool TryPostBackspaces(int n)
    {
        IntPtr fg = User32.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        uint tid = User32.GetWindowThreadProcessId(fg, out _);
        if (tid == 0) return false;

        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!User32.GetGUIThreadInfo(tid, ref info)) return false;

        IntPtr target = info.hwndFocus != IntPtr.Zero ? info.hwndFocus : fg;
        if (target == IntPtr.Zero) return false;

        // 只发 WM_CHAR '\b' 退格控制字符：精确删除 n 字符。
        // 不发 WM_KEYDOWN VK_BACK 避免 tiptop 等应用对两种消息分别响应导致删多了。
        const uint WM_CHAR = 0x0102;
        for (int i = 0; i < n; i++)
        {
            if (!User32.PostMessage(target, WM_CHAR, (IntPtr)'\b', IntPtr.Zero)) return false;
        }
        Thread.Sleep(20);
        return true;
    }

    private void FallbackSendBackspaces(int n)
    {
        var inputs = new INPUT[n * 2];
        for (int i = 0; i < n; i++)
        {
            inputs[i * 2]     = MakeVkKey(VK.BACK, down: true);
            inputs[i * 2 + 1] = MakeVkKey(VK.BACK, down: false);
        }
        Send(inputs, "Backspace(fallback)");
    }

    /// <summary>
    /// 模拟 Ctrl+C：等 Alt 物理松开后直接发 Ctrl+C。
    /// 不发 Escape，避免部分应用在 Escape 时清除文本选区。
    /// Ctrl 键本身会自动退出 Win32 菜单模式。
    /// </summary>
    public void SendCtrlC()
    {
        WaitForAltRelease(400);
        ExitMenuModeIfNeeded();

        var inputs = new[]
        {
            MakeVkKey(VK.CONTROL, down: true),
            MakeVkKey(VK.KEY_C,   down: true),
            MakeVkKey(VK.KEY_C,   down: false),
            MakeVkKey(VK.CONTROL, down: false),
        };
        Send(inputs, "Ctrl+C");
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────

    /// <summary>等 Alt 键物理松开（最多 maxWaitMs ms），轮询 2ms 一次。</summary>
    private static void WaitForAltRelease(int maxWaitMs)
    {
        int waited = 0;
        while (waited < maxWaitMs && (GetAsyncKeyState(VK.MENU) & 0x8000) != 0)
        {
            Thread.Sleep(2);
            waited += 2;
        }
    }

    /// <summary>
    /// 用 GetGUIThreadInfo 精确检测前台窗口是否处于菜单激活模式（GUI_INMENUMODE）。
    /// 若是，模拟按一下 Alt 让其退出（再发 WM_CANCELMODE 兜底）。
    /// 这是按 Alt+2 后旧式 Win32 程序菜单栏被高亮、字符发不进去的根因。
    /// </summary>
    private void ExitMenuModeIfNeeded()
    {
        try
        {
            IntPtr fg = User32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            uint tid = User32.GetWindowThreadProcessId(fg, out _);
            if (tid == 0) return;

            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (!User32.GetGUIThreadInfo(tid, ref info)) return;

            const uint GUI_INMENUMODE = 0x00000004;
            if ((info.flags & GUI_INMENUMODE) == 0) return;

            // 在菜单激活模式：按一下 Alt 退出（最可靠方式）
            var altPress = new[]
            {
                MakeVkKey(VK.MENU, down: true),
                MakeVkKey(VK.MENU, down: false),
            };
            Send(altPress, "ExitMenu(AltToggle)");
            // 等待菜单系统处理（短暂等待避免后续输入被吞）
            Thread.Sleep(30);

            // 兜底
            User32.SendMessage(fg, 0x001F /* WM_CANCELMODE */, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ExitMenuModeIfNeeded 异常");
        }
    }

    private void Send(INPUT[] inputs, string label)
    {
        uint sent = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            _log.LogWarning("SendInput ({Label}) 仅发送 {Sent}/{Total}", label, sent, inputs.Length);
    }

    private static INPUT MakeUnicodeKey(char c, bool down)
    {
        uint flags = down ? KEYEVENTF.UNICODE : (KEYEVENTF.UNICODE | KEYEVENTF.KEYUP);
        return new INPUT
        {
            type = INPUT_TYPE.KEYBOARD,
            u = { ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = flags } }
        };
    }

    private static INPUT MakeVkKey(int vk, bool down)
    {
        return new INPUT
        {
            type = INPUT_TYPE.KEYBOARD,
            u = { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = down ? KEYEVENTF.KEYDOWN : KEYEVENTF.KEYUP } }
        };
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // ── 剪贴板辅助 ────────────────────────────────────────────────────
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>备份当前剪贴板的 CF_UNICODETEXT 数据（含末尾 \0 的原始 UTF-16 字节）。</summary>
    private byte[]? BackupClipboardUnicode()
    {
        try
        {
            if (!User32.OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                IntPtr hData = User32.GetClipboardData(CF_UNICODETEXT);
                if (hData == IntPtr.Zero) return null;
                IntPtr locked = User32.GlobalLock(hData);
                if (locked == IntPtr.Zero) return null;
                try
                {
                    UIntPtr size = User32.GlobalSize(hData);
                    var buf = new byte[(uint)size];
                    Marshal.Copy(locked, buf, 0, buf.Length);
                    return buf;
                }
                finally { User32.GlobalUnlock(hData); }
            }
            finally { User32.CloseClipboard(); }
        }
        catch (Exception ex) { _log.LogWarning(ex, "BackupClipboard 异常"); return null; }
    }

    /// <summary>把 UTF-16 字符串写入剪贴板（清空其他格式）。</summary>
    private bool SetClipboardUnicode(string text)
    {
        try
        {
            if (!User32.OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                User32.EmptyClipboard();
                byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + '\0');
                IntPtr hMem = User32.GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                if (hMem == IntPtr.Zero) return false;
                IntPtr locked = User32.GlobalLock(hMem);
                if (locked == IntPtr.Zero) { User32.GlobalFree(hMem); return false; }
                try { Marshal.Copy(bytes, 0, locked, bytes.Length); }
                finally { User32.GlobalUnlock(hMem); }
                return User32.SetClipboardData(CF_UNICODETEXT, hMem) != IntPtr.Zero;
            }
            finally { User32.CloseClipboard(); }
        }
        catch (Exception ex) { _log.LogWarning(ex, "SetClipboard 异常"); return false; }
    }

    /// <summary>把备份的字节恢复到剪贴板 CF_UNICODETEXT。</summary>
    private void RestoreClipboardUnicode(byte[] data)
    {
        try
        {
            if (!User32.OpenClipboard(IntPtr.Zero)) return;
            try
            {
                User32.EmptyClipboard();
                IntPtr hMem = User32.GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
                if (hMem == IntPtr.Zero) return;
                IntPtr locked = User32.GlobalLock(hMem);
                if (locked == IntPtr.Zero) { User32.GlobalFree(hMem); return; }
                try { Marshal.Copy(data, 0, locked, data.Length); }
                finally { User32.GlobalUnlock(hMem); }
                User32.SetClipboardData(CF_UNICODETEXT, hMem);
            }
            finally { User32.CloseClipboard(); }
        }
        catch (Exception ex) { _log.LogWarning(ex, "RestoreClipboard 异常"); }
    }
}
