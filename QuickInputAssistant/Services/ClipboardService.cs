using Microsoft.Extensions.Logging;
using QuickInputAssistant.PInvoke;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickInputAssistant.Services;

/// <summary>
/// 剪贴板无痕借用：备份所有格式 → 模拟 Ctrl+C → 轮询新内容 → 恢复原始内容。
/// </summary>
internal sealed class ClipboardService
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    private readonly ILogger<ClipboardService> _log;
    private readonly InputService _input;

    public ClipboardService(ILogger<ClipboardService> logger, InputService input)
    {
        _log = logger;
        _input = input;
    }

    // ── 公开接口 ──────────────────────────────────────────────────────

    /// <summary>备份剪贴板所有格式，返回快照。</summary>
    public ClipboardSnapshot Backup()
    {
        var snapshot = new ClipboardSnapshot();
        try
        {
            if (!User32.OpenClipboard(IntPtr.Zero))
            {
                _log.LogWarning("OpenClipboard 失败（备份）: {Err}", Marshal.GetLastWin32Error());
                return snapshot;
            }

            uint fmt = 0;
            while ((fmt = User32.EnumClipboardFormats(fmt)) != 0)
            {
                IntPtr hData = User32.GetClipboardData(fmt);
                if (hData == IntPtr.Zero) continue;

                // 只备份有句柄的格式（跳过 delay-render）
                IntPtr locked = User32.GlobalLock(hData);
                if (locked == IntPtr.Zero) continue;

                try
                {
                    UIntPtr size = User32.GlobalSize(hData);
                    byte[] buf = new byte[(int)size];
                    Marshal.Copy(locked, buf, 0, buf.Length);
                    snapshot.Formats[fmt] = buf;
                }
                finally { User32.GlobalUnlock(hData); }
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "备份剪贴板异常"); }
        finally { User32.CloseClipboard(); }

        // 也顺便记一下文本，便于后续比对
        snapshot.TextBefore = GetText();
        return snapshot;
    }

    /// <summary>发送 Ctrl+C 模拟复制。</summary>
    public void SendCtrlC() => _input.SendCtrlC();

    /// <summary>轮询剪贴板变化，返回新文本（若无变化或超时则返回 null）。</summary>
    public async Task<string?> PollForChangeAsync(ClipboardSnapshot snapshot, int timeoutMs = 150, int intervalMs = 20)
    {
        int elapsed = 0;
        while (elapsed < timeoutMs)
        {
            await Task.Delay(intervalMs);
            elapsed += intervalMs;

            string current = GetText();
            if (!string.IsNullOrEmpty(current) && current != snapshot.TextBefore)
                return current;
        }
        return null;
    }

    /// <summary>恢复备份的剪贴板内容。</summary>
    public void Restore(ClipboardSnapshot snapshot)
    {
        if (snapshot.Formats.Count == 0) return;

        try
        {
            if (!User32.OpenClipboard(IntPtr.Zero))
            {
                _log.LogWarning("OpenClipboard 失败（恢复）: {Err}", Marshal.GetLastWin32Error());
                return;
            }

            User32.EmptyClipboard();

            foreach (var (fmt, data) in snapshot.Formats)
            {
                IntPtr hMem = User32.GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
                if (hMem == IntPtr.Zero) continue;

                IntPtr locked = User32.GlobalLock(hMem);
                if (locked == IntPtr.Zero) { User32.GlobalFree(hMem); continue; }

                try { Marshal.Copy(data, 0, locked, data.Length); }
                finally { User32.GlobalUnlock(hMem); }

                User32.SetClipboardData(fmt, hMem);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "恢复剪贴板异常"); }
        finally { User32.CloseClipboard(); }
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────

    private static string GetText()
    {
        try
        {
            if (!User32.OpenClipboard(IntPtr.Zero)) return "";
            try
            {
                IntPtr hData = User32.GetClipboardData(CF_UNICODETEXT);
                if (hData == IntPtr.Zero) return "";

                IntPtr locked = User32.GlobalLock(hData);
                if (locked == IntPtr.Zero) return "";
                try { return Marshal.PtrToStringUni(locked) ?? ""; }
                finally { User32.GlobalUnlock(hData); }
            }
            finally { User32.CloseClipboard(); }
        }
        catch { return ""; }
    }
}

internal sealed class ClipboardSnapshot
{
    public Dictionary<uint, byte[]> Formats { get; } = new();
    public string TextBefore { get; set; } = "";
}
