using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using MacroFolio.Localization;

namespace MacroFolio.UI.Components;


public static class SearchField
{
    public static bool Draw(string id, ref string text, string hint, float width, int maxLength = 64,
        bool focusOnAppear = false)
    {
        var style = ImGui.GetStyle();
        var iconWidth = Buttons.IconWidth(FontAwesomeIcon.Search) - style.FramePadding.X * 2f;
        var clearWidth = text.Length > 0 ? Buttons.IconWidth(FontAwesomeIcon.Times) + style.ItemSpacing.X : 0f;

        ImGui.AlignTextToFramePadding();
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            ImGui.TextColored(MacroFolioTheme.TextDim, FontAwesomeIcon.Search.ToIconString());
        ImGui.SameLine();

        ImGui.SetNextItemWidth(Math.Max(width - iconWidth - style.ItemSpacing.X - clearWidth, 40f));
        if (focusOnAppear && ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        var changed = ImGui.InputTextWithHint("##" + id, hint, ref text, maxLength);

        if (ImGui.IsItemActive() && ImGui.IsKeyPressed(ImGuiKey.Escape) && text.Length > 0)
        {
            text = string.Empty;
            changed = true;
        }

        if (text.Length > 0)
        {
            ImGui.SameLine();
            if (Buttons.QuietIcon(FontAwesomeIcon.Times, id + "_clear", Loc.Get("Search.Clear")))
            {
                text = string.Empty;
                changed = true;
            }
        }

        return changed;
    }
}
