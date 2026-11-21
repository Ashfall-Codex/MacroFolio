using System.Text.Json.Serialization;

namespace MacroFolio.Models;

public sealed class CachedMacro
{
    public string Id { get; set; } = "";

    public int? GameSlot { get; set; }

    public string Name { get; set; } = "";

    public uint IconId { get; set; }

    public List<string> Lines { get; set; } = new();
    
    public List<string> Notes { get; set; } = new();
    
    public float StartOffset { get; set; }

    public bool CreatedByMacroFolio { get; set; }

    [JsonIgnore]
    public bool IsActive => GameSlot.HasValue;
}
