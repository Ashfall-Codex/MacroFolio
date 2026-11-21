using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using TextCommand = Lumina.Excel.Sheets.TextCommand;

namespace MacroFolio.Services;

public static class AutomationGuard
{
    public enum Verdict
    {
        Allowed,
        CombatCommand,
        InCombat,
        InInstance,
    }
    
    private static readonly string[] CombatCommandNames =
    [
        "/action", "/pvpaction", "/blueaction", "/generalaction", "/petaction", "/buddyaction",
    ];

    private static HashSet<string>? combatCommands;

    public static bool IsExplorerMode()
    {
        unsafe
        {
            var finder = ContentsFinder.Instance();
            if (finder is null) return false;
            return finder->QueueInfo.QueueState == ContentsFinderQueueState.InContent
                   && finder->QueueInfo.PoppedContentIsExplorerMode;
        }
    }
    
    private static readonly HashSet<TerritoryIntendedUse> AllowedZones =
    [
        TerritoryIntendedUse.Town,
        TerritoryIntendedUse.Overworld,
        TerritoryIntendedUse.Firmament,
        TerritoryIntendedUse.GoldSaucer,
        TerritoryIntendedUse.HousingOutdoor,
        TerritoryIntendedUse.HousingIndoor,
        TerritoryIntendedUse.Inn,
        TerritoryIntendedUse.IslandSanctuary,
    ];

    // Open-world zones can still host instanced content (quest battles, events).
    private static readonly HashSet<TerritoryIntendedUse> SharedZones =
    [
        TerritoryIntendedUse.Town,
        TerritoryIntendedUse.Overworld,
    ];

    // Context check only: combat and instances block automated runs outside Explorer Mode.
    public static Verdict CheckContext()
    {
        if (IsExplorerMode()) return Verdict.Allowed;
        if (Plugin.Condition[ConditionFlag.InCombat]) return Verdict.InCombat;
        return IsInRestrictedInstance() ? Verdict.InInstance : Verdict.Allowed;
    }

    // In instanced content outside the allowed zones, Explorer Mode excepted.
    public static bool IsInRestrictedInstance()
    {
        if (IsExplorerMode()) return false;

        TerritoryIntendedUse zone;
        unsafe
        {
            var game = GameMain.Instance();
            if (game is null) return false;
            zone = game->CurrentTerritoryIntendedUseId;
        }

        if (!AllowedZones.Contains(zone)) return true;
        return SharedZones.Contains(zone) && IsBoundByDuty();
    }

    // Lines that must not run from a MacroFolio macro inside an instance: combat actions
    // and chaining to the next macro.
    public static bool IsRestrictedMacroLine(string line)
        => IsCombatCommand(line)
           || line.TrimStart().StartsWith("/nextmacro", StringComparison.OrdinalIgnoreCase);

    private static bool IsBoundByDuty()
        => Plugin.Condition[ConditionFlag.BoundByDuty]
           || Plugin.Condition[ConditionFlag.BoundByDuty56]
           || Plugin.Condition[ConditionFlag.BoundByDuty95];

    public static Verdict Check(IEnumerable<string> lines)
    {
        if (IsExplorerMode()) return Verdict.Allowed;

        var context = CheckContext();
        if (context != Verdict.Allowed) return context;

        return lines.Any(IsCombatCommand) ? Verdict.CombatCommand : Verdict.Allowed;
    }

    public static bool IsCombatCommand(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('/')) return false;

        var end = trimmed.IndexOfAny([' ', '\t']);
        var command = end < 0 ? trimmed : trimmed[..end];
        return CombatCommands().Contains(command);
    }

    private static HashSet<string> CombatCommands()
    {
        if (combatCommands is not null) return combatCommands;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Fallback when the sheet can't be read.
            "/action", "/ac", "/pvpaction", "/pvpac", "/blueaction", "/blueac",
            "/generalaction", "/gaction", "/petaction", "/pac", "/buddyaction", "/bac",
        };

        try
        {
            var english = Plugin.DataManager.GetExcelSheet<TextCommand>(ClientLanguage.English);
            var rowIds = english
                .Where(r => CombatCommandNames.Contains(r.Command.ExtractText(), StringComparer.OrdinalIgnoreCase))
                .Select(r => r.RowId)
                .ToList();

            foreach (var language in new[] { ClientLanguage.English, ClientLanguage.French, ClientLanguage.German, ClientLanguage.Japanese })
            {
                var sheet = Plugin.DataManager.GetExcelSheet<TextCommand>(language);
                foreach (var id in rowIds)
                {
                    if (sheet.GetRowOrDefault(id) is not { } row) continue;
                    foreach (var name in new[] { row.Command, row.ShortCommand, row.Alias, row.ShortAlias })
                    {
                        var text = name.ExtractText().Trim();
                        if (text.StartsWith('/')) set.Add(text);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to read TextCommand sheet; using the built-in combat command list.");
        }

        return combatCommands = set;
    }
}
