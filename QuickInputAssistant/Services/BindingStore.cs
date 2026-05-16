using Microsoft.Extensions.Logging;
using QuickInputAssistant.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuickInputAssistant.Services;

/// <summary>
/// 14 个键位绑定值的持久化存储。支持 4 套预设：每套有名字和独立绑定字典，
/// Get/Set 始终作用于活动预设。DPAPI 加密，原子写入，防抖 1 秒。
/// </summary>
public sealed class BindingStore : IDisposable
{
    private const int MaxLength = 500;
    private const int SlotCount = 4;
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickInputAssistant");
    private static readonly string FilePath    = Path.Combine(DataDir, "bindings.json");
    private static readonly string TmpFilePath = FilePath + ".tmp";

    private readonly ILogger<BindingStore> _log;
    private readonly List<PresetSlot> _slots = new();
    private int _activeIndex;
    private string _theme = "Dark";
    private string _lang  = "Zh";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private System.Threading.Timer? _debounceTimer;
    private readonly object _debounceLock = new();

    /// <summary>切换活动预设后触发（参数为新的 active index）</summary>
    public event Action<int>? ActiveSlotChanged;

    public BindingStore(ILogger<BindingStore> logger)
    {
        _log = logger;
        Directory.CreateDirectory(DataDir);
        Load();
    }

    // ── 当前活动预设的读写 ─────────────────────────────────────────────

    public string Get(char key) =>
        _slots[_activeIndex].Bindings.TryGetValue(key.ToString(), out var v) ? v : "";

    public void Set(char key, string value)
    {
        value = Sanitize(value);
        _slots[_activeIndex].Bindings[key.ToString()] = value;
        ScheduleSave();
    }

    // ── 预设管理 ─────────────────────────────────────────────────────

    public int ActiveSlot => _activeIndex;
    public int SlotTotal  => SlotCount;

    public string Theme
    {
        get => _theme;
        set { _theme = value ?? "Dark"; ScheduleSave(); }
    }

    public string Lang
    {
        get => _lang;
        set { _lang = value ?? "Zh"; ScheduleSave(); }
    }

    public string GetSlotName(int index)
    {
        if (index < 0 || index >= SlotCount) return "";
        var n = _slots[index].Name;
        return string.IsNullOrWhiteSpace(n) ? DefaultName(index) : n;
    }

    public void RenameSlot(int index, string newName)
    {
        if (index < 0 || index >= SlotCount) return;
        newName = (newName ?? "").Trim();
        if (newName.Length > 20) newName = newName[..20];
        _slots[index].Name = newName;
        ScheduleSave();
    }

    public void SwitchSlot(int index)
    {
        if (index < 0 || index >= SlotCount || index == _activeIndex) return;
        Flush();  // 切换前强制把当前修改落盘
        _activeIndex = index;
        SaveNow();
        ActiveSlotChanged?.Invoke(index);
    }

    private static string DefaultName(int index) => Strings.DefaultPresetName(index);

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
        // 初始化 4 个空槽位（即便后续 Load 失败也保证 _slots 长度=4）
        EnsureSlotsInitialized();

        if (!File.Exists(FilePath)) { SetDefaultsForFirstInstall(); return; }
        try
        {
            byte[] encrypted = File.ReadAllBytes(FilePath);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var data = JsonSerializer.Deserialize<BindingsData>(Encoding.UTF8.GetString(plain));
            if (data == null) { SetDefaultsForFirstInstall(); return; }

            // v1 迁移：根 Bindings 字典 → slot 0
            if ((data.Slots == null || data.Slots.Count == 0) && data.Bindings != null && data.Bindings.Count > 0)
            {
                _slots[0].Bindings = new Dictionary<string, string>(data.Bindings);
                _log.LogInformation("从 v1 格式迁移 {N} 条绑定到预设 1", data.Bindings.Count);
                SaveNow();
                return;
            }

            // v2/v3 正常加载
            _activeIndex = Math.Clamp(data.ActiveSlot, 0, SlotCount - 1);
            _theme = string.IsNullOrEmpty(data.Theme) ? "Dark" : data.Theme;
            _lang  = string.IsNullOrEmpty(data.Lang)  ? "Zh"   : data.Lang;
            for (int i = 0; i < Math.Min(SlotCount, data.Slots!.Count); i++)
            {
                _slots[i].Name     = data.Slots[i].Name ?? "";
                _slots[i].Bindings = data.Slots[i].Bindings ?? new();
            }
            _log.LogInformation("已加载预设 active={A}，theme={T}，slot0 共 {N} 条", _activeIndex, _theme, _slots[0].Bindings.Count);
        }
        catch (CryptographicException)
        {
            _log.LogWarning("bindings.json 解密失败（可能来自其他用户/机器），已重置");
            SetDefaultsForFirstInstall();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "加载 bindings.json 失败");
        }
    }

    private void EnsureSlotsInitialized()
    {
        _slots.Clear();
        for (int i = 0; i < SlotCount; i++)
            _slots.Add(new PresetSlot { Name = "", Bindings = new() });
    }

    private static readonly Dictionary<string, string> DefaultBindings = new()
    {
        ["1"] = "A700123",
        ["2"] = "AHBM10",
        ["3"] = "XX客户调试",
        ["4"] = "替换",
        ["5"] = "北京",
        ["6"] = "汉庭",
        ["W"] = "住所",
        ["E"] = "宾馆",
        ["R"] = "客户现场",
        ["A"] = "打车",
        ["S"] = "北京",
        ["D"] = "滴滴合计",
        ["F"] = "火车费",
    };

    private void SetDefaultsForFirstInstall()
    {
        _slots[0].Name = Strings.DefaultReimbursementName;
        _slots[0].Bindings = BuildDefaultBindings();
        SaveNow();
        _log.LogInformation("首次安装：已写入 {N} 条默认绑定到预设 1（报销）", _slots[0].Bindings.Count);
    }

    /// <summary>把指定槽位绑定重置为出厂默认（名字保持不变）</summary>
    public void ResetSlotToDefaults(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        _slots[index].Bindings = BuildDefaultBindings();
        SaveNow();
        if (index == _activeIndex) ActiveSlotChanged?.Invoke(index);
    }

    /// <summary>构造默认绑定字典；Alt+Q 动态填昨天日期（YY/MM/DD）</summary>
    private static Dictionary<string, string> BuildDefaultBindings()
    {
        var d = new Dictionary<string, string>(DefaultBindings);
        var y = DateTime.Today.AddDays(-1);
        d["Q"] = $"{y.Year % 100:D2}/{y.Month:D2}/{y.Day:D2}";
        return d;
    }

    private void SaveNow()
    {
        try
        {
            var data = new BindingsData
            {
                Version = 3,
                ActiveSlot = _activeIndex,
                Theme = _theme,
                Lang  = _lang,
                Slots = _slots.Select(s => new PresetSlot
                {
                    Name = s.Name,
                    Bindings = new Dictionary<string, string>(s.Bindings),
                }).ToList(),
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
