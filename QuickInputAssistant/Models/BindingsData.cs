namespace QuickInputAssistant.Models;

internal sealed class BindingsData
{
    public int Version { get; set; } = 3;
    public int ActiveSlot { get; set; }
    public List<PresetSlot> Slots { get; set; } = new();
    public string Theme { get; set; } = "Dark";
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;

    /// <summary>v1 旧字段：仅用于反序列化迁移到 Slots，不应再写入</summary>
    public Dictionary<string, string>? Bindings { get; set; }
}

internal sealed class PresetSlot
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Bindings { get; set; } = new();
}
