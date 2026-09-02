using System.Text.Json;
using MacroFolio.Models;

namespace MacroFolio.Services;
public sealed class MacroLibrary
{
    private const int CurrentSchemaVersion = 2;

    private readonly MacroService macroService;
    private readonly string cacheRootDir;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string? loadedCharacter;
    private LibraryFile data = new();

    public MacroLibrary(MacroService macroService)
    {
        this.macroService = macroService;
        cacheRootDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "Cache");
    }

    public bool IsAvailable => GetCurrentCharacter() is not null;
    public int Capacity => MacroService.SlotsPerSet;
    public IReadOnlyList<CachedMacro> List(MacroSet set)
    {
        EnsureLoaded();
        SyncWithGame(set);

        return Pick(set)
            .OrderBy(m => m.IsActive ? 0 : 1)
            .ThenBy(m => m.GameSlot ?? int.MaxValue)
            .ToList();
    }

    public int ActiveCount(MacroSet set)
    {
        EnsureLoaded();
        return Pick(set).Count(m => m.IsActive);
    }
    
    /// <summary>A cache/game divergence the user must arbitrate (never auto-overwritten).</summary>
    public readonly record struct MacroConflict(
        MacroSet Set, int Slot, string Id,
        string CacheName, string GameName,
        IReadOnlyList<string> CacheLines, IReadOnlyList<string> GameLines)
    {
        public int CacheLineCount => CacheLines.Count;
        public int GameLineCount => GameLines.Count;
    }

    private readonly List<MacroConflict> pendingConflicts = new();
    public IReadOnlyList<MacroConflict> PendingConflicts => pendingConflicts;

    /// <summary>
    /// Compare the cache with the in-game slots without ever silently overwriting
    /// the game. Safe cases are auto-resolved; genuine divergences are recorded as
    /// <see cref="PendingConflicts"/> for the user to arbitrate. Runs on login and
    /// when the native Macro window closes.
    ///
    /// Auto-resolved (no prompt):
    ///  - Icon-only reset: same name + lines, only the icon differs → re-impose our
    ///    cached icon (the game resets out-of-palette icons on save).
    ///  - External clear: the game slot is empty → mark the macro cache-only.
    /// Everything else (name or lines changed in-game, e.g. edited via the vanilla
    /// tool while the plugin was unloaded) is a CONFLICT and is left untouched.
    /// </summary>
    public void ReconcileWithGame()
    {
        if (GetCurrentCharacter() is null) return;
        EnsureLoaded();

        var changed = false;
        foreach (var set in new[] { MacroSet.Individual, MacroSet.Shared })
        {
            var live = macroService.Read(set);
            foreach (var m in Pick(set).Where(x => x.IsActive).ToList())
            {
                var slot = m.GameSlot!.Value;
                if (slot < 0 || slot >= live.Count) continue;
                var game = live[slot];

                var gameEmpty = string.IsNullOrEmpty(game.Name) && game.Lines.Count == 0 && game.IconId == 0;
                if (gameEmpty)
                {
                    // External clear: don't fight it, just mark cache-only.
                    m.GameSlot = null;
                    pendingConflicts.RemoveAll(c => c.Set == set && c.Slot == slot);
                    changed = true;
                    continue;
                }

                var sameContent = m.Name == game.Name && LinesEqual(m.Lines, game.Lines);
                if (sameContent)
                {
                    if (game.IconId != m.IconId)
                        macroService.Write(set, slot, ToEntry(m)); // restore our icon (safe)
                    pendingConflicts.RemoveAll(c => c.Set == set && c.Slot == slot);
                    continue;
                }

                // Genuine divergence: NEVER overwrite. Ask the user.
                if (!pendingConflicts.Any(c => c.Set == set && c.Slot == slot))
                {
                    pendingConflicts.Add(new MacroConflict(
                        set, slot, m.Id, m.Name, game.Name,
                        new List<string>(m.Lines), new List<string>(game.Lines)));
                }
            }
        }

        if (changed) SaveCurrent();
    }

    /// <summary>Resolve a conflict by adopting the in-game version into the cache.</summary>
    public void ResolveKeepGame(MacroConflict conflict)
    {
        EnsureLoaded();
        var m = Pick(conflict.Set).FirstOrDefault(x => x.Id == conflict.Id);
        if (m is not null && m.GameSlot == conflict.Slot)
        {
            var game = macroService.ReadSlot(conflict.Set, conflict.Slot);
            m.Name = game.Name;
            m.IconId = game.IconId;
            m.Lines = game.Lines;
            SaveCurrent();
        }
        pendingConflicts.RemoveAll(c => c.Set == conflict.Set && c.Slot == conflict.Slot);
    }

    /// <summary>Resolve a conflict by re-imposing MacroFolio's cached version in-game.</summary>
    public void ResolveRestoreCache(MacroConflict conflict)
    {
        EnsureLoaded();
        var m = Pick(conflict.Set).FirstOrDefault(x => x.Id == conflict.Id);
        if (m is not null && m.GameSlot == conflict.Slot)
            macroService.Write(conflict.Set, conflict.Slot, ToEntry(m));
        pendingConflicts.RemoveAll(c => c.Set == conflict.Set && c.Slot == conflict.Slot);
    }

    public void ResolveRestoreCacheAll()
    {
        EnsureLoaded();
        foreach (var conflict in pendingConflicts.ToList())
        {
            var m = Pick(conflict.Set).FirstOrDefault(x => x.Id == conflict.Id);
            if (m is not null && m.GameSlot == conflict.Slot)
                macroService.Write(conflict.Set, conflict.Slot, ToEntry(m));
        }
        pendingConflicts.Clear();
    }

    public CachedMacro Create(MacroSet set, string name, uint iconId, IEnumerable<string> lines, float startOffset = 0f, IEnumerable<string>? notes = null)
    {
        EnsureLoaded();
        SyncWithGame(set);

        var macro = new CachedMacro
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            IconId = iconId,
            Lines = lines.Take(MacroService.MaxLinesPerMacro).ToList(),
            Notes = notes?.Take(MacroService.MaxLinesPerMacro).ToList() ?? new List<string>(),
            StartOffset = startOffset,
        };
        var free = FindFreeSlot(set);
        if (free.HasValue)
        {
            macroService.Write(set, free.Value, ToEntry(macro));
            macro.GameSlot = free.Value;
        }

        Pick(set).Add(macro);
        SaveCurrent();
        return macro;
    }
    public void DisableAll(MacroSet set)
    {
        EnsureLoaded();
        SyncWithGame(set);
        foreach (var m in Pick(set).Where(m => m.IsActive).ToList())
        {
            var current = macroService.ReadSlot(set, m.GameSlot!.Value);
            m.Name = current.Name;
            m.IconId = current.IconId;
            m.Lines = current.Lines;
            macroService.Clear(set, m.GameSlot.Value);
            m.GameSlot = null;
        }
        SaveCurrent();
    }

    public void Disable(MacroSet set, string id)
    {
        EnsureLoaded();
        var macro = Pick(set).FirstOrDefault(m => m.Id == id);
        if (macro is null || !macro.IsActive) return;
        var current = macroService.ReadSlot(set, macro.GameSlot!.Value);
        macro.Name = current.Name;
        macro.IconId = current.IconId;
        macro.Lines = current.Lines;

        macroService.Clear(set, macro.GameSlot.Value);
        macro.GameSlot = null;
        SaveCurrent();
    }

    public bool Enable(MacroSet set, string id)
    {
        EnsureLoaded();
        var macro = Pick(set).FirstOrDefault(m => m.Id == id);
        if (macro is null || macro.IsActive) return false;

        var slot = FindFreeSlot(set);
        if (!slot.HasValue) return false;

        macroService.Write(set, slot.Value, ToEntry(macro));
        macro.GameSlot = slot.Value;
        SaveCurrent();
        return true;
    }
    
    public void UpdateMacro(MacroSet set, string id, string name, uint iconId, IEnumerable<string> lines, float startOffset = 0f, IEnumerable<string>? notes = null)
    {
        EnsureLoaded();
        var macro = Pick(set).FirstOrDefault(m => m.Id == id);
        if (macro is null) return;

        macro.Name = name;
        macro.IconId = iconId;
        macro.Lines = lines.Take(MacroService.MaxLinesPerMacro).ToList();
        macro.Notes = notes?.Take(MacroService.MaxLinesPerMacro).ToList() ?? new List<string>();
        macro.StartOffset = startOffset;

        if (macro.IsActive)
            macroService.Write(set, macro.GameSlot!.Value, ToEntry(macro));

        SaveCurrent();
    }

    public void Rename(MacroSet set, string id, string name)
    {
        EnsureLoaded();
        var macro = Pick(set).FirstOrDefault(m => m.Id == id);
        if (macro is null) return;

        macro.Name = name;
        if (macro.IsActive)
            macroService.Write(set, macro.GameSlot!.Value, ToEntry(macro));
        SaveCurrent();
    }

    public void SetIcon(MacroSet set, string id, uint iconId)
    {
        EnsureLoaded();
        var macro = Pick(set).FirstOrDefault(m => m.Id == id);
        if (macro is null) return;

        macro.IconId = iconId;
        if (macro.IsActive)
            macroService.Write(set, macro.GameSlot!.Value, ToEntry(macro));
        SaveCurrent();
    }

    public void Execute(MacroSet set, string id)
    {
        EnsureLoaded();
        var macro = Pick(set).FirstOrDefault(m => m.Id == id);
        if (macro?.GameSlot is not { } slot) return;
        macroService.ExecuteSlot(set, slot);
    }

    public enum PreviewResult { Ok, NoFreeSlot, Empty }

    // Timed preview of a long sequence: it is split into 15-line chunks, each run
    // on a scratch slot; the next chunk fires after the previous one's total
    // <wait> duration (so it mirrors the real chained-macro playback).
    private readonly Queue<List<string>> previewQueue = new();
    private MacroSet previewSet;
    private int? previewSlot;
    private float previewTimer;

    public bool IsPreviewing => previewQueue.Count > 0;

    public PreviewResult PreviewRun(MacroSet set, IReadOnlyList<string> lines)
    {
        EnsureLoaded();
        SyncWithGame(set);

        var body = lines.Where(l => l.Trim().Length > 0).ToList();
        if (body.Count == 0) return PreviewResult.Empty;

        var slot = FindFreeSlot(set);
        if (slot is null) return PreviewResult.NoFreeSlot;

        // Split into game-sized chunks (strip any /nextmacro: we chain in code).
        previewQueue.Clear();
        foreach (var chunk in BrouillonLibrary.SplitForGame(body))
        {
            var clean = chunk.Where(l =>
                !l.Trim().StartsWith("/nextmacro", StringComparison.OrdinalIgnoreCase)).ToList();
            if (clean.Count > 0) previewQueue.Enqueue(clean);
        }
        if (previewQueue.Count == 0) return PreviewResult.Empty;

        previewSet = set;
        previewSlot = slot.Value;
        previewTimer = 0f; // first chunk fires on the next tick
        return PreviewResult.Ok;
    }

    /// <summary>Advance the timed multi-chunk preview. Call once per frame.</summary>
    public void TickPreview(float deltaSeconds)
    {
        if (previewQueue.Count == 0 || previewSlot is null) return;

        previewTimer -= deltaSeconds;
        if (previewTimer > 0f) return;

        var chunk = previewQueue.Dequeue();
        macroService.Write(previewSet, previewSlot.Value, new MacroEntry
        {
            Name = "MacroFolio",
            IconId = 66001,
            Lines = chunk,
        });
        macroService.ExecuteSlot(previewSet, previewSlot.Value);

        if (previewQueue.Count > 0)
        {
            // Wait for this chunk to finish before the next: sum its <wait.N>,
            // plus a small margin. Minimum 1s so chunks never overlap.
            previewTimer = Math.Max(1f, SumWaits(chunk) + 0.5f);
        }
        else
        {
            // Last chunk done: clear the scratch slot shortly after.
            macroService.Clear(previewSet, previewSlot.Value);
            previewSlot = null;
        }
    }

    private static float SumWaits(IEnumerable<string> lines)
    {
        var total = 0f;
        var rx = new System.Text.RegularExpressions.Regex(@"<wait[.:](\d+(?:\.\d+)?)>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var line in lines)
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(line))
                if (float.TryParse(m.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var w))
                    total += w;
        return total;
    }

    public void SwapSlots(MacroSet set, int slotA, int slotB)
    {
        EnsureLoaded();
        if (slotA == slotB) return;

        var a = Pick(set).FirstOrDefault(m => m.IsActive && m.GameSlot == slotA);
        var b = Pick(set).FirstOrDefault(m => m.IsActive && m.GameSlot == slotB);
        if (a is null && b is null) return;

        if (a is not null) a.GameSlot = slotB;
        if (b is not null) b.GameSlot = slotA;

        if (a is not null) macroService.Write(set, slotB, ToEntry(a));
        else macroService.Clear(set, slotB);

        if (b is not null) macroService.Write(set, slotA, ToEntry(b));
        else macroService.Clear(set, slotA);

        SaveCurrent();
    }
    
    public void ActivateAtSlot(MacroSet set, string id, int slot)
    {
        EnsureLoaded();
        if (slot < 0 || slot >= MacroService.SlotsPerSet) return;

        var macro = Pick(set).FirstOrDefault(m => m.Id == id);
        if (macro is null) return;

        var oldSlot = macro.GameSlot;
        if (oldSlot == slot) return;

        var occupant = Pick(set).FirstOrDefault(m => m.IsActive && m.GameSlot == slot && m.Id != id);
        if (occupant is not null)
            occupant.GameSlot = null;

        macro.GameSlot = slot;
        macroService.Write(set, slot, ToEntry(macro));

        if (oldSlot.HasValue && oldSlot.Value != slot)
            macroService.Clear(set, oldSlot.Value);

        SaveCurrent();
    }

    public void Delete(MacroSet set, string id)
    {
        EnsureLoaded();
        var macro = Pick(set).FirstOrDefault(m => m.Id == id);
        if (macro is null) return;

        if (macro.IsActive)
            macroService.Clear(set, macro.GameSlot!.Value);

        Pick(set).Remove(macro);
        SaveCurrent();
    }
    
    public void ReplaceAll(MacroSet set, IEnumerable<PlancheEntry> entries)
    {
        EnsureLoaded();
        SyncWithGame(set);

        var list = Pick(set);
        var entryList = entries.ToList();
        var targetSlots = entryList.Select(e => e.Slot).ToHashSet();
        
        foreach (var m in list.Where(m => m.IsActive).ToList())
        {
            var slot = m.GameSlot!.Value;
            if (targetSlots.Contains(slot)) continue;

            var current = macroService.ReadSlot(set, slot);
            m.Name = current.Name;
            m.IconId = current.IconId;
            m.Lines = current.Lines;
            macroService.Clear(set, slot);
            m.GameSlot = null;
        }

        foreach (var entry in entryList)
        {
            if (entry.Slot < 0 || entry.Slot >= MacroService.SlotsPerSet) continue;

            var existing = list.FirstOrDefault(m => m.IsActive && m.GameSlot == entry.Slot);
            if (existing is not null)
            {
                existing.Name = entry.Name;
                existing.IconId = entry.IconId;
                existing.Lines = new List<string>(entry.Lines);
            }
            else
            {
                list.Add(new CachedMacro
                {
                    Id = Guid.NewGuid().ToString("N"),
                    GameSlot = entry.Slot,
                    Name = entry.Name,
                    IconId = entry.IconId,
                    Lines = new List<string>(entry.Lines),
                });
            }

            macroService.Write(set, entry.Slot, new MacroEntry
            {
                Name = entry.Name,
                IconId = entry.IconId,
                Lines = new List<string>(entry.Lines),
            });
        }

        SaveCurrent();
    }
    
    public IReadOnlyList<PlancheEntry> SnapshotActive(MacroSet set)
    {
        EnsureLoaded();
        SyncWithGame(set);
        return Pick(set)
            .Where(m => m.IsActive)
            .OrderBy(m => m.GameSlot!.Value)
            .Select(m => new PlancheEntry
            {
                Slot = m.GameSlot!.Value,
                Name = m.Name,
                IconId = m.IconId,
                Lines = new List<string>(m.Lines),
            })
            .ToList();
    }

    // Reconcile in-memory state with the game:
    //   game has content + we don't track it → adopt (new cache entry)
    //   game empty + we track it as active   → mark cache-only (external clear)
    //   game has content + we track it       → game wins, refresh fields
    private void SyncWithGame(MacroSet set)
    {
        var live = macroService.Read(set);
        var entries = Pick(set);
        var bySlot = entries
            .Where(e => e.IsActive)
            .GroupBy(e => e.GameSlot!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var changed = false;

        for (var slot = 0; slot < live.Count; slot++)
        {
            var game = live[slot];
            var isEmpty = string.IsNullOrEmpty(game.Name) && game.Lines.Count == 0 && game.IconId == 0;

            if (bySlot.TryGetValue(slot, out var tracked))
            {
                if (isEmpty)
                {
                    tracked.GameSlot = null;
                    changed = true;
                }
                else
                {
                    var contentDiffers = tracked.Name != game.Name || !LinesEqual(tracked.Lines, game.Lines);

                    // If a conflict is pending on this slot, NEVER overwrite the
                    // cache with the game version: the game copy may be a truncated
                    // save and the user hasn't decided yet (Restaurer / Garder).
                    var hasConflict = pendingConflicts.Any(c => c.Set == set && c.Slot == slot);

                    if (contentDiffers && !hasConflict)
                    {
                        // Genuine external edit that we haven't flagged yet: record a
                        // conflict instead of silently adopting it. Keeps the cache
                        // intact so "Restaurer MacroFolio" can re-impose it.
                        pendingConflicts.Add(new MacroConflict(
                            set, slot, tracked.Id, tracked.Name, game.Name,
                            new List<string>(tracked.Lines), new List<string>(game.Lines)));
                    }

                    // Icon-only reset (same content): re-impose our cached icon.
                    if (!contentDiffers)
                    {
                        if (tracked.IconId == 0)
                        {
                            if (game.IconId != 0) { tracked.IconId = game.IconId; changed = true; }
                        }
                        else if (game.IconId != tracked.IconId)
                        {
                            macroService.Write(set, slot, ToEntry(tracked));
                        }
                    }
                }
            }
            else if (!isEmpty)
            {
                entries.Add(new CachedMacro
                {
                    Id = Guid.NewGuid().ToString("N"),
                    GameSlot = slot,
                    Name = game.Name,
                    IconId = game.IconId,
                    Lines = game.Lines,
                });
                changed = true;
            }
        }

        if (changed)
            SaveCurrent();
    }

    private static bool LinesEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private int? FindFreeSlot(MacroSet set)
    {
        var used = new HashSet<int>(Pick(set).Where(m => m.IsActive).Select(m => m.GameSlot!.Value));
        for (var i = 0; i < MacroService.SlotsPerSet; i++)
            if (!used.Contains(i))
                return i;
        return null;
    }

    private static MacroEntry ToEntry(CachedMacro macro) => new()
    {
        Name = macro.Name,
        IconId = macro.IconId,
        Lines = new List<string>(macro.Lines),
    };
    
    public bool PublishChunks(
        MacroSet set,
        int startSlot,
        string baseName,
        uint iconId,
        IReadOnlyList<List<string>> chunks)
    {
        if (chunks.Count == 0) return true;
        if (startSlot < 0) return false;
        if (startSlot + chunks.Count > MacroService.SlotsPerSet) return false;

        EnsureLoaded();
        SyncWithGame(set);
        var list = Pick(set);

        for (var i = 0; i < chunks.Count; i++)
        {
            var slot = startSlot + i;
            var chunkLines = chunks[i];
            var chunkName = chunks.Count == 1 ? baseName : $"{baseName} {i + 1}/{chunks.Count}";

            var existing = list.FirstOrDefault(m => m.IsActive && m.GameSlot == slot);
            if (existing is not null)
            {
                existing.Name = chunkName;
                existing.IconId = iconId;
                existing.Lines = new List<string>(chunkLines);
            }
            else
            {

                foreach (var m in list)
                {
                    if (!m.IsActive && m.GameSlot == slot) m.GameSlot = null;
                }

                list.Add(new CachedMacro
                {
                    Id = Guid.NewGuid().ToString("N"),
                    GameSlot = slot,
                    Name = chunkName,
                    IconId = iconId,
                    Lines = new List<string>(chunkLines),
                });
            }

            macroService.Write(set, slot, new MacroEntry
            {
                Name = chunkName,
                IconId = iconId,
                Lines = new List<string>(chunkLines),
            });
        }

        SaveCurrent();
        return true;
    }

    private List<CachedMacro> Pick(MacroSet set)
        => set == MacroSet.Individual ? data.Individual : data.Shared;

    private static string? GetCurrentCharacter()
    {
        var name = Plugin.ObjectTable.LocalPlayer?.Name.ToString();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private void EnsureLoaded()
    {
        var current = GetCurrentCharacter();
        if (current == loadedCharacter && current is not null) return;

        if (current is null)
        {
            data = new LibraryFile();
            loadedCharacter = null;
            return;
        }

        data = LoadFrom(current);
        loadedCharacter = current;
    }

    private LibraryFile LoadFrom(string characterName)
    {
        var failure = SafeIo.Run(() =>
        {
            var path = ResolvePath(characterName);
            if (!File.Exists(path)) return new LibraryFile();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LibraryFile>(json, jsonOptions) ?? new LibraryFile();
        }, out var loaded);

        if (failure is not null)
            Plugin.Log.Warning(failure, "Failed to load macro library for {Char}", characterName);

        return loaded ?? new LibraryFile();
    }

    private void SaveCurrent()
    {
        if (loadedCharacter is null) return;
        var failure = SafeIo.Run(() =>
        {
            var path = ResolvePath(loadedCharacter);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, jsonOptions));
            File.Move(tmp, path, overwrite: true);
        });
        if (failure is not null)
            Plugin.Log.Error(failure, "Failed to save macro library for {Char}", loadedCharacter);
    }

    private string ResolvePath(string characterName)
        => Path.Combine(cacheRootDir, Sanitize(characterName), "macros.json");

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars);
    }

    private sealed class LibraryFile
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<CachedMacro> Individual { get; set; } = new();
        public List<CachedMacro> Shared { get; set; } = new();
    }
}
