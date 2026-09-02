using System.Text.Json;
using MacroFolio.Models;

namespace MacroFolio.Services;

/// <summary>
/// Cross-character library of saved chat texts ("Mes textes"). Mirrors
/// <see cref="PlancheLibrary"/>: one JSON file per text under
/// <c>&lt;ConfigDir&gt;/Textes/</c>, shared across all characters.
/// </summary>
public sealed class TexteLibrary
{
    private readonly string textesDir;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private List<TexteRP>? cache;

    public TexteLibrary()
    {
        textesDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "Textes");
    }

    public IReadOnlyList<TexteRP> ListAll()
    {
        EnsureLoaded();
        return cache!
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public TexteRP Create(string title)
    {
        EnsureLoaded();
        var now = DateTimeOffset.UtcNow;
        var nextOrder = cache!.Count == 0 ? 0 : cache.Max(t => t.SortOrder) + 1;
        var texte = new TexteRP
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(title) ? "Sans titre" : title.Trim(),
            Content = "",
            SortOrder = nextOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };
        cache.Add(texte);
        Persist(texte);
        return texte;
    }

    public void MoveTo(string id, int targetIndex)
    {
        EnsureLoaded();
        var ordered = cache!.OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var from = ordered.FindIndex(t => t.Id == id);
        if (from < 0) return;

        targetIndex = Math.Clamp(targetIndex, 0, ordered.Count - 1);
        if (from == targetIndex) return;

        var moved = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(targetIndex, moved);

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].SortOrder == i) continue;
            ordered[i].SortOrder = i;
            Persist(ordered[i]);
        }
    }

    public void Update(TexteRP texte)
    {
        EnsureLoaded();
        var idx = cache!.FindIndex(t => t.Id == texte.Id);
        if (idx < 0) return;

        texte.UpdatedAt = DateTimeOffset.UtcNow;
        cache[idx] = texte;
        Persist(texte);
    }

    public void Delete(string id)
    {
        EnsureLoaded();
        var texte = cache!.FirstOrDefault(t => t.Id == id);
        if (texte is null) return;

        cache!.Remove(texte);

        var failure = SafeIo.Run(() =>
        {
            var path = ResolvePath(id);
            if (File.Exists(path))
                File.Delete(path);
        });
        if (failure is not null)
            Plugin.Log.Warning(failure, "Failed to delete texte file {Id}", id);
    }

    public TexteRP? GetById(string id)
    {
        EnsureLoaded();
        return cache!.FirstOrDefault(t => t.Id == id);
    }

    private void EnsureLoaded()
    {
        if (cache is not null) return;
        cache = new List<TexteRP>();

        var failure = SafeIo.Run(() =>
        {
            if (!Directory.Exists(textesDir)) return;

            foreach (var path in Directory.EnumerateFiles(textesDir, "*.json"))
            {
                var readFailure = SafeIo.Run(() =>
                {
                    var json = File.ReadAllText(path);
                    var texte = JsonSerializer.Deserialize<TexteRP>(json, jsonOptions);
                    if (texte is not null && !string.IsNullOrEmpty(texte.Id))
                        cache.Add(texte);
                });
                if (readFailure is not null)
                    Plugin.Log.Warning(readFailure, "Skipped malformed texte {Path}", path);
            }
        });
        if (failure is not null)
            Plugin.Log.Warning(failure, "Failed to enumerate textes directory");
    }

    private void Persist(TexteRP texte)
    {
        var failure = SafeIo.Run(() =>
        {
            Directory.CreateDirectory(textesDir);
            var path = ResolvePath(texte.Id);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(texte, jsonOptions));
            File.Move(tmp, path, overwrite: true);
        });
        if (failure is not null)
            Plugin.Log.Error(failure, "Failed to save texte {Id}", texte.Id);
    }

    private string ResolvePath(string id) => Path.Combine(textesDir, $"{id}.json");
}
