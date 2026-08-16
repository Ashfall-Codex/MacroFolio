using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using MacroFolio.Localization;
using MacroFolio.Models;
using MacroFolio.Services;

namespace MacroFolio.UI;

public sealed class MainWindow : MacroFolioWindowBase, IDisposable
{
    private const float SidebarWidth = 56f;
    private const float SidebarButtonSize = 38f;
    private const float SidebarButtonRounding = 6f;
    private const float GridCellSize = 44f;
    private const float GridCellSpacing = 4f;

    private static int ComputeGridColumns(float availWidth, float cellSize, float spacing)
    {
        if (cellSize <= 0f) return 1;
        var step = cellSize + spacing;
        var fit = (int)Math.Floor((availWidth + spacing) / step);
        return Math.Max(1, fit);
    }

    private readonly MacroLibrary library;
    private readonly PlancheLibrary plancheLibrary;
    private readonly BrouillonLibrary brouillonLibrary;
    private readonly TexteLibrary texteLibrary;
    private readonly ChatSenderService chatSender;
    private readonly EmoteRecordingService recordingService;
    private readonly MacroCreateWizard createWizard;
    private readonly MacroImportWindow importWindow;
    private readonly IconPickerWindow iconPicker;
    private Tab activeTab = Tab.Macros;
    private const string MacroDragType = "MF_MACRO_CELL";
    private const string PlancheSlotDragType = "MF_PLANCHE_SLOT";
    private static readonly byte[] DragPayloadDummy = new byte[1];
    private string? draggedMacroId;
    private bool draggedMacroActive;
    private int draggedMacroSlot = -1;
    private (int A, int B)? pendingSwap;            // active ↔ active: swap slots
    private (string Id, int Slot)? pendingActivate; // cached/active → slot (move/activate)
    private string? pendingDisableId;               // active → cache
    private (string Id, int Slot, string OccupantName)? pendingOverwrite; // needs confirm
    private bool requestCacheAll;

    private int draggedPlancheSlot = -1;
    private (int From, int To)? pendingPlancheSwap;

    private static unsafe bool AcceptMacroDrop()
        => ImGui.AcceptDragDropPayload(MacroDragType).Handle is not null;

    private static unsafe bool AcceptPlancheDrop()
        => ImGui.AcceptDragDropPayload(PlancheSlotDragType).Handle is not null;

    private MacroSet macrosActiveSet = MacroSet.Individual;
    private MacroSet libraryActiveSet = MacroSet.Individual;
    private string? selectedId;
    private string? requestDeleteMacroId;
    private string? requestApplyPlancheId;
    private string? requestDeletePlancheId;
    private bool requestOpenCapture;
    private bool requestOpenCreateEmpty;
    private string? requestEditPlancheId;
    private bool requestOpenExport;
    private readonly HashSet<string> exportSelectedIds = new();
    private ExportFormat exportFormat = ExportFormat.JsonBundle;
    private MacroSet exportSourceSet = MacroSet.Individual;
    private enum ExportFormat { JsonBundle, TxtIndividual }
    private MacroSet captureTargetSet = MacroSet.Individual;
    private string captureNameDraft = "";
    private MacroSet emptyCreateSet = MacroSet.Individual;
    private string emptyCreateNameDraft = "";
    private Planche? editingPlanche;
    private string editingNameDraft = "";
    private int? pickingForSlot;
    private string pickerSearch = "";

    private static readonly Vector4 BorderEnabledColor = new(0.30f, 0.85f, 0.30f, 1f);
    private static readonly Vector4 BorderDisabledColor = new(0.90f, 0.30f, 0.30f, 1f);
    private static readonly Vector4 PlusBorderColor = new(0.55f, 0.55f, 0.55f, 1f);

    public MainWindow(
        MacroLibrary library,
        PlancheLibrary plancheLibrary,
        BrouillonLibrary brouillonLibrary,
        TexteLibrary texteLibrary,
        ChatSenderService chatSender,
        EmoteRecordingService recordingService,
        MacroCreateWizard createWizard,
        MacroImportWindow importWindow,
        IconPickerWindow iconPicker)
        : base("MacroFolio###MacroFolioMain")
    {
        this.library = library;
        this.plancheLibrary = plancheLibrary;
        this.brouillonLibrary = brouillonLibrary;
        this.texteLibrary = texteLibrary;
        this.chatSender = chatSender;
        this.recordingService = recordingService;
        this.createWizard = createWizard;
        this.importWindow = importWindow;
        this.iconPicker = iconPicker;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420f, 380f),
            MaximumSize = new Vector2(2400f, 1800f),
        };
        Size = new Vector2(820f, 580f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    private enum Tab
    {
        Macros,
        Library,
        Drafts,
        Textes,
        About,
    }

    protected override void DrawContents()
    {
        var sidebarW = SidebarWidth * ImGuiHelpers.GlobalScale;

        if (ImGui.BeginChild("##sidebar", new Vector2(sidebarW, 0), false, ImGuiWindowFlags.NoScrollbar))
            DrawSidebar();
        ImGui.EndChild();

        ImGui.SameLine();

        var drawList = ImGui.GetWindowDrawList();
        var sepPos = ImGui.GetCursorScreenPos();
        var sepHeight = ImGui.GetContentRegionAvail().Y;
        var sepColor = MacroFolioTheme.AccentColor with { W = 0.6f };
        drawList.AddLine(
            sepPos,
            new Vector2(sepPos.X, sepPos.Y + sepHeight),
            ImGui.GetColorU32(sepColor),
            1f * ImGuiHelpers.GlobalScale);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * ImGuiHelpers.GlobalScale);

        if (ImGui.BeginChild("##content", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar))
        {
            switch (activeTab)
            {
                case Tab.Macros:
                    DrawMacrosTab();
                    break;
                case Tab.Library:
                    DrawLibraryTab();
                    break;
                case Tab.Drafts:
                    DrawDraftsTab();
                    break;
                case Tab.Textes:
                    DrawTextesTab();
                    break;
                case Tab.About:
                    DrawAboutTab();
                    break;
            }
        }
        ImGui.EndChild();

        DispatchPlanchePopups();
        DispatchExportPopups();
        DispatchMacroDeletePopup();
        DrawConflictPopup();
    }

    private void DrawConflictPopup()
    {
        var conflicts = library.PendingConflicts;
        if (conflicts.Count == 0) return;

        if (!ImGui.IsPopupOpen("##macro_conflict"))
            ImGui.OpenPopup("##macro_conflict");

        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##macro_conflict", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextColored(new Vector4(0.90f, 0.55f, 0.20f, 1f), Loc.Get("Conflict.Header"));
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(2f);
        ImGui.TextWrapped(Loc.Get("Conflict.Intro"));
        ImGuiHelpers.ScaledDummy(6f);

        // Resolve the first pending conflict; the modal stays up for the next one.
        var c = conflicts[0];
        var setLabel = c.Set == MacroSet.Individual ? IndividualSetLabel() : Loc.Get("Set.Shared");
        ImGui.TextDisabled(string.Format(Loc.Get("Conflict.SlotInfo"), setLabel, c.Slot));
        ImGuiHelpers.ScaledDummy(4f);

        // Side-by-side line diff: game version (left) vs MacroFolio cache (right).
        var gameName = string.IsNullOrEmpty(c.GameName) ? Loc.Get("Common.Unnamed") : c.GameName;
        var cacheName = string.IsNullOrEmpty(c.CacheName) ? Loc.Get("Common.Unnamed") : c.CacheName;
        var colW = 240f * ImGuiHelpers.GlobalScale;
        var diffH = 170f * ImGuiHelpers.GlobalScale;

        if (ImGui.BeginTable("##conflict_diff", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(MacroFolioTheme.AccentColor, Loc.Get("Conflict.GameVersion"));
            ImGui.TableNextColumn();
            ImGui.TextColored(MacroFolioTheme.AccentColor, Loc.Get("Conflict.CacheVersion"));

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawConflictColumn(gameName, c.GameLines, c.CacheLines, colW, diffH, "##game");
            ImGui.TableNextColumn();
            DrawConflictColumn(cacheName, c.CacheLines, c.GameLines, colW, diffH, "##cache");

            ImGui.EndTable();
        }

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.TextDisabled(Loc.Get("Conflict.DiffLegend"));
        ImGuiHelpers.ScaledDummy(6f);

        if (ImGui.Button(Loc.Get("Conflict.KeepGame")))
            library.ResolveKeepGame(c);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(Loc.Get("Conflict.KeepGame.Help"));
            ImGui.EndTooltip();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Conflict.RestoreCache")))
            library.ResolveRestoreCache(c);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(Loc.Get("Conflict.RestoreCache.Help"));
            ImGui.EndTooltip();
        }

        // Close automatically once all conflicts are resolved.
        if (library.PendingConflicts.Count == 0)
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    // One side of the conflict diff: the macro name then its lines, with each line
    // highlighted (orange) when it differs from the same line in the other version.
    private static void DrawConflictColumn(
        string name, IReadOnlyList<string> lines, IReadOnlyList<string> other,
        float width, float height, string id)
    {
        var diffColor = new Vector4(0.95f, 0.65f, 0.25f, 1f);
        var sameColor = new Vector4(0.80f, 0.80f, 0.80f, 1f);

        ImGui.TextDisabled(name);
        if (ImGui.BeginChild(id, new Vector2(width, height), true))
        {
            if (lines.Count == 0)
                ImGui.TextDisabled("(vide)");

            for (var i = 0; i < lines.Count; i++)
            {
                var otherLine = i < other.Count ? other[i] : null;
                var differs = otherLine is null || otherLine != lines[i];
                ImGui.PushStyleColor(ImGuiCol.Text, differs ? diffColor : sameColor);
                ImGui.TextWrapped($"{i + 1}. {lines[i]}");
                ImGui.PopStyleColor();
            }
        }
        ImGui.EndChild();
    }

    private void DispatchMacroDeletePopup()
    {
        if (requestDeleteMacroId is not null)
        {
            CenterNextPopup();
            ImGui.OpenPopup("##macro_delete");
        }
        DrawMacroDeletePopup();
    }

    private void DrawMacroDeletePopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##macro_delete", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var macro = requestDeleteMacroId is null
            ? null
            : library.List(macrosActiveSet).FirstOrDefault(m => m.Id == requestDeleteMacroId);
        if (macro is null)
        {
            requestDeleteMacroId = null;
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var label = string.IsNullOrEmpty(macro.Name) ? Loc.Get("Common.Unnamed") : macro.Name;
        ImGui.TextUnformatted(string.Format(Loc.Get("Macros.Delete.Confirm"), label));
        ImGui.Separator();
        ImGui.TextDisabled(macro.IsActive
            ? Loc.Get("Macros.Delete.WarnActive")
            : Loc.Get("Macros.Delete.WarnCached"));

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.22f, 0.22f, 1f));
        if (ImGui.Button(Loc.Get("Ctx.Delete")))
        {
            library.Delete(macrosActiveSet, macro.Id);
            if (selectedId == macro.Id) selectedId = null;
            requestDeleteMacroId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##macro_del_cancel"))
        {
            requestDeleteMacroId = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DispatchExportPopups()
    {
        if (requestOpenExport)
        {
            ImGui.OpenPopup("##export");
            requestOpenExport = false;
        }
        DrawExportPopup();
    }


    private void DrawSidebar()
    {
        ImGui.Spacing();
        DrawSidebarButton(FontAwesomeIcon.Scroll, Tab.Macros, Loc.Get("Tab.Macros"));
        ImGui.Spacing();
        DrawSidebarButton(FontAwesomeIcon.PenAlt, Tab.Drafts, Loc.Get("Tab.Drafts"));
        ImGui.Spacing();
        DrawSidebarButton(FontAwesomeIcon.Book, Tab.Library, Loc.Get("Tab.Library"));
        ImGui.Spacing();
        DrawSidebarButton(FontAwesomeIcon.CommentDots, Tab.Textes, Loc.Get("Tab.Textes"));

        var btnSize = SidebarButtonSize * ImGuiHelpers.GlobalScale;
        var bottomPad = 4f * ImGuiHelpers.GlobalScale;
        var spaceLeft = ImGui.GetContentRegionAvail().Y - btnSize - bottomPad;
        if (spaceLeft > 0)
            ImGui.Dummy(new Vector2(0f, spaceLeft));

        DrawSidebarButton(FontAwesomeIcon.InfoCircle, Tab.About, Loc.Get("Tab.About"));
    }

    private void DrawSidebarButton(FontAwesomeIcon icon, Tab tab, string tooltip)
    {
        var isActive = activeTab == tab;
        var size = SidebarButtonSize * ImGuiHelpers.GlobalScale;
        var availW = ImGui.GetContentRegionAvail().X;
        var offset = Math.Max(0f, (availW - size) / 2f);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

        var bgColor = isActive ? MacroFolioTheme.AccentColor : MacroFolioTheme.ThemeButtonBg;
        var hoverColor = isActive ? MacroFolioTheme.AccentColor : MacroFolioTheme.ThemeButtonHovered;
        var iconColor = isActive ? MacroFolioTheme.ThemeWindowBg : MacroFolioTheme.AccentColor;

        ImGui.PushStyleColor(ImGuiCol.Button, bgColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MacroFolioTheme.ThemeButtonActive);
        ImGui.PushStyleColor(ImGuiCol.Text, iconColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, SidebarButtonRounding * ImGuiHelpers.GlobalScale);

        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            var iconStr = icon.ToIconString();
            if (ImGui.Button(iconStr + "##tab_" + (int)tab, new Vector2(size, size)))
                activeTab = tab;
        }

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }
    }


    private void DrawMacrosTab()
    {
        DrawToolbar();
        DrawSetTabs();
        ImGui.Spacing();

        if (!library.IsAvailable)
        {
            ImGui.TextColored(BorderDisabledColor, Loc.Get("Macros.LoginRequired"));
            return;
        }

        var macros = library.List(macrosActiveSet);
        var activeBySlot = macros.Where(m => m.IsActive).ToDictionary(m => m.GameSlot!.Value);
        var cached = macros.Where(m => !m.IsActive).ToList();

        var scale = ImGuiHelpers.GlobalScale;
        var spacing = GridCellSpacing * scale;
        var footerH = 30f * scale;
        
        const int gridCols = 10;
        const int gridRows = 10;
        const int cacheReserveRows = 2;
        var style = ImGui.GetStyle();
        var padY = style.WindowPadding.Y;
        var itemSpaceY = style.ItemSpacing.Y;
        var cacheHeaderH = 34f * scale;

        var availW = ImGui.GetContentRegionAvail().X;
        var availH = ImGui.GetContentRegionAvail().Y;

        var widthBased = (availW - spacing * (gridCols - 1) - 16f * scale) / gridCols;
        var chrome = footerH + cacheHeaderH + 4f * padY + 3f * itemSpaceY + 4f * scale;
        var heightBased =
            (availH - chrome - spacing * ((gridRows - 1) + (cacheReserveRows - 1)))
            / (gridRows + cacheReserveRows);

        var cellSize = Math.Clamp(Math.Min(widthBased, heightBased), 18f * scale, 72f * scale);
        var hSpacing = Math.Clamp(
            (availW - 16f * scale - gridCols * cellSize) / (gridCols - 1),
            spacing,
            cellSize);

        var gridH = gridRows * cellSize + (gridRows - 1) * spacing + 2f * padY;
        if (ImGui.BeginChild("##active_grid", new Vector2(0, gridH), false, ImGuiWindowFlags.NoScrollbar))
            DrawActiveGrid(activeBySlot, cellSize, hSpacing, spacing);
        ImGui.EndChild();
        
        if (ImGui.BeginChild("##cache_section", new Vector2(0, -footerH)))
            DrawCacheSection(cached, cellSize, hSpacing, spacing);
        ImGui.EndChild();

        DrawFooter(macros);

        DispatchMacroDnd();
        DrawOverwritePopup();
    }

    private void DrawActiveGrid(Dictionary<int, CachedMacro> activeBySlot, float cellSize, float hSpacing, float vSpacing)
    {
        const int columns = 10;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(hSpacing, vSpacing));
        for (var slot = 0; slot < MacroService.SlotsPerSet; slot++)
        {
            activeBySlot.TryGetValue(slot, out var macro);
            DrawSlotCell(slot, macro, cellSize);
            if ((slot + 1) % columns != 0)
                ImGui.SameLine();
        }
        ImGui.PopStyleVar();
    }

    private void DrawSlotCell(int slot, CachedMacro? macro, float cellSize)
    {
        ImGui.PushID($"slot_{slot}");
        var cursorScreen = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var size = new Vector2(cellSize, cellSize);
        var isSelected = macro is not null && selectedId == macro.Id;

        if (isSelected)
            drawList.AddRectFilled(cursorScreen, cursorScreen + size,
                ImGui.GetColorU32(MacroFolioTheme.AccentColor with { W = 0.15f }), 4f * ImGuiHelpers.GlobalScale);

        if (ImGui.InvisibleButton("##cell", size) && macro is not null)
            selectedId = macro.Id;

        if (macro is not null && ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
        {
            draggedMacroId = macro.Id;
            draggedMacroActive = true;
            draggedMacroSlot = slot;
            ImGui.SetDragDropPayload(MacroDragType, DragPayloadDummy);
            ImGui.TextUnformatted(string.IsNullOrEmpty(macro.Name) ? Loc.Get("Common.Unnamed") : macro.Name);
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginDragDropTarget())
        {
            if (AcceptMacroDrop() && draggedMacroId is not null && draggedMacroId != macro?.Id)
                ResolveDropOnSlot(slot, macro);
            ImGui.EndDragDropTarget();
        }

        if (macro is not null)
        {
            DrawMacroContextMenu(macro);
            DrawMacroIcon(macro, cursorScreen, cellSize, drawList, dimmed: false);
            DrawCellBorder(cursorScreen, size, drawList, isSelected ? MacroFolioTheme.AccentColor : BorderEnabledColor);
            DrawMacroTooltip(macro);
        }
        else
        {
            var label = slot.ToString();
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(cursorScreen + (size - textSize) / 2f,
                ImGui.GetColorU32(new Vector4(0.45f, 0.45f, 0.45f, 1f)), label);
            DrawCellBorder(cursorScreen, size, drawList, MacroFolioTheme.ThemeBorder, 1f);
        }

        ImGui.PopID();
    }

    private void DrawCacheSection(List<CachedMacro> cached, float cellSize, float hSpacing, float vSpacing)
    {
        // Header doubles as a drop zone: dropping an active macro here disables it.
        var drawList = ImGui.GetWindowDrawList();
        var headerPos = ImGui.GetCursorScreenPos();
        var headerH = 22f * ImGuiHelpers.GlobalScale;
        ImGui.InvisibleButton("##cache_header", new Vector2(ImGui.GetContentRegionAvail().X, headerH));
        if (ImGui.BeginDragDropTarget())
        {
            if (AcceptMacroDrop() && draggedMacroId is not null && draggedMacroActive)
                pendingDisableId = draggedMacroId;
            ImGui.EndDragDropTarget();
        }
        var headerHovered = ImGui.IsItemHovered();
        drawList.AddText(headerPos + new Vector2(0, 3f * ImGuiHelpers.GlobalScale),
            ImGui.GetColorU32(headerHovered ? MacroFolioTheme.AccentColor : MacroFolioTheme.AccentColor with { W = 0.7f }),
            Loc.Get("Cache.Header"));

        ImGuiHelpers.ScaledDummy(2f);

        var cols = Math.Max(1, ComputeGridColumns(ImGui.GetContentRegionAvail().X, cellSize, hSpacing));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(hSpacing, vSpacing));

        var index = 0;
        foreach (var macro in cached)
        {
            DrawCachedCell(macro, cellSize);
            index++;
            if (index % cols != 0)
                ImGui.SameLine();
        }
        DrawPlusCell(cellSize);

        ImGui.PopStyleVar();
    }

    private void DrawCachedCell(CachedMacro macro, float cellSize)
    {
        ImGui.PushID($"cache_{macro.Id}");
        var cursorScreen = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var size = new Vector2(cellSize, cellSize);
        var isSelected = selectedId == macro.Id;

        if (isSelected)
            drawList.AddRectFilled(cursorScreen, cursorScreen + size,
                ImGui.GetColorU32(MacroFolioTheme.AccentColor with { W = 0.15f }), 4f * ImGuiHelpers.GlobalScale);

        if (ImGui.InvisibleButton("##ccell", size))
            selectedId = macro.Id;

        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
        {
            draggedMacroId = macro.Id;
            draggedMacroActive = false;
            draggedMacroSlot = -1;
            ImGui.SetDragDropPayload(MacroDragType, DragPayloadDummy);
            ImGui.TextUnformatted(string.IsNullOrEmpty(macro.Name) ? Loc.Get("Common.Unnamed") : macro.Name);
            ImGui.EndDragDropSource();
        }

        DrawMacroContextMenu(macro);
        DrawMacroIcon(macro, cursorScreen, cellSize, drawList, dimmed: true);
        DrawCellBorder(cursorScreen, size, drawList, isSelected ? MacroFolioTheme.AccentColor : BorderDisabledColor);
        DrawMacroTooltip(macro);

        ImGui.PopID();
    }

    private void ResolveDropOnSlot(int slot, CachedMacro? occupant)
    {
        if (draggedMacroId is null) return;

        if (occupant is null)
        {
            pendingActivate = (draggedMacroId, slot);
        }
        else if (draggedMacroActive && draggedMacroSlot >= 0)
        {
            pendingSwap = (draggedMacroSlot, slot);
        }
        else
        {
            var name = string.IsNullOrEmpty(occupant.Name) ? Loc.Get("Common.Unnamed") : occupant.Name;
            pendingOverwrite = (draggedMacroId, slot, name);
        }
    }

    private void DispatchMacroDnd()
    {
        if (pendingSwap is { } swap)
        {
            library.SwapSlots(macrosActiveSet, swap.A, swap.B);
            pendingSwap = null;
        }
        if (pendingActivate is { } act)
        {
            library.ActivateAtSlot(macrosActiveSet, act.Id, act.Slot);
            pendingActivate = null;
        }
        if (pendingDisableId is not null)
        {
            library.Disable(macrosActiveSet, pendingDisableId);
            pendingDisableId = null;
        }
        if (pendingOverwrite is not null)
            ImGui.OpenPopup("##macro_overwrite");
    }

    private void DrawOverwritePopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##macro_overwrite", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (pendingOverwrite is not { } ow)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        ImGui.TextUnformatted(string.Format(Loc.Get("Overwrite.Confirm"), ow.Slot, ow.OccupantName));
        ImGui.TextWrapped(Loc.Get("Overwrite.Detail"));
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.55f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.65f, 0.30f, 1f));
        if (ImGui.Button(Loc.Get("Overwrite.Replace")))
        {
            library.ActivateAtSlot(macrosActiveSet, ow.Id, ow.Slot);
            pendingOverwrite = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);
        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##overwrite"))
        {
            pendingOverwrite = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    // --- Shared macro cell rendering ---

    private void DrawMacroContextMenu(CachedMacro macro)
    {
        if (!ImGui.BeginPopupContextItem("##ctx")) return;

        if (ImGui.MenuItem(Loc.Get("Ctx.Edit")))
            createWizard.OpenForEdit(macrosActiveSet, macro);

        // Execute: only meaningful for active macros (they occupy a game slot).
        if (!macro.IsActive) ImGui.BeginDisabled();
        if (ImGui.MenuItem(Loc.Get("Ctx.Execute")))
            library.Execute(macrosActiveSet, macro.Id);
        if (!macro.IsActive)
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(Loc.Get("Ctx.ExecuteDisabledHint"));
                ImGui.EndTooltip();
            }
        }

        ImGui.Separator();

        if (!macro.IsActive)
        {
            var canActivate = library.ActiveCount(macrosActiveSet) < library.Capacity;
            if (!canActivate) ImGui.BeginDisabled();
            if (ImGui.MenuItem(Loc.Get("Ctx.Enable")))
                library.Enable(macrosActiveSet, macro.Id);
            if (!canActivate) ImGui.EndDisabled();
        }
        else
        {
            if (ImGui.MenuItem(Loc.Get("Ctx.Disable")))
                library.Disable(macrosActiveSet, macro.Id);
        }

        ImGui.Separator();
        if (ImGui.MenuItem(Loc.Get("Ctx.Duplicate")))
            DuplicateMacroTo(macro, macrosActiveSet);

        var otherSet = macrosActiveSet == MacroSet.Individual ? MacroSet.Shared : MacroSet.Individual;
        var otherLabel = otherSet == MacroSet.Individual ? IndividualSetLabel() : Loc.Get("Set.Shared");
        if (ImGui.MenuItem(string.Format(Loc.Get("Ctx.DuplicateTo"), otherLabel)))
            DuplicateMacroTo(macro, otherSet);
        if (ImGui.MenuItem(string.Format(Loc.Get("Ctx.MoveTo"), otherLabel)))
        {
            MoveMacroTo(macro, otherSet);
            if (selectedId == macro.Id) selectedId = null;
        }

        ImGui.Separator();
        if (ImGui.MenuItem(Loc.Get("Ctx.ExportTxt")))
            ExportSingleAsTxt(macro);

        ImGui.Separator();
        if (ImGui.MenuItem(Loc.Get("Ctx.Delete")))
            requestDeleteMacroId = macro.Id;

        ImGui.EndPopup();
    }

    private static void DrawMacroIcon(CachedMacro macro, Vector2 cursorScreen, float cellSize, ImDrawListPtr drawList, bool dimmed)
    {
        var size = new Vector2(cellSize, cellSize);
        if (macro.IconId > 0)
        {
            try
            {
                var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(macro.IconId)).GetWrapOrDefault();
                if (wrap != null)
                {
                    var tint = dimmed ? new Vector4(1f, 1f, 1f, 0.45f) : Vector4.One;
                    drawList.AddImage(wrap.Handle, cursorScreen, cursorScreen + size, Vector2.Zero, Vector2.One, ImGui.GetColorU32(tint));
                    return;
                }
            }
            catch (Exception) { /* ignore: icon texture not ready */ }
        }

        var initial = string.IsNullOrEmpty(macro.Name) ? "?" : macro.Name[..1].ToUpperInvariant();
        var textSize = ImGui.CalcTextSize(initial);
        var color = dimmed ? new Vector4(0.7f, 0.7f, 0.7f, 0.6f) : new Vector4(0.85f, 0.85f, 0.85f, 1f);
        drawList.AddText(cursorScreen + (size - textSize) / 2f, ImGui.GetColorU32(color), initial);
    }

    private static void DrawCellBorder(Vector2 cursorScreen, Vector2 size, ImDrawListPtr drawList, Vector4 color, float thickness = 0f)
    {
        var t = thickness > 0f ? thickness : 2f * ImGuiHelpers.GlobalScale;
        drawList.AddRect(cursorScreen, cursorScreen + size, ImGui.GetColorU32(color),
            4f * ImGuiHelpers.GlobalScale, ImDrawFlags.None, t);
    }

    private static void DrawMacroTooltip(CachedMacro macro)
    {
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        var slotLabel = macro.IsActive ? string.Format(Loc.Get("Footer.Slot"), macro.GameSlot) : Loc.Get("Macros.Tooltip.Disabled");
        var displayName = string.IsNullOrEmpty(macro.Name) ? Loc.Get("Common.Unnamed") : macro.Name;
        ImGui.TextUnformatted($"{displayName}  ({slotLabel})");
        if (macro.Lines.Count > 0)
        {
            ImGui.Separator();
            foreach (var line in macro.Lines)
                ImGui.TextUnformatted(line);
        }
        ImGui.TextDisabled(Loc.Get("Macros.Tooltip.DragHint"));
        ImGui.EndTooltip();
    }

    private void OpenExportDialog()
    {
        if (!library.IsAvailable) return;
        var macros = library.List(macrosActiveSet);
        if (macros.Count == 0) return;

        exportSourceSet = macrosActiveSet;
        exportSelectedIds.Clear();
        foreach (var m in macros)
            exportSelectedIds.Add(m.Id);
        exportFormat = ExportFormat.JsonBundle;
        requestOpenExport = true;
    }

    private void RunExport()
    {
        var macros = library.List(exportSourceSet)
            .Where(m => exportSelectedIds.Contains(m.Id))
            .ToList();
        if (macros.Count == 0) return;

        if (exportFormat == ExportFormat.JsonBundle)
        {
            var setTag = exportSourceSet == MacroSet.Individual ? "personnel" : "communes";
            var defaultName = $"macrofolio_{setTag}_{DateTime.Now:yyyyMMdd_HHmm}";
            Plugin.FileDialogManager.SaveFileDialog(
                Loc.Get("Export.JsonDialogTitle"),
                ".json",
                defaultName,
                ".json",
                (success, path) =>
                {
                    if (!success) return;
                    WriteJsonBundle(macros, path);
                });
        }
        else
        {
            Plugin.FileDialogManager.OpenFolderDialog(
                Loc.Get("Export.TxtFolderDialogTitle"),
                (success, path) =>
                {
                    if (!success) return;
                    WriteTxtFiles(macros, path);
                });
        }
    }

    private void WriteJsonBundle(IReadOnlyList<CachedMacro> macros, string filePath)
    {
        try
        {
            var bundle = new MacroBundle
            {
                SchemaVersion = 1,
                ExportedBy = Plugin.ObjectTable.LocalPlayer?.Name.ToString(),
                ExportedAt = DateTimeOffset.UtcNow,
                Macros = macros.Select(m => new MacroBundleEntry
                {
                    Set = exportSourceSet,
                    Slot = m.GameSlot ?? -1,
                    Entry = new MacroEntry
                    {
                        Name = m.Name,
                        IconId = m.IconId,
                        Lines = new List<string>(m.Lines),
                    },
                }).ToList(),
            };
            var opts = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            };
            File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(bundle, opts));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to export JSON bundle to {Path}", filePath);
        }
    }

    private static void WriteTxtFiles(IReadOnlyList<CachedMacro> macros, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var seen = new HashSet<string>();
            foreach (var m in macros)
            {
                var baseName = SanitizeFileName(string.IsNullOrEmpty(m.Name) ? "sans-nom" : m.Name);
                var unique = baseName;
                var i = 2;
                while (!seen.Add(unique))
                    unique = $"{baseName}_{i++}";
                var path = Path.Combine(folder, unique + ".txt");
                File.WriteAllText(path, string.Join("\n", m.Lines));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to export TXT files into {Folder}", folder);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        var s = new string(chars).Trim();
        return string.IsNullOrEmpty(s) ? "macro" : s;
    }

    private void DuplicateMacroTo(CachedMacro macro, MacroSet targetSet)
    {
        library.Create(targetSet, macro.Name, macro.IconId, macro.Lines);
    }

    private void MoveMacroTo(CachedMacro macro, MacroSet targetSet)
    {
        library.Create(targetSet, macro.Name, macro.IconId, macro.Lines);
        library.Delete(macrosActiveSet, macro.Id);
    }

    private void ExportSingleAsTxt(CachedMacro macro)
    {
        var defaultName = SanitizeFileName(string.IsNullOrEmpty(macro.Name) ? "macro" : macro.Name);
        Plugin.FileDialogManager.SaveFileDialog(
            Loc.Get("Export.TxtSingleDialogTitle"),
            ".txt",
            defaultName,
            ".txt",
            (success, path) =>
            {
                if (!success) return;
                try
                {
                    File.WriteAllText(path, string.Join("\n", macro.Lines));
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "Failed to export single macro {Id}", macro.Id);
                }
            });
    }

    private void DrawExportPopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##export", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted(Loc.Get("Export.Title"));
        ImGui.Separator();

        var macros = library.List(exportSourceSet);
        ImGui.TextDisabled(string.Format(Loc.Get("Export.Source"),
            exportSourceSet == MacroSet.Individual ? IndividualSetLabel() : Loc.Get("Set.Shared"),
            macros.Count));

        ImGui.Spacing();
        ImGui.TextDisabled(Loc.Get("Export.Format"));
        if (ImGui.RadioButton(Loc.Get("Export.FormatJson") + "##exp_json",
                exportFormat == ExportFormat.JsonBundle))
            exportFormat = ExportFormat.JsonBundle;
        if (ImGui.RadioButton(Loc.Get("Export.FormatTxt") + "##exp_txt",
                exportFormat == ExportFormat.TxtIndividual))
            exportFormat = ExportFormat.TxtIndividual;

        ImGui.Spacing();
        ImGui.TextDisabled(Loc.Get("Export.Selection"));
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.Get("Export.SelectAll") + "##exp_all"))
            foreach (var m in macros) exportSelectedIds.Add(m.Id);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.Get("Export.SelectNone") + "##exp_none"))
            exportSelectedIds.Clear();

        var listH = 240f * ImGuiHelpers.GlobalScale;
        if (ImGui.BeginChild("##exp_list", new Vector2(380f * ImGuiHelpers.GlobalScale, listH), true))
        {
            foreach (var m in macros)
            {
                var checkedNow = exportSelectedIds.Contains(m.Id);
                var label = string.IsNullOrEmpty(m.Name) ? Loc.Get("Common.Unnamed") : m.Name;
                var status = m.IsActive ? "" : "  " + Loc.Get("Export.DisabledTag");
                if (ImGui.Checkbox($"{label}{status}##exp_{m.Id}", ref checkedNow))
                {
                    if (checkedNow) exportSelectedIds.Add(m.Id);
                    else exportSelectedIds.Remove(m.Id);
                }
            }
        }
        ImGui.EndChild();

        ImGui.Spacing();
        var count = exportSelectedIds.Count;
        ImGui.TextDisabled(string.Format(Loc.Get("Export.SelectedCount"), count));

        ImGui.Spacing();
        var canExport = count > 0;
        if (!canExport) ImGui.BeginDisabled();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.55f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.65f, 0.30f, 1f));
        if (ImGui.Button(Loc.Get("Toolbar.Export")))
        {
            RunExport();
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);
        if (!canExport) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##exp_cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void OpenImportDialog()
    {
        Plugin.FileDialogManager.OpenFileDialog(
            Loc.Get("Import.FileDialogTitle"),
            ".txt",
            (success, paths) =>
            {
                if (!success || paths.Count == 0) return;

                var items = new List<MacroImportWindow.ImportItem>(paths.Count);
                foreach (var path in paths)
                {
                    try
                    {
                        var content = File.ReadAllText(path);
                        var lines = content
                            .Replace("\r\n", "\n")
                            .Split('\n')
                            .ToList();
                        while (lines.Count > 0 && string.IsNullOrEmpty(lines[^1]))
                            lines.RemoveAt(lines.Count - 1);
                        if (lines.Count == 0) continue;
                        items.Add(new MacroImportWindow.ImportItem(
                            Path.GetFileName(path),
                            lines));
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warning(ex, "Failed to read {Path} during import", path);
                    }
                }

                if (items.Count > 0)
                    importWindow.OpenWith(macrosActiveSet, items);
            },
            selectionCountMax: 50,
            startPath: null,
            isModal: false);
    }

    private void DrawToolbar()
    {
        var spacing = 6f * ImGuiHelpers.GlobalScale;
        var cacheLabel = Loc.Get("Toolbar.CacheAll");
        var importLabel = Loc.Get("Toolbar.Import");
        var exportLabel = Loc.Get("Toolbar.Export");
        var cacheW = ImGui.CalcTextSize(cacheLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var importW = ImGui.CalcTextSize(importLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var exportW = ImGui.CalcTextSize(exportLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var totalW = cacheW + importW + exportW + spacing * 2;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - totalW);

        var hasActive = library.IsAvailable && library.ActiveCount(macrosActiveSet) > 0;
        if (!hasActive) ImGui.BeginDisabled();
        if (ImGui.Button(cacheLabel))
            requestCacheAll = true;
        if (!hasActive) ImGui.EndDisabled();

        ImGui.SameLine(0f, spacing);
        if (ImGui.Button(importLabel))
            OpenImportDialog();
        ImGui.SameLine(0f, spacing);
        if (ImGui.Button(exportLabel))
            OpenExportDialog();

        if (requestCacheAll)
        {
            ImGui.OpenPopup("##cache_all");
            requestCacheAll = false;
        }
        DrawCacheAllPopup();
    }

    private void DrawCacheAllPopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##cache_all", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var setLabel = macrosActiveSet == MacroSet.Individual ? IndividualSetLabel() : Loc.Get("Set.Shared");
        var count = library.ActiveCount(macrosActiveSet);
        ImGui.TextUnformatted(string.Format(Loc.Get("CacheAll.Confirm"), count, setLabel));
        ImGui.TextWrapped(Loc.Get("CacheAll.Detail"));
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.40f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.65f, 0.48f, 0.18f, 1f));
        if (ImGui.Button(Loc.Get("Toolbar.CacheAll")))
        {
            library.DisableAll(macrosActiveSet);
            selectedId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);
        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##cache_all"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void DrawSetTabs()
    {
        var characterName = Plugin.ObjectTable.LocalPlayer?.Name.ToString();
        var individualLabel = string.IsNullOrEmpty(characterName)
            ? Loc.Get("Set.CurrentCharacter")
            : characterName;

        var avail = ImGui.GetContentRegionAvail().X;
        var spacing = 6f * ImGuiHelpers.GlobalScale;
        var btnW = (avail - spacing) / 2f;
        var btnH = 28f * ImGuiHelpers.GlobalScale;

        DrawSetTabButton(individualLabel, MacroSet.Individual, new Vector2(btnW, btnH));
        ImGui.SameLine(0f, spacing);
        DrawSetTabButton(Loc.Get("Set.Shared"), MacroSet.Shared, new Vector2(btnW, btnH));
    }

    private void DrawSetTabButton(string label, MacroSet set, Vector2 size)
    {
        var isActive = macrosActiveSet == set;
        var bg = isActive ? MacroFolioTheme.AccentColor : MacroFolioTheme.ThemeButtonBg;
        var hover = isActive ? MacroFolioTheme.AccentColor : MacroFolioTheme.ThemeButtonHovered;
        var textColor = isActive ? MacroFolioTheme.ThemeWindowBg : MacroFolioTheme.AccentColor;

        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MacroFolioTheme.ThemeButtonActive);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * ImGuiHelpers.GlobalScale);

        if (ImGui.Button(label + "##set_" + set, size))
        {
            macrosActiveSet = set;
            selectedId = null;
        }

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();
    }

    private void DrawPlusCell(float cellSize)
    {
        ImGui.PushID("##plus");

        var cursorScreen = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (ImGui.InvisibleButton("##plus_btn", new Vector2(cellSize, cellSize)))
            createWizard.OpenFor(macrosActiveSet);

        var hovered = ImGui.IsItemHovered();
        var borderColor = hovered ? MacroFolioTheme.AccentColor : PlusBorderColor;

        drawList.AddRect(
            cursorScreen,
            cursorScreen + new Vector2(cellSize, cellSize),
            ImGui.GetColorU32(borderColor),
            4f * ImGuiHelpers.GlobalScale,
            ImDrawFlags.None,
            1.5f * ImGuiHelpers.GlobalScale);

        var plus = "+";
        var plusSize = ImGui.CalcTextSize(plus);
        var plusPos = cursorScreen + (new Vector2(cellSize, cellSize) - plusSize) / 2f;
        drawList.AddText(plusPos, ImGui.GetColorU32(borderColor), plus);

        if (hovered)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(Loc.Get("Macros.CreateTooltip"));
            ImGui.EndTooltip();
        }

        ImGui.PopID();
    }
    
    // --- Textes tab ---

    private TexteRP? editingTexte;
    private string texteTitleDraft = "";
    private string texteContentDraft = "";
    private bool texteSendDirectDraft;
    private string? requestDeleteTexteId;
    private string? texteFeedback;
    private double texteFeedbackUntil;

    private void DrawTextesTab()
    {
        if (editingTexte is not null)
        {
            DrawTexteEditor();
            return;
        }

        // Toolbar: new text (right-aligned).
        var newLabel = Loc.Get("Texte.New");
        var newW = ImGui.CalcTextSize(newLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - newW);
        if (ImGui.Button(newLabel))
        {
            var t = texteLibrary.Create(Loc.Get("Texte.DefaultName"));
            OpenTexte(t);
        }

        ImGuiHelpers.ScaledDummy(2f);
        if (!string.IsNullOrEmpty(texteFeedback) && ImGui.GetTime() < texteFeedbackUntil)
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 0.4f, 1f), texteFeedback);
        else
            ImGui.TextDisabled(Loc.Get("Texte.Hint"));
        ImGui.Separator();
        ImGui.Spacing();

        var textes = texteLibrary.ListAll();
        if (textes.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("Texte.Empty"));
            return;
        }

        if (ImGui.BeginChild("##textes_list"))
        {
            for (var i = 0; i < textes.Count; i++)
                DrawTexteRow(textes[i], i, textes.Count);
        }
        ImGui.EndChild();

        DrawTexteDeletePopup();
    }

    private void DrawTexteRow(TexteRP t, int index, int total)
    {
        ImGui.PushID(t.Id);

        var rowH = 30f * ImGuiHelpers.GlobalScale;
        var title = string.IsNullOrWhiteSpace(t.Title) ? Loc.Get("Common.Unnamed") : t.Title;

        var arrowW = 24f * ImGuiHelpers.GlobalScale;
        var editW = ImGui.CalcTextSize(Loc.Get("Common.Edit")).X + ImGui.GetStyle().FramePadding.X * 2;
        var delW = 28f * ImGuiHelpers.GlobalScale;
        var spacing = 4f * ImGuiHelpers.GlobalScale;

        // Explicit up/down arrows to reorder (more discoverable than drag).
        var atTop = index == 0;
        var atBottom = index == total - 1;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            if (atTop) ImGui.BeginDisabled();
            if (ImGui.Button(FontAwesomeIcon.ChevronUp.ToIconString() + "##up", new Vector2(arrowW, rowH)))
                texteLibrary.MoveUp(t.Id);
            if (atTop) ImGui.EndDisabled();

            ImGui.SameLine(0f, 1f);
            if (atBottom) ImGui.BeginDisabled();
            if (ImGui.Button(FontAwesomeIcon.ChevronDown.ToIconString() + "##down", new Vector2(arrowW, rowH)))
                texteLibrary.MoveDown(t.Id);
            if (atBottom) ImGui.EndDisabled();
        }

        // Click the title to send/pre-fill. Buttons on the right edit/delete.
        ImGui.SameLine(0f, spacing);
        if (ImGui.Selectable($"{title}##send", false, ImGuiSelectableFlags.None,
                new Vector2(ImGui.GetContentRegionAvail().X - editW - delW - spacing * 2, rowH)))
        {
            SendTexte(t);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(t.SendDirectly ? Loc.Get("Texte.Tooltip.SendDirect") : Loc.Get("Texte.Tooltip.Prefill"));
            ImGui.EndTooltip();
        }

        ImGui.SameLine(0f, spacing);
        if (ImGui.Button(Loc.Get("Common.Edit") + "##e"))
            OpenTexte(t);

        ImGui.SameLine(0f, spacing);
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + "##d", new Vector2(delW, 0)))
                requestDeleteTexteId = t.Id;
        }

        ImGui.PopID();
    }

    private void SendTexte(TexteRP t)
    {
        var chunks = ChatSenderService.Split(t.Content);
        if (chunks.Count == 0)
        {
            SetTexteFeedback(Loc.Get("Texte.Feedback.Empty"));
            return;
        }

        if (t.SendDirectly)
        {
            var queued = chatSender.SendDirect(t.Content);
            SetTexteFeedback(queued > 1
                ? string.Format(Loc.Get("Texte.Feedback.Queued"), queued, (int)ChatSenderService.SendIntervalSeconds)
                : string.Format(Loc.Get("Texte.Feedback.Sent"), queued));
        }
        else
        {
            // Pre-fill mode: copy the (split) text to the clipboard so the player
            // pastes it into the chat box and presses Enter. The native chat box
            // can't be filled programmatically without ToS-risky tricks.
            ImGui.SetClipboardText(string.Join("\n", chunks));
            SetTexteFeedback(string.Format(Loc.Get("Texte.Feedback.Copied"), chunks.Count));
        }
    }

    private void SetTexteFeedback(string message)
    {
        texteFeedback = message;
        texteFeedbackUntil = ImGui.GetTime() + 4.0;
    }

    private void OpenTexte(TexteRP t)
    {
        editingTexte = t;
        texteTitleDraft = t.Title;
        texteContentDraft = t.Content;
        texteSendDirectDraft = t.SendDirectly;
    }

    private void DrawTexteEditor()
    {
        if (editingTexte is null) return;

        // Header row: back + save.
        if (ImGui.Button("← " + Loc.Get("Common.Back")))
        {
            PersistTexte();
            editingTexte = null;
            return;
        }
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.Get("Texte.AutoSaved"));

        ImGui.Separator();
        ImGui.Spacing();

        // Title.
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##texte_title", Loc.Get("Texte.TitleHint"), ref texteTitleDraft, 80))
            PersistTexte();

        ImGui.Spacing();

        // Send mode toggle.
        if (ImGui.Checkbox(Loc.Get("Texte.SendDirect"), ref texteSendDirectDraft))
            PersistTexte();
        ImGui.SameLine();
        ImGuiComponentsHelpMarker(Loc.Get("Texte.SendDirect.Help"));

        ImGui.Spacing();

        // Content with live character counter and split preview.
        var chars = texteContentDraft.Length;
        var chunks = ChatSenderService.Split(texteContentDraft).Count;
        ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f),
            string.Format(Loc.Get("Texte.Counter"), chars, chunks));
        ImGui.TextDisabled(Loc.Get("Texte.EditorHint"));

        // The editor takes the upper half; a wrapped read-only preview fills the
        // lower half (ImGui's multiline input does not word-wrap, so long lines
        // scroll horizontally — the preview shows the full text reflowed).
        var avail = ImGui.GetContentRegionAvail().Y - 8f * ImGuiHelpers.GlobalScale;
        var editorH = Math.Max(100f * ImGuiHelpers.GlobalScale, avail * 0.5f);
        if (ImGui.InputTextMultiline("##texte_content", ref texteContentDraft,
                32768, new Vector2(-1, editorH)))
            PersistTexte();

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.TextDisabled(Loc.Get("Texte.Preview"));
        if (ImGui.BeginChild("##texte_preview", new Vector2(-1, 0), true))
        {
            if (string.IsNullOrEmpty(texteContentDraft))
                ImGui.TextDisabled("-");
            else
                ImGui.TextWrapped(texteContentDraft);
        }
        ImGui.EndChild();
    }

    private void PersistTexte()
    {
        if (editingTexte is null) return;
        editingTexte.Title = string.IsNullOrWhiteSpace(texteTitleDraft)
            ? Loc.Get("Texte.DefaultName")
            : texteTitleDraft.Trim();
        editingTexte.Content = texteContentDraft;
        editingTexte.SendDirectly = texteSendDirectDraft;
        texteLibrary.Update(editingTexte);
    }

    private void DrawTexteDeletePopup()
    {
        if (requestDeleteTexteId is not null)
        {
            ImGui.OpenPopup("##texte_delete");
        }

        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##texte_delete", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var t = requestDeleteTexteId is null ? null : texteLibrary.GetById(requestDeleteTexteId);
        var name = t is null || string.IsNullOrWhiteSpace(t.Title) ? Loc.Get("Common.Unnamed") : t.Title;
        ImGui.TextUnformatted(string.Format(Loc.Get("Texte.Delete.Confirm"), name));
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.22f, 0.22f, 1f));
        if (ImGui.Button(Loc.Get("Common.Delete") + "##texte_del_yes"))
        {
            if (requestDeleteTexteId is not null) texteLibrary.Delete(requestDeleteTexteId);
            requestDeleteTexteId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);
        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##texte_del_no"))
        {
            requestDeleteTexteId = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static void ImGuiComponentsHelpMarker(string text)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private static readonly Vector4 AshfallGold = new(0.831f, 0.686f, 0.416f, 1f);
    private static readonly Vector4 AshfallEmber = new(0.831f, 0.384f, 0.165f, 1f);

    private void DrawAboutTab()
    {
        var availW = ImGui.GetContentRegionAvail().X;

        ImGuiHelpers.ScaledDummy(20f);

        DrawCenteredAccentIcon(FontAwesomeIcon.Scroll, availW, scale: 1.6f);

        ImGuiHelpers.ScaledDummy(10f);

        CenteredAccentTitle(Constants.PluginName, availW, scale: 1.4f);

        ImGuiHelpers.ScaledDummy(4f);

        CenteredLine(
            $"v{Constants.PluginVersion}  ·  par {Constants.Author}",
            new Vector4(0.55f, 0.55f, 0.55f, 1f),
            availW);

        ImGuiHelpers.ScaledDummy(14f);

        var manifest = Plugin.PluginInterface.Manifest;
        if (!string.IsNullOrEmpty(manifest.Punchline))
            CenteredBlock(manifest.Punchline, new Vector4(0.75f, 0.75f, 0.75f, 1f), availW);

        if (!string.IsNullOrEmpty(manifest.Description)
            && !string.Equals(manifest.Description, manifest.Punchline, StringComparison.Ordinal))
        {
            ImGuiHelpers.ScaledDummy(4f);
            CenteredBlock(manifest.Description, new Vector4(0.55f, 0.55f, 0.55f, 1f), availW);
        }

        ImGuiHelpers.ScaledDummy(22f);

        CenteredLine(Loc.Get("About.Links"), new Vector4(0.5f, 0.5f, 0.5f, 1f), availW);
        ImGuiHelpers.ScaledDummy(6f);
        DrawAboutLinks(availW);

        ImGuiHelpers.ScaledDummy(22f);

        DrawLanguageSelector(availW);

        ImGuiHelpers.ScaledDummy(18f);

        CenteredLine(
            Loc.Get("About.SlashCommands"),
            new Vector4(0.5f, 0.5f, 0.5f, 1f),
            availW);

        ImGuiHelpers.ScaledDummy(20f);

        var sepStart = ImGui.GetCursorScreenPos();
        var sepW = 140f * ImGuiHelpers.GlobalScale;
        var sepX = sepStart.X + (availW - sepW) / 2f;
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(sepX, sepStart.Y),
            new Vector2(sepX + sepW, sepStart.Y),
            ImGui.GetColorU32(AshfallGold with { W = 0.18f }),
            1f);

        ImGuiHelpers.ScaledDummy(16f);

        var logoSize = 72f * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availW - logoSize) / 2f);
        var logoPos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(logoSize, logoSize));
        DrawAshfallCodexLogo(
            new Vector2(logoPos.X + logoSize / 2f, logoPos.Y + logoSize / 2f),
            logoSize);

        ImGuiHelpers.ScaledDummy(4f);

        const string brandName = "Ashfall Codex";
        var brandSz = ImGui.CalcTextSize(brandName);
        ImGui.PushStyleColor(ImGuiCol.Text, AshfallGold with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, AshfallEmber with { W = 0.22f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, AshfallEmber with { W = 0.34f });
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availW - brandSz.X) / 2f);
        if (ImGui.Selectable(brandName, false, ImGuiSelectableFlags.None, brandSz))
            Dalamud.Utility.Util.OpenLink("https://ashfall-codex.dev/");
        ImGui.PopStyleColor(3);

        ImGuiHelpers.ScaledDummy(16f);
    }

    private void DrawLanguageSelector(float availW)
    {
        CenteredLine(Loc.Get("About.Language").ToUpperInvariant(), new Vector4(0.5f, 0.5f, 0.5f, 1f), availW);
        ImGuiHelpers.ScaledDummy(6f);

        var current = Loc.AvailableLanguages
            .FirstOrDefault(l => string.Equals(l.Key, Loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase));
        var preview = string.IsNullOrEmpty(current.Value) ? current.Key ?? "English" : current.Value;

        var comboW = 200f * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, (availW - comboW) / 2f));
        ImGui.SetNextItemWidth(comboW);
        if (ImGui.BeginCombo("##lang", preview))
        {
            foreach (var (code, label) in Loc.AvailableLanguages)
            {
                var selected = string.Equals(code, Loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(label, selected) && !selected)
                {
                    Loc.SetLanguage(code);
                    Plugin.Config.UiLanguage = code;
                    Plugin.Config.Save();
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }


    private static void DrawCenteredAccentIcon(FontAwesomeIcon icon, float availW, float scale)
    {
        var iconStr = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var sz = ImGui.CalcTextSize(iconStr) * scale;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(0f, sz.Y));
        ImGui.GetWindowDrawList().AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize() * scale,
            new Vector2(pos.X + (availW - sz.X) / 2f, pos.Y),
            ImGui.GetColorU32(MacroFolioTheme.AccentColor),
            iconStr);
        ImGui.PopFont();
    }

    private static void CenteredAccentTitle(string text, float availW, float scale)
    {
        var sz = ImGui.CalcTextSize(text) * scale;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(0f, sz.Y));
        ImGui.GetWindowDrawList().AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize() * scale,
            new Vector2(pos.X + (availW - sz.X) / 2f, pos.Y),
            ImGui.GetColorU32(MacroFolioTheme.AccentColor),
            text);
    }

    private static void CenteredLine(string text, Vector4 color, float availW)
    {
        var sz = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availW - sz.X) / 2f);
        ImGui.TextColored(color, text);
    }
    
    private static void CenteredBlock(string text, Vector4 color, float availW)
    {
        var maxWrap = Math.Min(availW - 40f * ImGuiHelpers.GlobalScale, 520f * ImGuiHelpers.GlobalScale);
        var natural = ImGui.CalcTextSize(text);
        if (natural.X <= maxWrap)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availW - natural.X) / 2f);
            ImGui.TextColored(color, text);
        }
        else
        {
            var margin = (availW - maxWrap) / 2f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + margin);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + maxWrap);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(text);
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
        }
    }

    private static void DrawAboutLinks(float availW)
    {
        var btnW = 180f * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availW - btnW) / 2f);

        if (DrawAboutLinkButton(FontAwesomeIcon.Code, "GitHub", btnW))
            Dalamud.Utility.Util.OpenLink(Constants.GitHubUrl);
    }

    private static bool DrawAboutLinkButton(FontAwesomeIcon icon, string label, float width)
    {
        var fontSize = ImGui.GetFontSize() * 0.9f;
        var iconStr = icon.ToIconString();

        ImGui.PushFont(UiBuilder.IconFont);
        var iconSz = ImGui.CalcTextSize(iconStr) * 0.9f;
        ImGui.PopFont();

        var labelSz = ImGui.CalcTextSize(label) * 0.9f;
        var gap = 6f * ImGuiHelpers.GlobalScale;
        var totalW = iconSz.X + gap + labelSz.X;
        var btnH = 28f * ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.Button, MacroFolioTheme.ThemeButtonBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, MacroFolioTheme.ThemeButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MacroFolioTheme.ThemeButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * ImGuiHelpers.GlobalScale);

        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.Button($"##about_link_{label}", new Vector2(width, btnH));

        var dl = ImGui.GetWindowDrawList();
        var startX = pos.X + (width - totalW) / 2f;
        var textColor = ImGui.GetColorU32(MacroFolioTheme.AccentColor);
        dl.AddText(
            UiBuilder.IconFont,
            fontSize,
            new Vector2(startX, pos.Y + (btnH - iconSz.Y) / 2f),
            textColor,
            iconStr);
        dl.AddText(
            ImGui.GetFont(),
            fontSize,
            new Vector2(startX + iconSz.X + gap, pos.Y + (btnH - labelSz.Y) / 2f),
            textColor,
            label);

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        return clicked;
    }

    private static void DrawAshfallCodexLogo(Vector2 center, float size)
    {
        var dl = ImGui.GetWindowDrawList();
        var s = size / 512f;
        var ox = center.X - 256f * s;
        var oy = center.Y - 256f * s;
        var t = ImGui.GetTime();
        var cxVb = 261f;
        var cyVb = 256f;
        var logoCenter = new Vector2(ox + cxVb * s, oy + cyVb * s);
        var glowPulse = 0.5f + 0.3f * MathF.Sin((float)(t * Math.PI * 2.0 / 2.5));

        var haloR = 80f * s;
        for (var ring = 5; ring >= 0; ring--)
        {
            var r = haloR * (1f + ring * 0.5f);
            var a = 0.04f * glowPulse * (1f - ring / 6f);
            dl.AddCircleFilled(logoCenter, r,
                ImGui.GetColorU32(AshfallEmber with { W = a }), 48);
        }

        dl.AddCircle(new Vector2(ox + 256f * s, oy + 256f * s), 241f * s,
            ImGui.GetColorU32(AshfallGold with { W = 0.25f }), 64, MathF.Max(1.5f, 4.4f * s));

        var bookR = MathF.Max(2f, 11f * s);
        var bookMin = new Vector2(ox + 150f * s, oy + 117f * s);
        var bookMax = new Vector2(ox + 362f * s, oy + 395f * s);
        dl.AddRectFilled(bookMin, bookMax,
            ImGui.GetColorU32(new Vector4(0.075f, 0.075f, 0.082f, 1f)), bookR);
        dl.AddRect(bookMin, bookMax,
            ImGui.GetColorU32(AshfallGold with { W = 0.85f }), bookR, ImDrawFlags.None, MathF.Max(1.5f, 6.6f * s));

        dl.AddRectFilled(
            new Vector2(ox + 150f * s, oy + 117f * s),
            new Vector2(ox + 176f * s, oy + 395f * s),
            ImGui.GetColorU32(new Vector4(0.102f, 0.102f, 0.118f, 0.7f)), bookR);
        dl.AddLine(
            new Vector2(ox + 176f * s, oy + 125f * s),
            new Vector2(ox + 176f * s, oy + 387f * s),
            ImGui.GetColorU32(AshfallGold with { W = 0.3f }), MathF.Max(1f, 1.5f * s));

        var d1 = new Vector2(ox + cxVb * s, oy + 212f * s);
        var d2 = new Vector2(ox + 305f * s, oy + cyVb * s);
        var d3 = new Vector2(ox + cxVb * s, oy + 300f * s);
        var d4 = new Vector2(ox + 217f * s, oy + cyVb * s);
        var outerThk = MathF.Max(1.5f, 5.5f * s);
        var outerCol = ImGui.GetColorU32(AshfallEmber with { W = 0.85f });
        dl.AddLine(d1, d2, outerCol, outerThk);
        dl.AddLine(d2, d3, outerCol, outerThk);
        dl.AddLine(d3, d4, outerCol, outerThk);
        dl.AddLine(d4, d1, outerCol, outerThk);

        var i1 = new Vector2(ox + cxVb * s, oy + 229f * s);
        var i2 = new Vector2(ox + 288f * s, oy + cyVb * s);
        var i3 = new Vector2(ox + cxVb * s, oy + 283f * s);
        var i4 = new Vector2(ox + 234f * s, oy + cyVb * s);
        var innerThk = MathF.Max(1.2f, 3.7f * s);
        var innerCol = ImGui.GetColorU32(new Vector4(0.941f, 0.565f, 0.259f, 0.65f));
        dl.AddLine(i1, i2, innerCol, innerThk);
        dl.AddLine(i2, i3, innerCol, innerThk);
        dl.AddLine(i3, i4, innerCol, innerThk);
        dl.AddLine(i4, i1, innerCol, innerThk);

        var emberR = MathF.Max(2f, 11f * s);
        for (var g = 3; g >= 0; g--)
        {
            var r = emberR * (1f + g * 0.8f);
            var a = 0.12f * glowPulse * (1f - g / 4f);
            dl.AddCircleFilled(logoCenter, r,
                ImGui.GetColorU32(new Vector4(0.941f, 0.565f, 0.259f, a)), 32);
        }
        dl.AddCircleFilled(logoCenter, emberR,
            ImGui.GetColorU32(new Vector4(0.941f, 0.565f, 0.259f, 0.5f + 0.35f * glowPulse)), 32);

        var cThk = MathF.Max(1.2f, 2.9f * s);
        var cCol = ImGui.GetColorU32(AshfallGold with { W = 0.4f });
        dl.AddLine(new Vector2(ox + 187f * s, oy + 128f * s), new Vector2(ox + 187f * s, oy + 146f * s), cCol, cThk);
        dl.AddLine(new Vector2(ox + 187f * s, oy + 146f * s), new Vector2(ox + 206f * s, oy + 146f * s), cCol, cThk);
        dl.AddLine(new Vector2(ox + 342f * s, oy + 128f * s), new Vector2(ox + 342f * s, oy + 146f * s), cCol, cThk);
        dl.AddLine(new Vector2(ox + 342f * s, oy + 146f * s), new Vector2(ox + 324f * s, oy + 146f * s), cCol, cThk);
        dl.AddLine(new Vector2(ox + 187f * s, oy + 383f * s), new Vector2(ox + 187f * s, oy + 365f * s), cCol, cThk);
        dl.AddLine(new Vector2(ox + 187f * s, oy + 365f * s), new Vector2(ox + 206f * s, oy + 365f * s), cCol, cThk);
        dl.AddLine(new Vector2(ox + 342f * s, oy + 383f * s), new Vector2(ox + 342f * s, oy + 365f * s), cCol, cThk);
        dl.AddLine(new Vector2(ox + 342f * s, oy + 365f * s), new Vector2(ox + 324f * s, oy + 365f * s), cCol, cThk);
    }


    private void DrawLibraryTab()
    {
        if (editingPlanche is not null)
        {
            DrawPlancheEditor();
            return;
        }

        DrawLibraryToolbar();
        DrawLibrarySetTabs();
        ImGui.Spacing();

        var planches =plancheLibrary.ListForSet(libraryActiveSet);
        if (planches.Count == 0)
        {
            var setLabel = libraryActiveSet == MacroSet.Individual ? IndividualSetLabel() : Loc.Get("Set.Shared");
            ImGui.TextColored(MacroFolioTheme.AccentColor with { W = 0.6f },
                string.Format(Loc.Get("Planche.Empty"), setLabel));
            return;
        }

        if (ImGui.BeginChild("##planche_list", Vector2.Zero))
        {
            foreach (var planche in planches)
                DrawPlancheRow(planche);
        }
        ImGui.EndChild();
    }

    private void DrawLibraryToolbar()
    {
        var spacing = 6f * ImGuiHelpers.GlobalScale;
        var emptyLabel = Loc.Get("Planche.NewEmptyButton");
        var captureLabel = Loc.Get("Planche.CaptureButton");
        var emptyW = ImGui.CalcTextSize(emptyLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var captureW = ImGui.CalcTextSize(captureLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var totalW = emptyW + captureW + spacing;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - totalW);

        if (ImGui.Button(emptyLabel))
        {
            emptyCreateSet = libraryActiveSet;
            emptyCreateNameDraft = string.Format(Loc.Get("Planche.DefaultNameEmpty"), DateTime.Now.ToString("HH:mm"));
            requestOpenCreateEmpty = true;
        }
        ImGui.SameLine(0f, spacing);
        if (ImGui.Button(captureLabel))
        {
            captureTargetSet = libraryActiveSet;
            captureNameDraft = string.Format(Loc.Get("Planche.DefaultNameCapture"), DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            requestOpenCapture = true;
        }
    }

    private static string IndividualSetLabel()
    {
        var name = Plugin.ObjectTable.LocalPlayer?.Name.ToString();
        return string.IsNullOrEmpty(name) ? Loc.Get("Set.CurrentCharacter") : name;
    }

    private static void CenterNextPopup()
    {

        var viewport = ImGui.GetMainViewport();
        var center = viewport.WorkPos + viewport.WorkSize * 0.5f;
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
    }

    private void DrawLibrarySetTabs()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var spacing = 6f * ImGuiHelpers.GlobalScale;
        var btnW = (avail - spacing) / 2f;
        var btnH = 28f * ImGuiHelpers.GlobalScale;

        DrawLibraryTabButton(IndividualSetLabel(), MacroSet.Individual, new Vector2(btnW, btnH));
        ImGui.SameLine(0f, spacing);
        DrawLibraryTabButton(Loc.Get("Set.Shared"), MacroSet.Shared, new Vector2(btnW, btnH));
    }

    private void DrawLibraryTabButton(string label, MacroSet set, Vector2 size)
    {
        var isActive = libraryActiveSet == set;
        var bg = isActive ? MacroFolioTheme.AccentColor : MacroFolioTheme.ThemeButtonBg;
        var textColor = isActive ? MacroFolioTheme.ThemeWindowBg : MacroFolioTheme.AccentColor;

        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MacroFolioTheme.ThemeButtonActive);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * ImGuiHelpers.GlobalScale);

        if (ImGui.Button(label + "##libset_" + set, size))
            libraryActiveSet = set;

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();
    }

    private void DrawPlancheRow(Planche planche)
    {
        var rowH = 56f * ImGuiHelpers.GlobalScale;
        var pad = 8f * ImGuiHelpers.GlobalScale;
        var cursor = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(
            cursor,
            cursor + new Vector2(width, rowH),
            ImGui.GetColorU32(MacroFolioTheme.ThemeButtonBg),
            4f * ImGuiHelpers.GlobalScale);
        drawList.AddRect(
            cursor,
            cursor + new Vector2(width, rowH),
            ImGui.GetColorU32(MacroFolioTheme.ThemeBorder),
            4f * ImGuiHelpers.GlobalScale,
            ImDrawFlags.None,
            1f);

        ImGui.PushID(planche.Id);

        // Name (top line) + metadata (bottom line).
        ImGui.SetCursorScreenPos(cursor + new Vector2(pad, pad));
        ImGui.TextUnformatted(string.IsNullOrEmpty(planche.Name) ? Loc.Get("Common.Unnamed") : planche.Name);
        ImGui.SetCursorScreenPos(cursor + new Vector2(pad, pad + 22f * ImGuiHelpers.GlobalScale));
        var macroWord = planche.Entries.Count > 1 ? Loc.Get("Planche.MacrosPlural") : Loc.Get("Planche.MacroSingular");
        var meta = $"{planche.Entries.Count} {macroWord}  "
                 + $"·  {planche.UpdatedAt.ToLocalTime():dd/MM/yyyy HH:mm}";
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), meta);

        // Action buttons aligned right.
        var btnH = 26f * ImGuiHelpers.GlobalScale;
        var modW = 84f * ImGuiHelpers.GlobalScale;
        var applyW = 90f * ImGuiHelpers.GlobalScale;
        var delW = 90f * ImGuiHelpers.GlobalScale;
        var btnY = cursor.Y + (rowH - btnH) / 2f;
        var rightSpacing = 4f;
        var totalBtnW = modW + applyW + delW + rightSpacing * 2f;

        ImGui.SetCursorScreenPos(new Vector2(cursor.X + width - totalBtnW - 12f * ImGuiHelpers.GlobalScale, btnY));
        if (ImGui.Button(Loc.Get("Planche.Edit") + $"##mod_{planche.Id}", new Vector2(modW, btnH)))
            requestEditPlancheId = planche.Id;

        ImGui.SameLine(0f, rightSpacing);
        if (ImGui.Button(Loc.Get("Planche.Apply") + $"##apply_{planche.Id}", new Vector2(applyW, btnH)))
            requestApplyPlancheId = planche.Id;

        ImGui.SameLine(0f, rightSpacing);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.22f, 0.22f, 1f));
        if (ImGui.Button(Loc.Get("Planche.Delete") + $"##del_{planche.Id}", new Vector2(delW, btnH)))
            requestDeletePlancheId = planche.Id;
        ImGui.PopStyleColor(2);

        ImGui.PopID();

        ImGui.SetCursorScreenPos(cursor + new Vector2(0f, rowH + 6f * ImGuiHelpers.GlobalScale));
    }

    private void DispatchPlanchePopups()
    {
        if (requestOpenCapture)
        {
            ImGui.OpenPopup("##planche_capture");
            requestOpenCapture = false;
        }
        if (requestOpenCreateEmpty)
        {
            ImGui.OpenPopup("##planche_empty");
            requestOpenCreateEmpty = false;
        }
        if (requestApplyPlancheId is not null)
        {
            ImGui.OpenPopup("##planche_apply");
        }
        if (requestDeletePlancheId is not null)
        {
            ImGui.OpenPopup("##planche_delete");
        }
        if (requestEditPlancheId is not null)
        {
            var s = plancheLibrary.GetById(requestEditPlancheId);
            if (s is not null)
            {
                editingPlanche = ClonePlanche(s);
                editingNameDraft = editingPlanche.Name;
                libraryActiveSet = editingPlanche.TargetSet;
            }
            requestEditPlancheId = null;
        }
        if (pickingForSlot is not null)
        {
            ImGui.OpenPopup("##slot_picker");
        }

        DrawPlancheCapturePopup();
        DrawPlancheCreateEmptyPopup();
        DrawPlancheApplyPopup();
        DrawPlancheDeletePopup();
        DrawSlotPickerPopup();
    }

    private static Planche ClonePlanche(Planche s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        TargetSet = s.TargetSet,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
        Entries = s.Entries.Select(e => new PlancheEntry
        {
            Slot = e.Slot,
            Name = e.Name,
            IconId = e.IconId,
            Lines = new List<string>(e.Lines),
        }).ToList(),
    };

    private void DrawPlancheCreateEmptyPopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##planche_empty", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted(Loc.Get("Planche.NewEmptyTitle"));
        ImGui.Separator();

        ImGui.TextDisabled(Loc.Get("Planche.TargetSet"));
        if (ImGui.RadioButton($"{IndividualSetLabel()}##emp_ind", emptyCreateSet == MacroSet.Individual))
            emptyCreateSet = MacroSet.Individual;
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.Get("Set.Shared") + "##emp_sh", emptyCreateSet == MacroSet.Shared))
            emptyCreateSet = MacroSet.Shared;

        ImGui.Spacing();
        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##emp_name", Loc.Get("Planche.NameHint"), ref emptyCreateNameDraft, 64);

        ImGui.Spacing();
        var canCreate = !string.IsNullOrWhiteSpace(emptyCreateNameDraft);
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Get("Planche.CreateAndEdit")))
        {
            var created = plancheLibrary.Create(
                emptyCreateNameDraft.Trim(),
                emptyCreateSet,
                Array.Empty<PlancheEntry>());
            libraryActiveSet = emptyCreateSet;
            editingPlanche = ClonePlanche(created);
            editingNameDraft = editingPlanche.Name;
            emptyCreateNameDraft = "";
            ImGui.CloseCurrentPopup();
        }
        if (!canCreate) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##emp"))
        {
            emptyCreateNameDraft = "";
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }


    private void DrawPlancheEditor()
    {
        if (editingPlanche is null) return;

        var setLabel = editingPlanche.TargetSet == MacroSet.Individual
            ? IndividualSetLabel()
            : Loc.Get("Set.Shared");
        
        var backLabel = "← " + Loc.Get("Common.Back");
        var backW = ImGui.CalcTextSize(backLabel).X + ImGui.GetStyle().FramePadding.X * 2;

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), string.Format(Loc.Get("Planche.EditorTag"), setLabel));
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - backW);
        if (ImGui.Button(backLabel))
        {
            PersistPlanche();
            editingPlanche = null;
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##edit_name", Loc.Get("Planche.NameHint"), ref editingNameDraft, 64);
        if (ImGui.IsItemDeactivatedAfterEdit())
            PersistPlanche();

        ImGuiHelpers.ScaledDummy(2f);
        ImGui.TextDisabled(Loc.Get("Planche.AutoSaved"));

        ImGui.Separator();
        ImGui.Spacing();

        DrawPlancheEditorGrid();
    }

    private void PersistPlanche()
    {
        if (editingPlanche is null) return;
        if (!string.IsNullOrWhiteSpace(editingNameDraft))
            editingPlanche.Name = editingNameDraft.Trim();
        plancheLibrary.Update(editingPlanche);
    }

    private void DrawPlancheEditorGrid()
    {
        if (editingPlanche is null) return;

        const int totalSlots = 100;
        var scale = ImGuiHelpers.GlobalScale;
        var cellSize = GridCellSize * scale;
        var spacing = GridCellSpacing * scale;
        var cols = ComputeGridColumns(ImGui.GetContentRegionAvail().X, cellSize, spacing);
        var entryBySlot = editingPlanche.Entries.ToDictionary(e => e.Slot);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(spacing, spacing));
        for (var slot = 0; slot < totalSlots; slot++)
        {
            entryBySlot.TryGetValue(slot, out var entry);
            DrawEditorCell(slot, entry, cellSize);
            if ((slot + 1) % cols != 0)
                ImGui.SameLine();
        }
        ImGui.PopStyleVar();

        if (pendingPlancheSwap is { } pswap)
        {
            SwapPlancheSlots(pswap.From, pswap.To);
            pendingPlancheSwap = null;
            PersistPlanche();
        }

        ImGui.Spacing();
        var filled = editingPlanche.Entries.Count;
        ImGui.TextColored(MacroFolioTheme.AccentColor with { W = 0.7f },
            string.Format(Loc.Get("Planche.SlotsFilled"), filled));
    }

    private void SwapPlancheSlots(int from, int to)
    {
        if (editingPlanche is null || from == to) return;
        var a = editingPlanche.Entries.FirstOrDefault(e => e.Slot == from);
        var b = editingPlanche.Entries.FirstOrDefault(e => e.Slot == to);
        if (a is not null) a.Slot = to;
        if (b is not null) b.Slot = from;
    }

    private void DrawEditorCell(int slot, PlancheEntry? entry, float cellSize)
    {
        var isEmpty = entry is null;
        ImGui.PushID($"edit_slot_{slot}");

        var cursorScreen = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (ImGui.InvisibleButton("##cell", new Vector2(cellSize, cellSize)))
        {
            pickingForSlot = slot;
            pickerSearch = "";
        }

        // Drag a filled slot onto another to swap their contents.
        if (!isEmpty && ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
        {
            draggedPlancheSlot = slot;
            ImGui.SetDragDropPayload(PlancheSlotDragType, DragPayloadDummy);
            ImGui.TextUnformatted(string.IsNullOrEmpty(entry!.Name) ? string.Format(Loc.Get("Planche.SlotLabel"), slot) : entry.Name);
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginDragDropTarget())
        {
            if (AcceptPlancheDrop() && draggedPlancheSlot >= 0 && draggedPlancheSlot != slot)
                pendingPlancheSwap = (draggedPlancheSlot, slot);
            ImGui.EndDragDropTarget();
        }

        if (!isEmpty && entry!.IconId > 0)
        {
            try
            {
                var iconWrap = Plugin.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(entry.IconId))
                    .GetWrapOrDefault();
                if (iconWrap is not null)
                {
                    drawList.AddImage(
                        iconWrap.Handle,
                        cursorScreen,
                        cursorScreen + new Vector2(cellSize, cellSize));
                }
            }
            catch
            {
                // ignored
            }
        }

        // Slot number for empty cells.
        if (isEmpty)
        {
            var label = slot.ToString();
            var textSize = ImGui.CalcTextSize(label);
            var textPos = cursorScreen + (new Vector2(cellSize, cellSize) - textSize) / 2f;
            drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(0.45f, 0.45f, 0.45f, 1f)), label);
        }

        var borderColor = isEmpty ? MacroFolioTheme.ThemeBorder : BorderEnabledColor;
        var borderThickness = isEmpty ? 1f : 2f * ImGuiHelpers.GlobalScale;
        drawList.AddRect(
            cursorScreen,
            cursorScreen + new Vector2(cellSize, cellSize),
            ImGui.GetColorU32(borderColor),
            4f * ImGuiHelpers.GlobalScale,
            ImDrawFlags.None,
            borderThickness);

        if (ImGui.IsItemHovered() && !isEmpty)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted($"#{slot}  {(string.IsNullOrEmpty(entry!.Name) ? Loc.Get("Common.Unnamed") : entry.Name)}");
            if (entry.Lines.Count > 0)
            {
                ImGui.Separator();
                foreach (var line in entry.Lines.Take(15))
                    ImGui.TextUnformatted(line);
            }
            ImGui.EndTooltip();
        }

        ImGui.PopID();
    }

    private void DrawSlotPickerPopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##slot_picker", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (editingPlanche is null || pickingForSlot is null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var slot = pickingForSlot.Value;
        ImGui.TextUnformatted(Loc.Get("Planche.PickMacro"));
        ImGui.Separator();

        ImGui.SetNextItemWidth(360f * ImGuiHelpers.GlobalScale);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint("##pick_search", Loc.Get("Common.SearchHint"), ref pickerSearch, 64);

        var macros = library.List(editingPlanche.TargetSet);
        var query = pickerSearch.Trim();
        var listH = 280f * ImGuiHelpers.GlobalScale;

        if (ImGui.BeginChild("##picker_list", new Vector2(360f * ImGuiHelpers.GlobalScale, listH), true))
        {
            if (macros.Count == 0)
            {
                ImGui.TextDisabled(Loc.Get("Planche.NoMacroInSet"));
            }
            else
            {
                var any = false;
                foreach (var macro in macros)
                {
                    if (query.Length > 0 && !macro.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    any = true;
                    var label = string.IsNullOrEmpty(macro.Name) ? Loc.Get("Common.Unnamed") : macro.Name;
                    if (ImGui.Selectable($"{label}##pk_{macro.Id}"))
                    {
                        AssignMacroToPlancheSlot(slot, macro);
                        pickingForSlot = null;
                        ImGui.CloseCurrentPopup();
                    }
                }
                if (!any)
                    ImGui.TextDisabled(Loc.Get("Common.NoResult"));
            }
        }
        ImGui.EndChild();

        ImGui.Spacing();
        var hasEntry = editingPlanche.Entries.Any(e => e.Slot == slot);
        if (hasEntry)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.22f, 0.22f, 1f));
            if (ImGui.Button(Loc.Get("Planche.ClearSlot")))
            {
                editingPlanche.Entries.RemoveAll(e => e.Slot == slot);
                PersistPlanche();
                pickingForSlot = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.PopStyleColor(2);
            ImGui.SameLine();
        }
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##pk_cancel"))
        {
            pickingForSlot = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void AssignMacroToPlancheSlot(int slot, CachedMacro source)
    {
        if (editingPlanche is null) return;
        editingPlanche.Entries.RemoveAll(e => e.Slot == slot);
        editingPlanche.Entries.Add(new PlancheEntry
        {
            Slot = slot,
            Name = source.Name,
            IconId = source.IconId,
            Lines = new List<string>(source.Lines),
        });
        PersistPlanche();
    }

    private void DrawPlancheCapturePopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##planche_capture", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted(Loc.Get("Planche.CaptureTitle"));
        ImGui.Separator();

        ImGui.TextDisabled(Loc.Get("Planche.SourceSet"));
        if (ImGui.RadioButton($"{IndividualSetLabel()}##cap_ind", captureTargetSet == MacroSet.Individual))
            captureTargetSet = MacroSet.Individual;
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.Get("Set.Shared") + "##cap_sh", captureTargetSet == MacroSet.Shared))
            captureTargetSet = MacroSet.Shared;

        ImGui.Spacing();
        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##cap_name", Loc.Get("Planche.NameHint"), ref captureNameDraft, 64);

        var snapshot = library.SnapshotActive(captureTargetSet);
        ImGui.TextDisabled(string.Format(Loc.Get("Planche.CaptureCount"), snapshot.Count));

        ImGui.Spacing();
        var canCreate = !string.IsNullOrWhiteSpace(captureNameDraft) && snapshot.Count > 0;
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Get("Planche.CreateButton")))
        {
            plancheLibrary.Create(captureNameDraft.Trim(), captureTargetSet, snapshot);
            libraryActiveSet = captureTargetSet;
            captureNameDraft = "";
            ImGui.CloseCurrentPopup();
        }
        if (!canCreate) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##cap"))
        {
            captureNameDraft = "";
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawPlancheApplyPopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##planche_apply", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var planche =requestApplyPlancheId is null
            ? null
            : plancheLibrary.GetById(requestApplyPlancheId);
        if (planche is null)
        {
            ImGui.CloseCurrentPopup();
            requestApplyPlancheId = null;
            ImGui.EndPopup();
            return;
        }

        ImGui.TextUnformatted(string.Format(Loc.Get("Planche.Apply.Confirm"), planche.Name));
        ImGui.Separator();

        var setLabel = planche.TargetSet == MacroSet.Individual ? IndividualSetLabel() : Loc.Get("Set.Shared");
        ImGui.TextWrapped(string.Format(Loc.Get("Planche.Apply.Detail"), setLabel, planche.Entries.Count));

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.55f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.65f, 0.30f, 1f));
        if (ImGui.Button(Loc.Get("Planche.Apply.Now")))
        {
            library.ReplaceAll(planche.TargetSet, planche.Entries);
            requestApplyPlancheId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##apply"))
        {
            requestApplyPlancheId = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawPlancheDeletePopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##planche_delete", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var planche =requestDeletePlancheId is null
            ? null
            : plancheLibrary.GetById(requestDeletePlancheId);
        if (planche is null)
        {
            ImGui.CloseCurrentPopup();
            requestDeletePlancheId = null;
            ImGui.EndPopup();
            return;
        }

        ImGui.TextUnformatted(string.Format(Loc.Get("Planche.Delete.Confirm"), planche.Name));
        ImGui.Separator();
        ImGui.TextDisabled(Loc.Get("Planche.Delete.Detail"));

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.22f, 0.22f, 1f));
        if (ImGui.Button(Loc.Get("Common.Delete")))
        {
            plancheLibrary.Delete(planche.Id);
            requestDeletePlancheId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##del"))
        {
            requestDeletePlancheId = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
    
    private string footerNameBuffer = "";
    private string? footerNameBufferFor;

    private void DrawFooter(IReadOnlyList<CachedMacro> macros)
    {
        var active = macros.Count(m => m.IsActive);
        var cached = macros.Count - active;

        ImGui.Separator();

        var sel = selectedId is null ? null : macros.FirstOrDefault(m => m.Id == selectedId);
        if (sel is not null)
            DrawFooterQuickEdit(sel);
        else
        {
            footerNameBufferFor = null;
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(Loc.Get("Footer.NoSelection"));
        }

        var rightText = cached > 0
            ? string.Format(Loc.Get("Footer.ActiveCountWithCache"), active, library.Capacity, cached)
            : string.Format(Loc.Get("Footer.ActiveCount"), active, library.Capacity);
        var rightW = ImGui.CalcTextSize(rightText).X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - rightW);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(MacroFolioTheme.AccentColor, rightText);
    }

    private void DrawFooterQuickEdit(CachedMacro sel)
    {
        if (footerNameBufferFor != sel.Id)
        {
            footerNameBuffer = sel.Name;
            footerNameBufferFor = sel.Id;
        }

        var iconSize = new Vector2(24f * ImGuiHelpers.GlobalScale);
        var iconCursor = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        if (ImGui.InvisibleButton("##footer_icon", iconSize))
            iconPicker.Open(sel.IconId, id => library.SetIcon(macrosActiveSet, sel.Id, id));
        var hovered = ImGui.IsItemHovered();
        try
        {
            var wrap = Plugin.TextureProvider
                .GetFromGameIcon(new GameIconLookup(sel.IconId))
                .GetWrapOrDefault();
            if (wrap is not null)
                dl.AddImage(wrap.Handle, iconCursor, iconCursor + iconSize);
        }
        catch (Exception) { /* ignore: icon texture not ready */ }
        dl.AddRect(
            iconCursor, iconCursor + iconSize,
            ImGui.GetColorU32(hovered ? MacroFolioTheme.AccentColor : MacroFolioTheme.ThemeBorder),
            3f * ImGuiHelpers.GlobalScale, ImDrawFlags.None,
            hovered ? 2f * ImGuiHelpers.GlobalScale : 1f);
        if (hovered)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(Loc.Get("Footer.ChangeIcon"));
            ImGui.EndTooltip();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##footer_name", Loc.Get("Footer.NameHint"), ref footerNameBuffer, 64);
        if (ImGui.IsItemDeactivatedAfterEdit() && footerNameBuffer != (sel.Name ?? ""))
            library.Rename(macrosActiveSet, sel.Id, footerNameBuffer);

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(sel.IsActive ? string.Format(Loc.Get("Footer.Slot"), sel.GameSlot) : Loc.Get("Footer.Cached"));
    }
    
    private Brouillon? editingDraft;
    private string draftNameDraft = "";
    private readonly ActionListEditor draftActionEditor = new() { MaxLines = int.MaxValue };
    private uint draftIconDraft = 66001;

    private string? requestPublishDraftId;
    private string? requestDeleteDraftId;
    private MacroSet publishTargetSet = MacroSet.Individual;
    private int publishStartSlot;

    private void DrawDraftsTab()
    {
        if (editingDraft is not null)
        {
            DrawDraftEditor();
        }
        else
        {
            DrawDraftsToolbar();
            DrawDraftsList();
        }

        // Pop-ups handled at the root of the tab so they sit above everything.
        if (requestPublishDraftId is not null)
            ImGui.OpenPopup("##draft_publish");
        if (requestDeleteDraftId is not null)
            ImGui.OpenPopup("##draft_delete");

        DrawDraftPublishPopup();
        DrawDraftDeletePopup();
    }

    private void DrawDraftsToolbar()
    {
        var label = Loc.Get("Draft.NewButton");
        var w = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - w);
        if (ImGui.Button(label))
        {
            var draft = brouillonLibrary.Create(Loc.Get("Draft.DefaultName"), 66001u, Array.Empty<string>());
            OpenDraft(draft);
        }
        ImGui.Spacing();
    }

    private void DrawDraftsList()
    {
        var drafts = brouillonLibrary.ListAll();
        if (drafts.Count == 0)
        {
            ImGui.TextColored(MacroFolioTheme.AccentColor with { W = 0.6f },
                Loc.Get("Draft.EmptyList"));
            return;
        }

        if (!ImGui.BeginChild("##draft_list", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        for (var i = 0; i < drafts.Count; i++)
            DrawDraftRow(drafts[i], i, drafts.Count);

        ImGui.EndChild();
    }

    private void DrawDraftRow(Brouillon draft, int index, int total)
    {
        var rowH = 56f * ImGuiHelpers.GlobalScale;
        var cursor = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(
            cursor,
            cursor + new Vector2(width, rowH),
            ImGui.GetColorU32(new Vector4(0.16f, 0.16f, 0.16f, 1f)),
            6f * ImGuiHelpers.GlobalScale);
        dl.AddRect(
            cursor,
            cursor + new Vector2(width, rowH),
            ImGui.GetColorU32(MacroFolioTheme.ThemeBorder),
            6f * ImGuiHelpers.GlobalScale,
            ImDrawFlags.None,
            1f);

        ImGui.PushID(draft.Id);

        // Up/down reorder arrows at the left edge.
        var arrowW = 22f * ImGuiHelpers.GlobalScale;
        var arrowH = 22f * ImGuiHelpers.GlobalScale;
        var arrowX = cursor.X + 8f * ImGuiHelpers.GlobalScale;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            ImGui.SetCursorScreenPos(new Vector2(arrowX, cursor.Y + 6f * ImGuiHelpers.GlobalScale));
            var atTop = index == 0;
            if (atTop) ImGui.BeginDisabled();
            if (ImGui.Button(FontAwesomeIcon.ChevronUp.ToIconString() + "##d_up", new Vector2(arrowW, arrowH)))
                brouillonLibrary.Move(draft.Id, -1);
            if (atTop) ImGui.EndDisabled();

            ImGui.SetCursorScreenPos(new Vector2(arrowX, cursor.Y + 6f * ImGuiHelpers.GlobalScale + arrowH + 2f));
            var atBottom = index == total - 1;
            if (atBottom) ImGui.BeginDisabled();
            if (ImGui.Button(FontAwesomeIcon.ChevronDown.ToIconString() + "##d_down", new Vector2(arrowW, arrowH)))
                brouillonLibrary.Move(draft.Id, +1);
            if (atBottom) ImGui.EndDisabled();
        }

        var textX = arrowX + arrowW + 10f * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorScreenPos(new Vector2(textX, cursor.Y + 8f * ImGuiHelpers.GlobalScale));
        ImGui.TextUnformatted(string.IsNullOrEmpty(draft.Name) ? Loc.Get("Common.Unnamed") : draft.Name);

        var chunks = BrouillonLibrary.SplitForGame(draft.Lines).Count;
        var macroWord = chunks > 1 ? Loc.Get("Draft.MacrosPlural") : Loc.Get("Draft.MacroSingular");
        var meta = string.Format(Loc.Get("Draft.RowMeta"), draft.Lines.Count, chunks, macroWord)
                   + $"  ·  {draft.UpdatedAt.ToLocalTime():dd/MM/yyyy HH:mm}";
        ImGui.SetCursorScreenPos(new Vector2(textX, cursor.Y + 30f * ImGuiHelpers.GlobalScale));
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), meta);

        var btnH = 26f * ImGuiHelpers.GlobalScale;
        var modW = 84f * ImGuiHelpers.GlobalScale;
        var pubW = 90f * ImGuiHelpers.GlobalScale;
        var delW = 90f * ImGuiHelpers.GlobalScale;
        var rightSpacing = 4f;
        var totalBtnW = modW + pubW + delW + rightSpacing * 2f;
        var btnY = cursor.Y + (rowH - btnH) / 2f;

        ImGui.SetCursorScreenPos(new Vector2(cursor.X + width - totalBtnW - 12f * ImGuiHelpers.GlobalScale, btnY));
        if (ImGui.Button(Loc.Get("Draft.Edit") + $"##d_mod_{draft.Id}", new Vector2(modW, btnH)))
            OpenDraft(brouillonLibrary.GetById(draft.Id) ?? draft);

        ImGui.SameLine(0f, rightSpacing);
        if (ImGui.Button(Loc.Get("Draft.Publish") + $"##d_pub_{draft.Id}", new Vector2(pubW, btnH)))
        {
            requestPublishDraftId = draft.Id;
            publishTargetSet = MacroSet.Individual;
            publishStartSlot = 0;
        }

        ImGui.SameLine(0f, rightSpacing);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.22f, 0.22f, 1f));
        if (ImGui.Button(Loc.Get("Draft.Delete") + $"##d_del_{draft.Id}", new Vector2(delW, btnH)))
            requestDeleteDraftId = draft.Id;
        ImGui.PopStyleColor(2);

        ImGui.PopID();
        ImGui.SetCursorScreenPos(cursor + new Vector2(0f, rowH + 6f * ImGuiHelpers.GlobalScale));
    }

    private void OpenDraft(Brouillon draft)
    {
        editingDraft = draft;
        draftNameDraft = draft.Name;
        draftActionEditor.LoadFromLines(draft.Lines, draft.StartOffset, draft.Notes);
        draftIconDraft = draft.IconId > 0 ? draft.IconId : 66001u;
    }

    private void DrawDraftEditor()
    {
        if (editingDraft is null) return;

        var saveLabel = Loc.Get("Common.Save");
        var backLabel = "← " + Loc.Get("Common.Back");
        var saveW = ImGui.CalcTextSize(saveLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var backW = ImGui.CalcTextSize(backLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var spacing = 6f * ImGuiHelpers.GlobalScale;

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.Get("Draft.EditorTag"));
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - saveW - backW - spacing);
        if (ImGui.Button(backLabel))
        {
            StopDraftRecordingIfActive();
            editingDraft = null;
            return;
        }
        ImGui.SameLine(0f, spacing);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.55f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.65f, 0.30f, 1f));
        if (ImGui.Button(saveLabel))
        {
            StopDraftRecordingIfActive();
            editingDraft.Name = string.IsNullOrWhiteSpace(draftNameDraft)
                ? editingDraft.Name
                : draftNameDraft.Trim();
            editingDraft.IconId = draftIconDraft;
            editingDraft.Lines = draftActionEditor.ExportLines();
            editingDraft.Notes = draftActionEditor.ExportNotes();
            editingDraft.StartOffset = draftActionEditor.StartOffset;
            brouillonLibrary.Update(editingDraft);
            editingDraft = null;
        }
        ImGui.PopStyleColor(2);
        if (editingDraft is null) return;

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##draft_name", Loc.Get("Draft.NameHint"), ref draftNameDraft, 80);

        ImGui.Separator();
        ImGui.Spacing();

        var previewSize = new Vector2(40f * ImGuiHelpers.GlobalScale, 40f * ImGuiHelpers.GlobalScale);
        var iconCursor = ImGui.GetCursorScreenPos();
        var iconDl = ImGui.GetWindowDrawList();
        if (ImGui.InvisibleButton("##draft_icon_preview", previewSize))
            iconPicker.Open(draftIconDraft, id => draftIconDraft = id);
        var iconHovered = ImGui.IsItemHovered();
        try
        {
            var wrap = Plugin.TextureProvider
                .GetFromGameIcon(new GameIconLookup(draftIconDraft))
                .GetWrapOrDefault();
            if (wrap is not null)
                iconDl.AddImage(wrap.Handle, iconCursor, iconCursor + previewSize);
        }
        catch (Exception) { /* ignore: icon texture not ready */ }
        iconDl.AddRect(
            iconCursor,
            iconCursor + previewSize,
            ImGui.GetColorU32(iconHovered ? MacroFolioTheme.AccentColor : MacroFolioTheme.ThemeBorder),
            4f * ImGuiHelpers.GlobalScale,
            ImDrawFlags.None,
            iconHovered ? 2f * ImGuiHelpers.GlobalScale : 1f);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        if (ImGui.Button(Loc.Get("Common.ChooseIcon")))
            iconPicker.Open(draftIconDraft, id => draftIconDraft = id);

        ImGui.Spacing();

        DrawDraftRecordRow();
        ImGui.Spacing();

        var availWidth = ImGui.GetContentRegionAvail().X;
        draftActionEditor.Draw(availWidth);

        ImGuiHelpers.ScaledDummy(4f);
        var lines = draftActionEditor.LineCount;
        var chunks = lines == 0 ? 0
            : (lines <= BrouillonLibrary.ChunkMaxLines
                ? 1
                : 1 + (lines - 1) / BrouillonLibrary.ChunkUsefulLines);
        var lineWord = lines > 1 ? Loc.Get("Draft.LinesPlural") : Loc.Get("Draft.LineSingular");
        var macroWord2 = chunks > 1 ? Loc.Get("Draft.MacrosPlural") : Loc.Get("Draft.MacroSingular");
        ImGui.TextColored(MacroFolioTheme.AccentColor with { W = 0.7f },
            string.Format(Loc.Get("Draft.PublishedPreview"), lines, lineWord, chunks, macroWord2));
    }

    private void DrawDraftRecordRow()
    {
        if (!recordingService.IsAvailable)
        {
            ImGui.TextDisabled(Loc.Get("Draft.RecordUnavailable"));
            return;
        }

        if (recordingService.IsRecording)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.60f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.72f, 0.22f, 0.22f, 1f));
            if (ImGui.Button("■ " + Loc.Get("Draft.StopRecording")))
                ToggleDraftRecording();
            ImGui.PopStyleColor(2);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(new Vector4(0.95f, 0.4f, 0.4f, 1f),
                string.Format(Loc.Get("Draft.RecStatus"), recordingService.CapturedCount));
        }
        else
        {
            if (ImGui.Button("● " + Loc.Get("Draft.RecordEmotes")))
                ToggleDraftRecording();
            ImGui.SameLine();
            
            if (ImGui.Button("▶ " + Loc.Get("Draft.RunSequence")))
            {
                var allLines = draftActionEditor.ExportLines();
                var from = Math.Clamp(draftRunFromLine - 1, 0, Math.Max(0, allLines.Count - 1));
                var subset = allLines.Skip(from).ToList();
                var result = library.PreviewRun(MacroSet.Individual, subset);
                draftPreviewMessage = result switch
                {
                    MacroLibrary.PreviewResult.Empty => Loc.Get("Draft.Preview.Empty"),
                    MacroLibrary.PreviewResult.NoFreeSlot => Loc.Get("Draft.Preview.NoFreeSlot"),
                    _ => "",
                };
                draftPreviewMessageUntil = ImGui.GetTime() + 4.0;
            }

            // "From line N" selector so the preview can start anywhere.
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(Loc.Get("Draft.RunFrom"));
            ImGui.SameLine();
            var lineCount = Math.Max(1, draftActionEditor.LineCount);
            ImGui.SetNextItemWidth(70f * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("##draft_run_from", ref draftRunFromLine))
                draftRunFromLine = Math.Clamp(draftRunFromLine, 1, lineCount);

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            if (!string.IsNullOrEmpty(draftPreviewMessage) && ImGui.GetTime() < draftPreviewMessageUntil)
                ImGui.TextColored(new Vector4(0.90f, 0.55f, 0.20f, 1f), draftPreviewMessage);
            else
                ImGui.TextDisabled(Loc.Get("Draft.RecordHint"));
        }
    }

    private string draftPreviewMessage = "";
    private double draftPreviewMessageUntil;
    private int draftRunFromLine = 1;

    private void ToggleDraftRecording()
    {
        if (recordingService.IsRecording)
        {
            recordingService.OnCaptured -= OnDraftEmoteCaptured;
            recordingService.Stop();
        }
        else
        {
            recordingService.OnCaptured += OnDraftEmoteCaptured;
            recordingService.Start();
        }
    }

    private void StopDraftRecordingIfActive()
    {
        if (recordingService.IsRecording)
        {
            recordingService.OnCaptured -= OnDraftEmoteCaptured;
            recordingService.Stop();
        }
        recordingService.StopPlayback();
    }

    private void OnDraftEmoteCaptured(EmoteRecordingService.CapturedEmote e)
        => draftActionEditor.AppendCapturedEmote(e.Command, e.GapSeconds);

    private void DrawDraftPublishPopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##draft_publish", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var draft = requestPublishDraftId is null ? null : brouillonLibrary.GetById(requestPublishDraftId);
        if (draft is null)
        {
            ImGui.CloseCurrentPopup();
            requestPublishDraftId = null;
            ImGui.EndPopup();
            return;
        }

        var chunks = BrouillonLibrary.SplitForGame(draft.Lines);
        ImGui.TextUnformatted(string.Format(Loc.Get("Draft.Publish.Title"), draft.Name));
        ImGui.Separator();
        var pubLineWord = draft.Lines.Count > 1 ? Loc.Get("Draft.LinesPlural") : Loc.Get("Draft.LineSingular");
        var pubMacroWord = chunks.Count > 1 ? Loc.Get("Draft.ChainedMacrosPlural") : Loc.Get("Draft.ChainedMacroSingular");
        ImGui.TextColored(MacroFolioTheme.AccentColor with { W = 0.7f },
            string.Format(Loc.Get("Draft.Publish.Summary"), draft.Lines.Count, pubLineWord, chunks.Count, pubMacroWord));

        if (chunks.Count > 0)
        {
            ImGui.TextDisabled(string.Format(Loc.Get("Draft.Publish.Composition"), chunks.Count - 1, BrouillonLibrary.ChunkMaxLines) +
                               (chunks.Count > 1 ? string.Format(Loc.Get("Draft.Publish.CompositionTail"), chunks[^1].Count) : ""));
        }
        ImGui.Spacing();

        ImGui.TextDisabled(Loc.Get("Draft.Publish.TargetSet"));
        var indLabel = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? Loc.Get("Set.CurrentCharacter");
        if (ImGui.RadioButton($"{indLabel}##pub_ind", publishTargetSet == MacroSet.Individual))
            publishTargetSet = MacroSet.Individual;
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.Get("Set.Shared") + "##pub_sh", publishTargetSet == MacroSet.Shared))
            publishTargetSet = MacroSet.Shared;

        ImGui.Spacing();
        ImGui.TextDisabled(Loc.Get("Draft.Publish.StartSlot"));
        ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("##pub_slot", ref publishStartSlot, 1, 5);
        publishStartSlot = Math.Clamp(publishStartSlot, 0, MacroService.SlotsPerSet - 1);

        var endSlot = publishStartSlot + chunks.Count - 1;
        var fits = endSlot < MacroService.SlotsPerSet;
        var range = publishStartSlot == endSlot
            ? $"{publishStartSlot}"
            : $"{publishStartSlot}-{endSlot}";
        if (!fits)
        {
            ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1f),
                string.Format(Loc.Get("Draft.Publish.NotEnoughRoom"), range, MacroService.SlotsPerSet - 1));
        }
        else
        {
            // Detect existing macros that would be overwritten.
            var existing = library.List(publishTargetSet);
            var occupied = existing
                .Where(m => m.IsActive && m.GameSlot >= publishStartSlot && m.GameSlot <= endSlot)
                .Select(m => m.GameSlot!.Value)
                .ToList();
            if (occupied.Count > 0)
            {
                ImGui.TextColored(new Vector4(0.95f, 0.7f, 0.2f, 1f),
                    string.Format(Loc.Get("Draft.Publish.WillOverwrite"), occupied.Count, string.Join(", ", occupied)));
            }
            else
            {
                ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.3f, 1f),
                    string.Format(publishStartSlot == endSlot
                        ? Loc.Get("Draft.Publish.SlotFree")
                        : Loc.Get("Draft.Publish.SlotsFree"), range));
            }
        }

        ImGui.Spacing();
        if (!fits) ImGui.BeginDisabled();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.55f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.65f, 0.30f, 1f));
        if (ImGui.Button(Loc.Get("Draft.Publish.Now")))
        {
            library.PublishChunks(publishTargetSet, publishStartSlot, draft.Name, draft.IconId, chunks);
            requestPublishDraftId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);
        if (!fits) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##pub_cancel"))
        {
            requestPublishDraftId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void DrawDraftDeletePopup()
    {
        CenterNextPopup();
        if (!ImGui.BeginPopupModal("##draft_delete", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var draft = requestDeleteDraftId is null ? null : brouillonLibrary.GetById(requestDeleteDraftId);
        if (draft is null)
        {
            ImGui.CloseCurrentPopup();
            requestDeleteDraftId = null;
            ImGui.EndPopup();
            return;
        }

        ImGui.TextUnformatted(string.Format(Loc.Get("Draft.Delete.Confirm"), draft.Name));
        ImGui.Separator();
        ImGui.TextDisabled(Loc.Get("Draft.Delete.Detail"));

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.22f, 0.22f, 1f));
        if (ImGui.Button(Loc.Get("Common.Delete")))
        {
            brouillonLibrary.Delete(draft.Id);
            requestDeleteDraftId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get("Common.Cancel") + "##d_del_cancel"))
        {
            requestDeleteDraftId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
