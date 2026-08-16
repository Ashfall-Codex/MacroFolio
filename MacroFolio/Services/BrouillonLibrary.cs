using System.Text.Json;
using MacroFolio.Models;

namespace MacroFolio.Services;

public sealed class BrouillonLibrary
{
    public const int ChunkUsefulLines = 14;

    public const int ChunkMaxLines = 15;

    private readonly string draftsDir;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private List<Brouillon>? cache;

    public BrouillonLibrary()
    {
        draftsDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "Brouillons");
    }

    public IReadOnlyList<Brouillon> ListAll()
    {
        EnsureLoaded();
        return cache!
            .OrderBy(d => d.SortOrder)
            .ThenByDescending(d => d.UpdatedAt)
            .ToList();
    }

    public Brouillon Create(string name, uint iconId, IEnumerable<string> lines)
    {
        EnsureLoaded();
        var now = DateTimeOffset.UtcNow;
        var nextOrder = cache!.Count == 0 ? 0 : cache.Max(d => d.SortOrder) + 1;
        var draft = new Brouillon
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "Sans nom" : name.Trim(),
            IconId = iconId,
            Lines = lines.ToList(),
            SortOrder = nextOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };
        cache.Add(draft);
        Persist(draft);
        return draft;
    }

    /// <summary>Swap a draft with its neighbour (move up/down in the list).</summary>
    public void Move(string id, int delta)
    {
        EnsureLoaded();
        var ordered = cache!.OrderBy(d => d.SortOrder)
            .ThenByDescending(d => d.UpdatedAt)
            .ToList();
        var idx = ordered.FindIndex(d => d.Id == id);
        var target = idx + delta;
        if (idx < 0 || target < 0 || target >= ordered.Count) return;

        (ordered[idx], ordered[target]) = (ordered[target], ordered[idx]);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].SortOrder == i) continue;
            ordered[i].SortOrder = i;
            Persist(ordered[i]);
        }
    }

    public void Update(Brouillon draft)
    {
        EnsureLoaded();
        var idx = cache!.FindIndex(d => d.Id == draft.Id);
        if (idx < 0) return;

        draft.UpdatedAt = DateTimeOffset.UtcNow;
        cache[idx] = draft;
        Persist(draft);
    }

    public void Delete(string id)
    {
        EnsureLoaded();
        var draft = cache!.FirstOrDefault(d => d.Id == id);
        if (draft is null) return;

        cache!.Remove(draft);
        var failure = SafeIo.Run(() =>
        {
            var path = ResolvePath(id);
            if (File.Exists(path))
                File.Delete(path);
        });
        if (failure is not null)
            Plugin.Log.Warning(failure, "Failed to delete draft file {Id}", id);
    }

    public Brouillon? GetById(string id)
    {
        EnsureLoaded();
        return cache!.FirstOrDefault(d => d.Id == id);
    }
    
    public static List<List<string>> SplitForGame(IReadOnlyList<string> lines)
    {
        var chunks = new List<List<string>>();
        var i = 0;
        while (i < lines.Count)
        {
            var remaining = lines.Count - i;
            if (remaining <= ChunkMaxLines)
            {
                chunks.Add(lines.Skip(i).Take(remaining).ToList());
                break;
            }
            var chunk = lines.Skip(i).Take(ChunkUsefulLines).ToList();
            chunk.Add("/nextmacro");
            chunks.Add(chunk);
            i += ChunkUsefulLines;
        }
        return chunks;
    }

    public static int CountRequiredMacros(IReadOnlyList<string> lines) => CountRequiredMacros(lines.Count);
    public static int CountRequiredMacros(int lineCount)
    {
        if (lineCount <= 0) return 0;
        if (lineCount <= ChunkMaxLines) return 1;
        return 1 + (lineCount - ChunkMaxLines + ChunkUsefulLines - 1) / ChunkUsefulLines;
    }

    private void EnsureLoaded()
    {
        if (cache is not null) return;
        cache = new List<Brouillon>();

        var failure = SafeIo.Run(() =>
        {
            if (!Directory.Exists(draftsDir)) return;

            foreach (var path in Directory.EnumerateFiles(draftsDir, "*.json"))
            {
                var readFailure = SafeIo.Run(() =>
                {
                    var json = File.ReadAllText(path);
                    var draft = JsonSerializer.Deserialize<Brouillon>(json, jsonOptions);
                    if (draft is not null && !string.IsNullOrEmpty(draft.Id))
                        cache.Add(draft);
                });
                if (readFailure is not null)
                    Plugin.Log.Warning(readFailure, "Skipped malformed draft {Path}", path);
            }
        });
        if (failure is not null)
            Plugin.Log.Warning(failure, "Failed to enumerate drafts directory");
    }

    private void Persist(Brouillon draft)
    {
        var failure = SafeIo.Run(() =>
        {
            Directory.CreateDirectory(draftsDir);
            var path = ResolvePath(draft.Id);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(draft, jsonOptions));
            File.Move(tmp, path, overwrite: true);
        });
        if (failure is not null)
            Plugin.Log.Error(failure, "Failed to save draft {Id}", draft.Id);
    }

    private string ResolvePath(string id) => Path.Combine(draftsDir, $"{id}.json");
}
