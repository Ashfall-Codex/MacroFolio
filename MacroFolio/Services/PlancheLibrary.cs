using System.Text.Json;
using MacroFolio.Models;

namespace MacroFolio.Services;

public sealed class PlancheLibrary
{
    private readonly string planchesDir;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private List<Planche>? cache;

    public PlancheLibrary()
    {
        planchesDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "Planches");
    }

    public IReadOnlyList<Planche> ListAll()
    {
        EnsureLoaded();
        return cache!;
    }

    public IReadOnlyList<Planche> ListForSet(MacroSet set)
    {
        EnsureLoaded();
        return cache!
            .Where(s => s.TargetSet == set)
            .OrderByDescending(s => s.UpdatedAt)
            .ToList();
    }

    public Planche Create(string name, MacroSet set, IEnumerable<PlancheEntry> entries)
    {
        EnsureLoaded();
        var now = DateTimeOffset.UtcNow;
        var planche = new Planche
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "Sans nom" : name.Trim(),
            TargetSet = set,
            CreatedAt = now,
            UpdatedAt = now,
            Entries = entries.Select(Clone).ToList(),
        };
        cache!.Add(planche);
        Persist(planche);
        return planche;
    }

    public void Update(Planche planche)
    {
        EnsureLoaded();
        var idx = cache!.FindIndex(s => s.Id == planche.Id);
        if (idx < 0) return;

        planche.UpdatedAt = DateTimeOffset.UtcNow;
        cache[idx] = planche;
        Persist(planche);
    }

    public void Delete(string id)
    {
        EnsureLoaded();
        var planche = cache!.FirstOrDefault(s => s.Id == id);
        if (planche is null) return;

        cache!.Remove(planche);

        var failure = SafeIo.Run(() =>
        {
            var path = ResolvePath(id);
            if (File.Exists(path))
                File.Delete(path);
        });
        if (failure is not null)
            Plugin.Log.Warning(failure, "Failed to delete planche file {Id}", id);
    }

    public Planche? GetById(string id)
    {
        EnsureLoaded();
        return cache!.FirstOrDefault(s => s.Id == id);
    }

    private static PlancheEntry Clone(PlancheEntry e) => new()
    {
        Slot = e.Slot,
        Name = e.Name,
        IconId = e.IconId,
        Lines = new List<string>(e.Lines),
    };

    private void EnsureLoaded()
    {
        if (cache is not null) return;
        cache = new List<Planche>();

        var failure = SafeIo.Run(() =>
        {
            if (!Directory.Exists(planchesDir)) return;

            foreach (var path in Directory.EnumerateFiles(planchesDir, "*.json"))
            {
                var readFailure = SafeIo.Run(() =>
                {
                    var json = File.ReadAllText(path);
                    var planche = JsonSerializer.Deserialize<Planche>(json, jsonOptions);
                    if (planche is not null && !string.IsNullOrEmpty(planche.Id))
                        cache.Add(planche);
                });
                if (readFailure is not null)
                    Plugin.Log.Warning(readFailure, "Skipped malformed planche {Path}", path);
            }
        });
        if (failure is not null)
            Plugin.Log.Warning(failure, "Failed to enumerate planches directory");
    }

    private void Persist(Planche planche)
    {
        var failure = SafeIo.Run(() =>
        {
            Directory.CreateDirectory(planchesDir);
            var path = ResolvePath(planche.Id);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(planche, jsonOptions));
            File.Move(tmp, path, overwrite: true);
        });
        if (failure is not null)
            Plugin.Log.Error(failure, "Failed to save planche {Id}", planche.Id);
    }

    private string ResolvePath(string id) => Path.Combine(planchesDir, $"{id}.json");
}
