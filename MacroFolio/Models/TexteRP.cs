namespace MacroFolio.Models;

public sealed class TexteRP
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Content { get; set; } = "";

    public bool SendDirectly { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
