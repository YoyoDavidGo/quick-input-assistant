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

    /// <summary>逐字符输出文本到当前光标位置。</summary>
    public void TypeString(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // 等 Alt 物理松开（最多 400ms），然后退出菜单模式，再发字符
        WaitForAltRelease(400);
        DismissMenu();

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(MakeUnicodeKey(c, down: true));
            inputs.Add(MakeUnicodeKey(c, down: false));
        }
        Send(inputs.ToArray(), "TypeString");
    }

    /// <summary>发送 n 次 Backspace。</summary>
    public void SendBackspaces(int n)
    {
        if (n <= 0) return;
        var inputs = new INPUT[n * 2];
        for (int i = 0; i < n; i++)
        {
            inputs[i * 2]     = MakeVkKey(VK.BACK, down: true);
            inputs[i * 2 + 1] = MakeVkKey(VK.BACK, down: false);
        }
        Send(inputs, "Backspace");
    }

    /// <summary>
    /// 模拟 Ctrl+C：等 Alt 物理松开后直接发 Ctrl+C。
    /// 不发 Escape，避免部分应用在 Escape 时清除文本选区。
    /// Ctrl 键本身会自动退出 Win32 菜单模式。
    /// </summary>
    public void SendCtrlC()
    {
        WaitForAltRelease(400);

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

    /// <summary>等 Alt 键物理松开（最多 maxWaitMs ms）。</summary>
    private static void WaitForAltRelease(int maxWaitMs)
    {
        int waited = 0;
        while (waited < maxWaitMs && (GetAsyncKeyState(VK.MENU) & 0x8000) != 0)
        {
            Thread.Sleep(10);
            waited += 10;
        }
    }

    /// <summary>
    /// 退出 Win32 菜单激活模式（Alt 松开后进入的菜单栏高亮状态）。
    /// 用 WM_CANCELMODE 代替 VK_ESCAPE，避免关闭没有菜单栏的对话框。
    /// </summary>
    private static void DismissMenu()
    {
        IntPtr fg = User32.GetForegroundWindow();
        if (fg != IntPtr.Zero)
            User32.SendMessage(fg, 0x001F /* WM_CANCELMODE */, IntPtr.Zero, IntPtr.Zero);
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
}
