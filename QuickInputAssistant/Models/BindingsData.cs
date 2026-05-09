namespace QuickInputAssistant.Models;

internal sealed class BindingsData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Bindings { get; set; } = new();
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;
}
