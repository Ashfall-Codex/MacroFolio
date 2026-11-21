using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace MacroFolio.UI.Components;

public static class Buttons
{
    public static bool Primary(string label, float width = 0f, float height = 0f)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, MacroFolioTheme.PrimaryBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, MacroFolioTheme.PrimaryHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MacroFolioTheme.PrimaryActive);
        ImGui.PushStyleColor(ImGuiCol.Text, MacroFolioTheme.PrimaryText);
        var clicked = ImGui.Button(label, new Vector2(width, height));
        ImGui.PopStyleColor(4);
        return clicked;
    }

    public static bool Tinted(string label, float width = 0f, float height = 0f)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, MacroFolioTheme.TextStrong);
        var clicked = Quiet(label, width, height);
        ImGui.PopStyleColor();
        return clicked;
    }

    public static bool Quiet(string label, float width = 0f, float height = 0f)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, MacroFolioTheme.AccentColor with { W = 0.12f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MacroFolioTheme.AccentColor with { W = 0.22f });
        var clicked = ImGui.Button(label, new Vector2(width, height));
        ImGui.PopStyleColor(3);
        return clicked;
    }


    public static bool Destructive(string label, float width = 0f, float height = 0f)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, MacroFolioTheme.DangerColor);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, MacroFolioTheme.DangerColor with { W = 0.16f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MacroFolioTheme.DangerColor with { W = 0.28f });
        var clicked = ImGui.Button(label, new Vector2(width, height));
        ImGui.PopStyleColor(4);
        return clicked;
    }

    public static bool DestructiveFilled(string label, float width = 0f, float height = 0f)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, MacroFolioTheme.DangerButtonBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, MacroFolioTheme.DangerButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MacroFolioTheme.DangerButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.Text, MacroFolioTheme.TextStrong);
        var clicked = ImGui.Button(label, new Vector2(width, height));
        ImGui.PopStyleColor(4);
        return clicked;
    }

    public static bool QuietIcon(FontAwesomeIcon icon, string id, string? tooltip = null, Vector4? color = null,
        Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, (color ?? MacroFolioTheme.AccentColor) with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, (color ?? MacroFolioTheme.AccentColor) with { W = 0.24f });
        if (color is { } c) ImGui.PushStyleColor(ImGuiCol.Text, c);
        bool clicked;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            clicked = ImGui.Button(icon.ToIconString() + "##" + id, size);
        ImGui.PopStyleColor(color is null ? 3 : 4);

        if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    public static float TextWidth(string label)
        => ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f;

    public static float PrimaryWidth(string label)
        => ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 4f;

    public static float IconWidth(FontAwesomeIcon icon)
    {
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            return ImGui.CalcTextSize(icon.ToIconString()).X + ImGui.GetStyle().FramePadding.X * 2f;
    }

    public static bool EscapePressed()
        => ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape);
}
