using Microsoft.Extensions.Logging;
using QuickInputAssistant.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuickInputAssistant.Services;

/// <summary>
/// 14 个键位绑定值的持久化存储，DPAPI 加密，原子写入，防抖 1 秒。
/// </summary>
public sealed class BindingStore : IDisposable
{
    private const int MaxLength = 500;
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickInputAssistant");
    private static readonly string FilePath    = Path.Combine(DataDir, "bindings.json");
    private static readonly string TmpFilePath = FilePath + ".tmp";

    private readonly ILogger<BindingStore> _log;
    private readonly Dictionary<string, string> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private System.Threading.Timer? _debounceTimer;
    private readonly object _debounceLock = new();

    public BindingStore(ILogger<BindingStore> logger)
    {
        _log = logger;
        Directory.CreateDirectory(DataDir);
        Load();
    }

    // ── 读写接口 ──────────────────────────────────────────────────────

    public string Get(char key) =>
        _cache.TryGetValue(key.ToString(), out var v) ? v : "";

    public void Set(char key, string value)
    {
        value = Sanitize(value);
        _cache[key.ToString()] = value;
        ScheduleSave();
    }

    /// <summary>应用退出时强制 flush。</summary>
    public void Flush()
    {
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        SaveNow();
    }

    // ── 内部实现 ──────────────────────────────────────────────────────

    private void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            byte[] encrypted = File.ReadAllBytes(FilePath);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var data = JsonSerializer.Deserialize<BindingsData>(Encoding.UTF8.GetString(plain));
            if (data?.Bindings is not null)
            {
                foreach (var (k, v) in data.Bindings)
                    _cache[k] = v;
            }
            _log.LogInformation("已加载 {N} 条绑定", _cache.Count);
        }
        catch (CryptographicException)
        {
            _log.LogWarning("bindings.json 解密失败（可能来自其他用户/机器），已重置");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "加载 bindings.json 失败");
        }
    }

    private void SaveNow()
    {
        try
        {
            var data = new BindingsData
            {
                Bindings = new Dictionary<string, string>(_cache),
                LastUpdated = DateTimeOffset.Now,
            };
            byte[] plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
            byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TmpFilePath, encrypted);
            File.Move(TmpFilePath, FilePath, overwrite: true);
            _log.LogDebug("bindings.json 已保存");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "保存 bindings.json 失败");
        }
    }

    private void ScheduleSave()
    {
        lock (_debounceLock)
        {
            if (_debounceTimer == null)
                _debounceTimer = new System.Threading.Timer(_ => SaveNow(), null, 1000, Timeout.Infinite);
            else
                _debounceTimer.Change(1000, Timeout.Infinite);
        }
    }

    private static string Sanitize(string value)
    {
        // 剥离控制字符（保留 \t \n），截断超长
        var sb = new StringBuilder(Math.Min(value.Length, MaxLength));
        foreach (char c in value)
        {
            if (c == '\0') continue;
            if (char.IsControl(c) && c != '\t' && c != '\n') continue;
            if (sb.Length >= MaxLength) break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        Flush();
        _lock.Dispose();
        _debounceTimer?.Dispose();
    }
}
