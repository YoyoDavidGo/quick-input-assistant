using Microsoft.Extensions.Logging;
using QuickInputAssistant.PInvoke;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace QuickInputAssistant.Services;

/// <summary>
/// 检查当前前台进程是否在黑名单中（暂停响应热键）。
/// 读取 %LOCALAPPDATA%\QuickInputAssistant\blacklist.json。
/// </summary>
internal sealed class BlacklistService
{
    private static readonly string BlacklistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickInputAssistant", "blacklist.json");

    private readonly ILogger<BlacklistService> _log;
    private readonly HashSet<string> _list;

    public BlacklistService(ILogger<BlacklistService> logger)
    {
        _log = logger;
        _list = LoadList();
    }

    /// <summary>返回前台进程名（若在黑名单中），否则返回 null。</summary>
    public string? GetBlockedProcessName()
    {
        try
        {
            IntPtr fg = User32.GetForegroundWindow();
            User32.GetWindowThreadProcessId(fg, out uint pid);
            if (pid == 0) return null;
            using var proc = Process.GetProcessById((int)pid);
            string name = proc.ProcessName;
            return _list.Contains(name) ? name : null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "GetBlockedProcessName 失败");
            return null;
        }
    }

    private HashSet<string> LoadList()
    {
        var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mstsc" };
        try
        {
            if (!File.Exists(BlacklistPath))
            {
                // 写默认文件
                Directory.CreateDirectory(Path.GetDirectoryName(BlacklistPath)!);
                File.WriteAllText(BlacklistPath,
                    JsonSerializer.Serialize(defaults.ToArray(),
                        new JsonSerializerOptions { WriteIndented = true }));
                _log.LogInformation("已创建默认黑名单: {Path}", BlacklistPath);
                return defaults;
            }

            string json = File.ReadAllText(BlacklistPath);
            var arr = JsonSerializer.Deserialize<string[]>(json);
            if (arr is { Length: > 0 })
            {
                var set = new HashSet<string>(arr, StringComparer.OrdinalIgnoreCase);
                _log.LogInformation("已加载 {Count} 条黑名单", set.Count);
                return set;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "加载黑名单失败，使用默认值");
        }
        return defaults;
    }
}
