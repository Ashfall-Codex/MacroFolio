using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using MacroFolio.Localization;
using MacroFolio.Services;

namespace MacroFolio.UI;

public sealed class IconPickerWindow : MacroFolioWindowBase, IDisposable
{
    private readonly IconPickerIndex index = new();

    private Action<uint>? callback;
    private uint? currentIconId;

    private string searchText = "";
    private List<IconInfo> results = new();
    private IconInfoCategory? selectedCategory;
    private readonly HashSet<string> expanded = new();

    public IconPickerWindow()
        : base(Loc.Get("IconPicker.Title") + "###MacroFolioIconPicker", ImGuiWindowFlags.NoScrollbar)
    {
        IsOpen = false;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420f, 360f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(560f, 520f);
        SizeCondition = ImGuiCond.FirstUseEver;

        index.StartIndexing(RefreshSearch);
    }

    public void Dispose() { }

    public void Open(uint? initialIcon, Action<uint> onPicked)
    {
        currentIconId = initialIcon;
        callback = onPicked;
        if (!IsOpen)
            IsOpen = true;
        else
            ImGui.SetWindowFocus(WindowName);
    }

    private void SetCategory(IconInfoCategory? category)
    {
        selectedCategory = category;
        RefreshSearch();
    }

    private void RefreshSearch()
    {
        results = string.IsNullOrEmpty(searchText)
            ? index.All(selectedCategory)
            : index.NameSearch(searchText, selectedCategory);
    }

    protected override void DrawContents()
    {
        var iconSize = 48f * ImGuiHelpers.GlobalScale;

        if (!ImGui.BeginTable("##icon_picker_layout", 2, ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Categories", ImGuiTableColumnFlags.WidthFixed, 150f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Icons", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();

        var hint = selectedCategory == null
            ? Loc.Get("IconPicker.SearchHint")
            : $"{Loc.Get("IconPicker.SearchInCategory")} {selectedCategory}";
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("##iconsearch", hint, ref searchText, 255))
            RefreshSearch();

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        using (var tree = ImRaii.Child("##CategoryTree"))
        {
            if (tree.Success)
                DrawCategoryTree();
        }

        ImGui.TableNextColumn();
        using (var list = ImRaii.Child("##IconList"))
        {
            if (list.Success)
            {
                if (index.State == IconPickerIndex.IndexState.Indexed)
                {
                    var columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X - ImGui.GetStyle().WindowPadding.X)
                        / (iconSize + ImGui.GetStyle().ItemSpacing.X)));
                    DrawSearchResults(iconSize, columns);
                }
                else
                {
                    var spinner = "|/-\\"[(int)(ImGui.GetTime() / 0.1f) % 4];
                    ImGui.TextUnformatted($"{Loc.Get("IconPicker.Indexing")} {spinner}");
                }
            }
        }

        ImGui.EndTable();
    }

    private int rowSeq;

    private void DrawCategoryTree()
    {
        var scale = ImGuiHelpers.GlobalScale;
        rowSeq = 0;

        // Section header, on-brand accent.
        ImGui.PushStyleColor(ImGuiCol.Text, MacroFolioTheme.AccentColor with { W = 0.85f });
        ImGui.TextUnformatted(Loc.Get("IconPicker.CategoriesHeader"));
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(2f);

        // A touch more breathing room + rounded selection highlights.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f * scale, 4f * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * scale);

        DrawCategoryRow(Loc.Get("IconPicker.All"), FontAwesomeIcon.LayerGroup, selectedCategory == null,
            indent: 0, expandable: false, isOpen: false, () => SetCategory(null), out _);

        foreach (var group in index.CategoryRoots())
            DrawCategoryGroup(group);

        ImGui.PopStyleVar(2);
    }

    private void DrawCategoryGroup(IconInfoCategoryGroup group)
    {
        var key = group.Category.Name;
        var hasSubs = group.SubcategoryIconInfos.Count > 0;
        var isOpen = expanded.Contains(key);

        DrawCategoryRow(
            group.Category.Name,
            isOpen ? FontAwesomeIcon.FolderOpen : FontAwesomeIcon.Folder,
            selectedCategory == group.Category,
            indent: 0, expandable: hasSubs, isOpen,
            () => SetCategory(group.Category),
            out var toggled);

        if (hasSubs && toggled)
        {
            if (isOpen) expanded.Remove(key);
            else expanded.Add(key);
            isOpen = !isOpen;
        }

        if (isOpen)
        {
            foreach (var (subcategory, _) in group.SubcategoryIconInfos)
            {
                DrawCategoryRow(
                    subcategory.SubcategoryName ?? subcategory.ToString(),
                    FontAwesomeIcon.AngleRight,
                    selectedCategory == subcategory,
                    indent: 1, expandable: false, isOpen: false,
                    () => SetCategory(subcategory),
                    out _);
            }
        }
    }
    private void DrawCategoryRow(
        string label, FontAwesomeIcon icon, bool selected,
        int indent, bool expandable, bool isOpen, Action onSelect, out bool toggled)
    {
        toggled = false;
        var scale = ImGuiHelpers.GlobalScale;
        var rowH = ImGui.GetFrameHeight();
        var iconFont = Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle;

        ImGui.PushID(rowSeq++);

        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, MacroFolioTheme.AccentColor with { W = 0.18f });
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, MacroFolioTheme.AccentColor with { W = 0.25f });
        }

        var rowStart = ImGui.GetCursorScreenPos();
        if (ImGui.Selectable("##row", selected, ImGuiSelectableFlags.None, new Vector2(0, rowH)))
        {
            if (expandable) toggled = true;
            onSelect();
        }

        if (selected) ImGui.PopStyleColor(2);

        var dl = ImGui.GetWindowDrawList();
        var textColor = ImGui.GetColorU32(selected ? MacroFolioTheme.AccentColor : new Vector4(0.82f, 0.82f, 0.82f, 1f));
        var muted = ImGui.GetColorU32(new Vector4(0.55f, 0.55f, 0.55f, 1f));
        var x = rowStart.X + (6f + indent * 12f) * scale;
        var midY = rowStart.Y + rowH * 0.5f;

        if (expandable)
        {
            using (iconFont.Push())
            {
                var chev = (isOpen ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight).ToIconString();
                var sz = ImGui.CalcTextSize(chev);
                dl.AddText(new Vector2(x, midY - sz.Y * 0.5f), muted, chev);
            }
        }
        x += 14f * scale;

        using (iconFont.Push())
        {
            var ic = icon.ToIconString();
            var sz = ImGui.CalcTextSize(ic);
            dl.AddText(new Vector2(x, midY - sz.Y * 0.5f), selected ? textColor : muted, ic);
            x += sz.X + 6f * scale;
        }

        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(x, midY - labelSz.Y * 0.5f), textColor, label);

        ImGui.PopID();
    }

    private void DrawSearchResults(float iconSize, int columns)
    {
        var lineHeight = iconSize + ImGui.GetStyle().ItemSpacing.Y;
        ImGuiClip.ClippedDraw(results, icon => DrawIcon(icon, iconSize), columns, lineHeight);
    }

    private void DrawIcon(IconInfo iconInfo, float iconSize)
    {
        var selected = currentIconId == iconInfo.IconId;
        var cursor = ImGui.GetCursorScreenPos();
        var size = new Vector2(iconSize);

        var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconInfo.IconId)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, size);

        if (selected)
        {
            ImGui.GetWindowDrawList().AddRect(
                cursor, cursor + size,
                ImGui.GetColorU32(MacroFolioTheme.AccentColor),
                3f * ImGuiHelpers.GlobalScale, ImDrawFlags.None, 2f * ImGuiHelpers.GlobalScale);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right) && wrap != null)
            {
                ImGui.Image(wrap.Handle, new Vector2(400f * ImGuiHelpers.GlobalScale));
            }
            else
            {
                if (iconInfo.Names.Count > 0)
                {
                    ImGui.TextColored(MacroFolioTheme.AccentColor, iconInfo.Names[0]);
                    for (var i = 1; i < iconInfo.Names.Count; i++)
                        ImGui.TextUnformatted(iconInfo.Names[i]);
                }
                ImGui.TextDisabled($"#{iconInfo.IconId}");
            }
            ImGui.EndTooltip();
        }

        if (ImGui.IsItemClicked() && callback != null)
        {
            currentIconId = iconInfo.IconId;
            callback(iconInfo.IconId);
            callback = null;
            IsOpen = false;
        }
    }
}
