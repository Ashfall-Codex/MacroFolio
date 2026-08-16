using MacroFolio.Services;

namespace MacroFolio.Models;

public sealed class Planche
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public MacroSet TargetSet { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<PlancheEntry> Entries { get; set; } = new();
}

public sealed class PlancheEntry
{
    public int Slot { get; set; }

    public string Name { get; set; } = "";

    public uint IconId { get; set; }

    public List<string> Lines { get; set; } = new();
}
