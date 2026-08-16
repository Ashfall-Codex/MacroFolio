using System.Globalization;
using System.Text;
using Dalamud.Interface.Textures;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using FFXIVAction = Lumina.Excel.Sheets.Action;

namespace MacroFolio.Services;

public sealed class IconPickerIndex
{
    public enum IndexState { Unindexed, Indexing, Indexed }

    public IndexState State { get; private set; } = IndexState.Unindexed;
    private const int IconRangeMax = 250_000;
    private static readonly (int Start, int End) NullRange = (170_000, 180_000);
    private readonly SortedList<uint, IconInfo> iconInfos = new();
    private readonly SortedList<IconInfoCategory, IconInfoCategoryGroup> groupForCategory =
        new(IconInfoCategory.NameComparer);

    public void StartIndexing(System.Action onFinish)
    {
        if (State != IndexState.Unindexed) return;

        Task.Run(() =>
        {
            try
            {
                State = IndexState.Indexing;
                ComputeValidIconIds();
                ApplyHardcodedCategories();
                ApplyGamedataIconInfo();
                IndexIcons();
                State = IndexState.Indexed;
                onFinish();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to index icons");
            }
        });
    }
    public List<IconInfoCategoryGroup> CategoryRoots() =>
        groupForCategory
            .Where(kv => kv.Key.SubcategoryName is null)
            .Select(kv => kv.Value)
            .ToList();

    public List<IconInfo> All(IconInfoCategory? category = null)
    {
        if (State != IndexState.Indexed) return new();
        if (category is null) return iconInfos.Values.ToList();

        return groupForCategory.GetValueOrDefault(category)
            ?.GetIconsForCategory(category)
            .ToList()
            ?? new();
    }

    public List<IconInfo> NameSearch(string needle, IconInfoCategory? category = null)
    {
        if (State != IndexState.Indexed) return new();
        if (needle.Length < 2) return new();

        var searchNeedle = IconInfo.NormalizeForSearch(needle);
        var haystack = All(category);

        return haystack
            .Select<IconInfo, (IconInfo Icon, int Score)?>(icon =>
            {
                if (icon.IconId.ToString() == searchNeedle) return (icon, 0);
                foreach (var searchName in icon.SearchNames)
                {
                    if (searchName == searchNeedle) return (icon, 1);
                    if (searchName.Split(' ').Any(word => word == searchNeedle)) return (icon, 2);
                    if (searchName.Contains(searchNeedle)) return (icon, 3);
                }
                return null;
            })
            .OfType<(IconInfo Icon, int Score)>()
            .OrderBy(x => x.Score)
            .Select(x => x.Icon)
            .ToList();
    }

    private void ComputeValidIconIds()
    {
        for (var iconId = 0; iconId < IconRangeMax; iconId++)
        {
            if (iconId >= NullRange.Start && iconId < NullRange.End) continue;
            if (!Plugin.TextureProvider.TryGetIconPath(new GameIconLookup((uint)iconId), out _)) continue;

            iconInfos.Add((uint)iconId, new IconInfo { IconId = (uint)iconId });
        }
    }

    private void AddOrMergeIconInfo(IconInfo iconInfo)
    {
        if (iconInfos.TryGetValue(iconInfo.IconId, out var existing))
        {
            existing.AddNames(iconInfo.Names);
            existing.AddCategories(iconInfo.Categories);
        }
        else
        {
            iconInfos.Add(iconInfo.IconId, iconInfo);
        }
    }

    private void IndexIcons()
    {
        foreach (var iconInfo in iconInfos.Values)
        {
            foreach (var category in iconInfo.Categories)
            {
                var topLevel = category.TopLevel();
                if (!groupForCategory.TryGetValue(topLevel, out var group))
                {
                    group = new IconInfoCategoryGroup { Category = topLevel };
                    groupForCategory.Add(topLevel, group);
                }
                groupForCategory.TryAdd(category, group);
                group.AddIconForCategory(category, iconInfo);
            }
        }
    }

    private void ApplyHardcodedCategories()
    {
        ApplyIconCategory(1, 100, "Système");
        ApplyIconCategory(100, 4_000, "Actions", "Classe/Job");
        ApplyIconCategory(4_000, 4_400, "Montures");
        ApplyIconCategory(4_400, 5_100, "Mascottes");
        ApplyIconCategory(5_100, 8_000, "Actions", "Traits");
        ApplyIconCategory(8_000, 9_000, "Actions", "Mode");
        ApplyIconCategory(9_000, 10_000, "Actions", "JcJ");
        ApplyIconCategory(20_000, 30_000, "Objets", "Général");
        ApplyIconCategory(30_000, 50_000, "Objets", "Équipement");
        ApplyIconCategory(50_000, 54_000, "Objets", "Logement");
        ApplyIconCategory(54_000, 58_000, "Objets", "Équipement");
        ApplyIconCategory(58_000, 59_000, "Objets", "Mode");
        ApplyIconCategory(59_000, 59_400, "Montures");
        ApplyIconCategory(59_400, 60_000, "Mascottes");
        ApplyIconCategory(60_000, 60_100, "Système", "Interface");
        ApplyIconCategory(60_100, 60_200, "Icônes", "Catégories d'objets");
        ApplyIconCategory(60_200, 60_300, "Système", "Météo");
        ApplyIconCategory(60_300, 60_650, "Système", "Marqueurs de carte");
        ApplyIconCategory(60_650, 60_720, "Système", "Interface");
        ApplyIconCategory(60_720, 60_890, "Système", "Marqueurs de carte");
        ApplyIconCategory(60_900, 61_000, "Système", "Interface");
        ApplyIconCategory(61_100, 61_200, "Actions", "Événement");
        ApplyIconCategory(61_250, 61_290, "Actions", "Défis/Épreuves");
        ApplyIconCategory(61_390, 61_470, "Système", "Interface");
        ApplyIconCategory(61_690, 61_700, "Icônes", "Grande Compagnie");
        ApplyIconCategory(61_720, 61_750, "Icônes", "Quêtes");
        ApplyIconCategory(61_750, 61_900, "Icônes", "Style de jeu");
        ApplyIconCategory(61_920, 62_000, "Icônes", "Quêtes");
        ApplyIconCategory(62_000, 62_600, "Icônes", "Classe/Job");
        ApplyIconCategory(62_800, 62_900, "Icônes", "Classe/Job");
        ApplyIconCategory(62_900, 63_000, "Icônes", "Hauts faits");
        ApplyIconCategory(63_900, 64_000, "Système", "Marqueurs de carte");
        ApplyIconCategory(64_000, 64_200, "Actions", "Emotes");
        ApplyIconCategory(64_200, 64_325, "Actions", "Compagnie libre");
        ApplyIconCategory(64_325, 64_500, "Actions", "Emotes");
        ApplyIconCategory(64_600, 64_800, "Actions", "Eureka");
        ApplyIconCategory(64_800, 65_000, "Actions", "PNJ");
        ApplyIconCategory(65_000, 65_900, "Icônes", "Monnaies");
        ApplyIconCategory(66_000, 66_400, "Icônes", "Macros");
        ApplyIconCategory(66_400, 66_500, "Icônes", "Étiquettes");
        ApplyIconCategory(67_000, 68_000, "Actions", "Mode");
        ApplyIconCategory(70_000, 70_120, "Actions", "Course de chocobos");
        ApplyIconCategory(71_000, 71_450, "Icônes", "Quêtes");
        ApplyIconCategory(76_300, 78_000, "Icônes", "Pose de groupe");
        ApplyIconCategory(80_000, 80_200, "Icônes", "Quêtes");
        ApplyIconCategory(82_020, 82_040, "Icônes", "Orchestrion");
        ApplyIconCategory(83_000, 84_000, "Icônes", "Grande Compagnie");
        ApplyIconCategory(90_000, 100_000, "Icônes", "Compagnie libre");
        ApplyIconCategory(210_100, 229_962, "Statuts");
    }

    private void ApplyIconCategory(int startInclusive, int endExclusive, string category, string? subcategory = null)
    {
        for (var iconId = startInclusive; iconId < endExclusive; iconId++)
        {
            if (!iconInfos.ContainsKey((uint)iconId)) continue;

            var info = new IconInfo { IconId = (uint)iconId };
            info.AddCategory(new IconInfoCategory(category, subcategory));
            AddOrMergeIconInfo(info);
        }
    }

    // Curated, stable subset of name sources (see class summary).
    private void ApplyGamedataIconInfo()
    {
        ApplyIconNames<FFXIVAction>(
            row => row.Icon,
            row => new[] { row.Name, row.ClassJob.ValueNullable?.Name, row.ActionCategory.ValueNullable?.Name });

        ApplyIconNames<GeneralAction>(
            row => (uint)row.Icon,
            row => new ReadOnlySeString?[] { row.Name });

        ApplyIconNames<MainCommand>(
            row => (uint)row.Icon,
            row => new ReadOnlySeString?[] { row.Name });

        ApplyIconNames<Emote>(
            row => row.Icon,
            row => new[] { row.Name, row.EmoteCategory.ValueNullable?.Name });

        ApplyIconNames<Item>(
            row => row.Icon,
            row => new ReadOnlySeString?[] { row.Name });

        ApplyIconNames<Status>(
            row => row.Icon,
            row => new ReadOnlySeString?[] { row.Name });

        ApplyIconNames<Mount>(
            row => row.Icon,
            row => new ReadOnlySeString?[] { row.Singular });

        ApplyIconNames<Companion>(
            row => row.Icon,
            row => new ReadOnlySeString?[] { row.Singular });
    }

    private void ApplyIconNames<TRow>(
        Func<TRow, uint> icon,
        Func<TRow, IEnumerable<ReadOnlySeString?>> names)
        where TRow : struct, IExcelRow<TRow>
    {
        ExcelSheet<TRow> sheet;
        try { sheet = Plugin.DataManager.GetExcelSheet<TRow>(); }
        catch { return; }

        foreach (var row in sheet)
        {
            var iconId = icon(row);
            if (iconId == 0 || !iconInfos.ContainsKey(iconId)) continue;

            var usefulNames = names(row)
                .Where(n => n.HasValue)
                .Select(n => n!.Value.ExtractText())
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
            if (usefulNames.Count == 0) continue;

            AddOrMergeIconInfo(new IconInfo { IconId = iconId, Names = usefulNames });
        }
    }
}

public sealed class IconInfo
{
    public required uint IconId { get; init; }

    private List<string> names = new();
    public List<string> Names
    {
        get => names;
        init
        {
            names = value;
            SearchNames = value.Select(NormalizeForSearch).ToHashSet();
        }
    }

    public HashSet<string> SearchNames { get; private set; } = new();
    public List<IconInfoCategory> Categories { get; } = new();

    public void AddNames(IEnumerable<string> newNames)
    {
        foreach (var name in newNames)
        {
            var normalized = NormalizeForSearch(name);
            if (SearchNames.Contains(normalized)) continue;
            names.Add(name);
            SearchNames.Add(normalized);
        }
    }

    public void AddCategory(IconInfoCategory category)
    {
        if (Categories.Contains(category)) return;
        Categories.Add(category);

        var topLevel = category.TopLevel();
        if (topLevel != category && !Categories.Contains(topLevel))
            Categories.Add(topLevel);
    }

    public void AddCategories(IEnumerable<IconInfoCategory> categories)
    {
        foreach (var category in categories) AddCategory(category);
    }

    public static string NormalizeForSearch(string input) =>
        string.Concat(input.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
            .ToLowerInvariant();

    public static readonly Comparer<IconInfo> CompareById =
        Comparer<IconInfo>.Create((a, b) => a.IconId.CompareTo(b.IconId));
}

public sealed record IconInfoCategory(string Name, string? SubcategoryName = null)
{
    public IconInfoCategory TopLevel() => new(Name);

    public override string ToString() =>
        SubcategoryName is not null ? $"{Name} ({SubcategoryName})" : Name;

    public static readonly Comparer<IconInfoCategory> NameComparer =
        Comparer<IconInfoCategory>.Create((a, b) =>
        {
            var byName = string.CompareOrdinal(a.Name, b.Name);
            return byName != 0 ? byName : string.CompareOrdinal(a.SubcategoryName, b.SubcategoryName);
        });
}

public sealed class IconInfoCategoryGroup
{
    public required IconInfoCategory Category { get; init; }
    public SortedSet<IconInfo> IconInfos { get; } = new(IconInfo.CompareById);
    public SortedList<IconInfoCategory, SortedSet<IconInfo>> SubcategoryIconInfos { get; } =
        new(IconInfoCategory.NameComparer);

    public void AddIconForCategory(IconInfoCategory category, IconInfo iconInfo)
    {
        IconInfos.Add(iconInfo);

        if (category.SubcategoryName is not null)
        {
            if (!SubcategoryIconInfos.TryGetValue(category, out var sub))
            {
                sub = new SortedSet<IconInfo>(IconInfo.CompareById);
                SubcategoryIconInfos.Add(category, sub);
            }
            sub.Add(iconInfo);
        }
    }

    public SortedSet<IconInfo> GetIconsForCategory(IconInfoCategory category)
    {
        if (Category == category) return IconInfos;
        foreach (var (subcategory, icons) in SubcategoryIconInfos)
            if (subcategory == category) return icons;
        return new();
    }
}