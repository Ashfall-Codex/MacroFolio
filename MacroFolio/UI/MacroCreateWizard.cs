using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using MacroFolio.Localization;
using MacroFolio.UI.Components;
using MacroFolio.Services;

namespace MacroFolio.UI;

public sealed class MacroCreateWizard : MacroFolioWindowBase, IDisposable
{
    private const int MaxNameLength = 64;
    private const int MaxLines = 15;

    private readonly MacroLibrary library;
    private readonly IconPickerWindow iconPicker;
    private readonly ActionListEditor actionEditor = new() { MaxLines = MaxLines };

    private MacroSet targetSet = MacroSet.Individual;
    private string? editingMacroId;

    private uint draftIconId = 66001;
    private string draftName = "";

    public MacroCreateWizard(MacroLibrary library, IconPickerWindow iconPicker)
        : base(
            Loc.Get("Wizard.TitleNew") + "###MacroFolioCreate",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking)
    {
        this.library = library;
        this.iconPicker = iconPicker;
        IsOpen = false;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520f, 480f),
            MaximumSize = new Vector2(1400f, 1200f),
        };
        Size = new Vector2(600, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
        PositionCondition = ImGuiCond.Appearing;
    }

    public void Dispose() { }

    public void OpenFor(MacroSet set)
    {
        targetSet = set;
        editingMacroId = null;
        draftIconId = 66001u;
        draftName = "";
        actionEditor.Reset();
        WindowName = Loc.Get("Wizard.TitleNew") + "###MacroFolioCreate";
        IsOpen = true;
    }

    public void OpenForEdit(MacroSet set, Models.CachedMacro macro)
    {
        targetSet = set;
        editingMacroId = macro.Id;

        draftIconId = macro.IconId > 0 ? macro.IconId : 66001u;
        draftName = macro.Name;
        actionEditor.LoadFromLines(macro.Lines, macro.StartOffset, macro.Notes);

        WindowName = Loc.Get("Wizard.TitleEdit") + "###MacroFolioCreate";
        IsOpen = true;
    }

    public override void OnOpen()
    {
        var viewport = ImGui.GetMainViewport();
        var winSize = new Vector2(600, 620);
        Position = new Vector2(
            viewport.WorkPos.X + (viewport.WorkSize.X - winSize.X) / 2f,
            viewport.WorkPos.Y + (viewport.WorkSize.Y - winSize.Y) / 2f);
    }

    protected override void DrawContents()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;
        var navHeight = 44f * ImGuiHelpers.GlobalScale;

        DrawIconAndName();
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(4f);

        if (ImGui.BeginChild("##wizard_actions", new Vector2(0, -navHeight)))
            actionEditor.Draw(ImGui.GetContentRegionAvail().X);
        ImGui.EndChild();

        DrawBottomBar(availWidth);
    }

    private void DrawIconAndName()
    {
        ImGuiHelpers.ScaledDummy(2f);

        var iconSize = new Vector2(52f * ImGuiHelpers.GlobalScale);
        var cursor = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##wiz_icon", iconSize))
            iconPicker.Open(draftIconId, id => draftIconId = id);
        var hovered = ImGui.IsItemHovered();
        DrawIconImageAt(cursor, draftIconId, iconSize, fallbackLabel: "?");
        if (hovered)
        {
            ImGui.GetWindowDrawList().AddRect(
                cursor, cursor + iconSize,
                ImGui.GetColorU32(MacroFolioTheme.AccentColor),
                4f * ImGuiHelpers.GlobalScale, ImDrawFlags.None, 2f * ImGuiHelpers.GlobalScale);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(Loc.Get("Common.ChooseIcon"));
            ImGui.EndTooltip();
        }

        ImGui.SameLine();
        ImGui.BeginGroup();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##wiz_name", Loc.Get("Wizard.NameHint"), ref draftName, MaxNameLength);

        var setLabel = targetSet == MacroSet.Individual
            ? (Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? Loc.Get("Set.CurrentCharacter"))
            : Loc.Get("Set.Shared");
        ImGui.TextDisabled($"{setLabel}  ·  #{draftIconId}  ·  {draftName.Length}/{MaxNameLength}");

        ImGui.EndGroup();

        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawBottomBar(float availWidth)
    {
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(4f);

        var btnW = 120f * ImGuiHelpers.GlobalScale;

        if (Buttons.Quiet(Loc.Get("Common.Cancel") + "##wiz_cancel", btnW))
            IsOpen = false;

        ImGui.SameLine();
        var finishX = availWidth - btnW;
        if (finishX > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(finishX);

        var finishLabel = (editingMacroId is not null ? Loc.Get("Common.Save") : Loc.Get("Common.Create")) + "##wiz_finish";
        var disabled = string.IsNullOrWhiteSpace(draftName);
        if (disabled) ImGui.BeginDisabled();
        if (Buttons.Primary(finishLabel, btnW))
            Finish();
        if (disabled) ImGui.EndDisabled();
    }

    private static void DrawIconImageAt(Vector2 cursor, uint iconId, Vector2 size, string fallbackLabel)
    {
        var dl = ImGui.GetWindowDrawList();

        var rendered = false;
        if (iconId > 0)
        {
            try
            {
                var wrap = Plugin.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(iconId))
                    .GetWrapOrDefault();
                if (wrap is not null)
                {
                    dl.AddImage(wrap.Handle, cursor, cursor + size);
                    rendered = true;
                }
            }
            catch
            {
                // ignore
            }
        }

        if (!rendered)
        {
            var textSize = ImGui.CalcTextSize(fallbackLabel);
            dl.AddText(
                cursor + (size - textSize) / 2f,
                ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.6f, 1f)),
                fallbackLabel);
        }

        dl.AddRect(
            cursor,
            cursor + size,
            ImGui.GetColorU32(MacroFolioTheme.ThemeBorder),
            4f * ImGuiHelpers.GlobalScale,
            ImDrawFlags.None,
            1f);
    }

    private void Finish()
    {
        var lines = actionEditor.ExportLines();
        var notes = actionEditor.ExportNotes();
        if (editingMacroId is not null)
            library.UpdateMacro(targetSet, editingMacroId, draftName.Trim(), draftIconId, lines, actionEditor.StartOffset, notes);
        else
            library.Create(targetSet, draftName.Trim(), draftIconId, lines, actionEditor.StartOffset, notes);
        IsOpen = false;
    }
}
