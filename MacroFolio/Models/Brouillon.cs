namespace MacroFolio.Models;

public sealed class Brouillon
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public uint IconId { get; set; }

    public List<string> Lines { get; set; } = new();

    public List<string> Notes { get; set; } = new();

    public float StartOffset { get; set; }

    /// <summary>Manual ordering in the list (arrows). Lower = higher in the list.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
