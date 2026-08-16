using MacroFolio.Services;

namespace MacroFolio.Models;

public sealed class MacroBundle
{
    public int SchemaVersion { get; set; } = 1;

    public string? ExportedBy { get; set; }

    public DateTimeOffset ExportedAt { get; set; }

    public List<MacroBundleEntry> Macros { get; set; } = new();
}

public sealed class MacroBundleEntry
{
    public MacroSet Set { get; set; }

    // 0..99
    public int Slot { get; set; }

    public MacroEntry Entry { get; set; } = new();
}
