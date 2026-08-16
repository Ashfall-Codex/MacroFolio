namespace MacroFolio.Models;

public sealed class MacroEntry
{
    public string Name { get; set; } = "";

    public uint IconId { get; set; }

    public List<string> Lines { get; set; } = new();
}
