using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using MacroFolio.Localization;
using MacroFolio.UI.Components;
using MacroFolio.Services;

namespace MacroFolio.UI;

public sealed class MacroImportWindow : MacroFolioWindowBase, IDisposable
{
    private readonly MacroLibrary library;
    private readonly IconPickerWindow iconPicker;

    private readonly List<ImportItem> queue = new();
    private int currentIndex;

    private MacroSet targetSet = MacroSet.Individual;
    private uint draftIconId = 66001;
    private string draftName = "";
    private string draftLinesPreview = "";

    public MacroImportWindow(MacroLibrary library, IconPickerWindow iconPicker)
        : base(
            Loc.Get("Import.Title") + "###MacroFolioImport",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.library = library;
        this.iconPicker = iconPicker;
        IsOpen = false;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700f, 200f),
            MaximumSize = new Vector2(700f, float.MaxValue),
        };
        PositionCondition = ImGuiCond.Appearing;
    }

    public void Dispose() { }

    public void OpenWith(MacroSet defaultSet, IEnumerable<ImportItem> items)
    {
        queue.Clear();
        queue.AddRange(items);
        if (queue.Count == 0) return;

        currentIndex = 0;
        targetSet = defaultSet;
        LoadCurrent();
        IsOpen = true;
    }

    public override void OnOpen()
    {
        var viewport = ImGui.GetMainViewport();
        var center = viewport.WorkPos + viewport.WorkSize * 0.5f;
        Position = new Vector2(center.X - 350f, center.Y - 360f);
    }

    private void LoadCurrent()
    {
        if (currentIndex >= queue.Count) return;

        var item = queue[currentIndex];
        draftName = Path.GetFileNameWithoutExtension(item.FileName);
        if (draftName.Length > 64) draftName = draftName[..64];

        draftIconId = 66001;
        draftLinesPreview = string.Join("\n", item.Lines);
    }

    protected override void DrawContents()
    {
        if (currentIndex >= queue.Count)
        {
            ImGui.TextUnformatted(Loc.Get("Import.NoFilesPending"));
            if (Buttons.Primary(Loc.Get("Common.Close")))
                IsOpen = false;
            return;
        }

        var availWidth = ImGui.GetContentRegionAvail().X;
        var item = queue[currentIndex];

        DrawHeader(item, availWidth);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(4f);

        DrawTargetSetSelector();
        ImGuiHelpers.ScaledDummy(8f);

        DrawRecapBlock(availWidth);
        ImGuiHelpers.ScaledDummy(8f);

        DrawIconBlock();
        ImGuiHelpers.ScaledDummy(8f);

        DrawNameBlock(availWidth);
        ImGuiHelpers.ScaledDummy(10f);

        DrawActions();
    }

    private void DrawHeader(ImportItem item, float availWidth)
    {
        ImGuiHelpers.ScaledDummy(4f);
        var iconStr = FontAwesomeIcon.FileImport.ToIconString();
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            var iconSz = ImGui.CalcTextSize(iconStr);
            var pos = ImGui.GetCursorScreenPos();
            var iconX = pos.X + (availWidth - iconSz.X) / 2f;
            ImGui.Dummy(new Vector2(0, iconSz.Y));
            ImGui.GetWindowDrawList().AddText(
                new Vector2(iconX, pos.Y),
                ImGui.GetColorU32(MacroFolioTheme.AccentColor),
                iconStr);
        }
        ImGuiHelpers.ScaledDummy(4f);

        var titleText = string.Format(Loc.Get("Import.FileCounter"), currentIndex + 1, queue.Count);
        var titleSize = ImGui.CalcTextSize(titleText);
        ImGui.SetCursorPosX((availWidth - titleSize.X) / 2f);
        ImGui.TextUnformatted(titleText);

        var sub = item.FileName;
        var subSize = ImGui.CalcTextSize(sub);
        ImGui.SetCursorPosX((availWidth - subSize.X) / 2f);
        ImGui.TextColored(MacroFolioTheme.TextSecondary, sub);
    }

    private void DrawTargetSetSelector()
    {
        ImGui.TextDisabled(Loc.Get("Import.TargetSet"));
        var indLabel = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? Loc.Get("Set.CurrentCharacter");
        var picked = SegmentedControl.Draw("imp_set", [indLabel, Loc.Get("Set.Shared")],
            targetSet == MacroSet.Individual ? 0 : 1);
        targetSet = picked == 0 ? MacroSet.Individual : MacroSet.Shared;
    }

    private void DrawRecapBlock(float availWidth)
    {
        var item = queue[currentIndex];
        var lineCount = item.Lines.Count;
        var over = lineCount > MacroService.MaxLinesPerMacro;

        ImGui.TextDisabled(string.Format(Loc.Get("Import.Recap"), lineCount));

        var preview = draftLinesPreview;
        ImGui.InputTextMultiline(
            "##imp_recap",
            ref preview,
            32768,
            new Vector2(availWidth, 140f * ImGuiHelpers.GlobalScale),
            ImGuiInputTextFlags.ReadOnly);

        if (over)
        {
            ImGui.TextColored(
                new Vector4(0.9f, 0.5f, 0.2f, 1f),
                string.Format(Loc.Get("Import.OverLimit"), MacroService.MaxLinesPerMacro));
        }
    }

    private void DrawIconBlock()
    {
        ImGui.TextDisabled(Loc.Get("Import.Icon"));
        ImGuiHelpers.ScaledDummy(2f);

        var previewSize = new Vector2(56f * ImGuiHelpers.GlobalScale, 56f * ImGuiHelpers.GlobalScale);
        var dl = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();

        if (ImGui.InvisibleButton("##imp_icon_preview", previewSize))
            iconPicker.Open(draftIconId, id => draftIconId = id);
        var hovered = ImGui.IsItemHovered();

        try
        {
            var wrap = Plugin.TextureProvider
                .GetFromGameIcon(new GameIconLookup(draftIconId))
                .GetWrapOrDefault();
            if (wrap is not null)
                dl.AddImage(wrap.Handle, cursor, cursor + previewSize);
        }
        catch (Exception) { /* ignore: icon texture not ready */ }
        dl.AddRect(
            cursor, cursor + previewSize,
            ImGui.GetColorU32(hovered ? MacroFolioTheme.AccentColor : MacroFolioTheme.ThemeBorder),
            4f * ImGuiHelpers.GlobalScale, ImDrawFlags.None,
            hovered ? 2f * ImGuiHelpers.GlobalScale : 1f);

        ImGui.SameLine();
        ImGui.BeginGroup();
        if (Buttons.Quiet(Loc.Get("Common.ChooseIcon")))
            iconPicker.Open(draftIconId, id => draftIconId = id);
        ImGui.TextDisabled($"#{draftIconId}");
        ImGui.EndGroup();
    }

    private void DrawNameBlock(float availWidth)
    {
        ImGui.TextDisabled(Loc.Get("Import.Name"));
        ImGui.SetNextItemWidth(availWidth);
        ImGui.InputTextWithHint("##imp_name", Loc.Get("Import.NameHint"), ref draftName, 64);
    }

    private void DrawActions()
    {
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(6f);

        var btnH = 28f * ImGuiHelpers.GlobalScale;
        var importLabel = queue.Count > 1 ? Loc.Get("Import.CommitOne") : Loc.Get("Import.Commit");
        var skipLabel = Loc.Get("Import.Skip");
        var cancelLabel = Loc.Get("Import.CancelAll");

        var importW = Buttons.PrimaryWidth(importLabel);
        var skipW = Buttons.TextWidth(skipLabel);
        var cancelW = Buttons.TextWidth(cancelLabel);
        var spacing = 8f * ImGuiHelpers.GlobalScale;

        // Cancelling the queue discards nothing already imported: a quiet button is enough.
        if (Buttons.Quiet(cancelLabel, cancelW, btnH))
        {
            queue.Clear();
            currentIndex = 0;
            IsOpen = false;
            return;
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - importW - skipW - spacing);
        if (Buttons.Quiet(skipLabel, skipW, btnH))
            Advance();

        ImGui.SameLine(0f, spacing);
        var canImport = !string.IsNullOrWhiteSpace(draftName) && queue[currentIndex].Lines.Count > 0;
        if (!canImport) ImGui.BeginDisabled();
        if (Buttons.Primary(importLabel, importW, btnH))
        {
            CommitCurrent();
            Advance();
        }
        if (!canImport) ImGui.EndDisabled();
    }

    private void CommitCurrent()
    {
        var item = queue[currentIndex];
        var lines = item.Lines.Take(MacroService.MaxLinesPerMacro);
        library.Create(targetSet, draftName.Trim(), draftIconId, lines);
    }

    private void Advance()
    {
        currentIndex++;
        if (currentIndex >= queue.Count)
        {
            queue.Clear();
            currentIndex = 0;
            IsOpen = false;
            return;
        }
        LoadCurrent();
    }

    public sealed record ImportItem(string FileName, List<string> Lines);
}
