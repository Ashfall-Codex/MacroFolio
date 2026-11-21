using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace MacroFolio.UI.Components;


public static class EmptyState
{
    public static bool Draw(FontAwesomeIcon icon, string title, string? body = null, string? actionLabel = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var avail = ImGui.GetContentRegionAvail();
        var startX = ImGui.GetCursorPosX();
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var wrapWidth = MathF.Max(80f * scale, MathF.Min(avail.X - 24f * scale, 320f * scale));
        var bodyLines = string.IsNullOrEmpty(body) ? [] : WrapLines(body, wrapWidth);
        var symbolBlock = 56f * scale;
        var actionHeight = actionLabel != null ? ImGui.GetFrameHeightWithSpacing() + 10f * scale : 0f;
        var blockHeight = symbolBlock + lineHeight + 4f * scale + bodyLines.Count * lineHeight + actionHeight;

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + MathF.Max(0f, (avail.Y - blockHeight) * 0.42f));

        var iconStr = icon.ToIconString();
        var center = ImGui.GetCursorScreenPos() + new Vector2(avail.X / 2f, symbolBlock / 2f - 4f * scale);
        ImGui.PushFont(UiBuilder.IconFont);
        var iconSize = ImGui.CalcTextSize(iconStr) * 1.8f;
        ImGui.GetWindowDrawList().AddText(UiBuilder.IconFont, ImGui.GetFontSize() * 1.8f, center - iconSize / 2f,
            ImGui.GetColorU32(MacroFolioTheme.TextDim), iconStr);
        ImGui.PopFont();
        ImGui.Dummy(new Vector2(avail.X, symbolBlock));

        Centered(title, MacroFolioTheme.TextStrong, startX, avail.X);
        ImGuiHelpers.ScaledDummy(4f);
        foreach (var line in bodyLines)
            Centered(line, MacroFolioTheme.TextSecondary, startX, avail.X);

        if (actionLabel == null) return false;

        ImGuiHelpers.ScaledDummy(10f);
        var buttonWidth = MathF.Max(120f * scale, Buttons.PrimaryWidth(actionLabel));
        ImGui.SetCursorPosX(startX + MathF.Max(0f, (avail.X - buttonWidth) / 2f));
        return Buttons.Primary(actionLabel, buttonWidth);
    }

    private static void Centered(string text, Vector4 color, float startX, float width)
    {
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX(startX + MathF.Max(0f, (width - size.X) / 2f));
        ImGui.TextColored(color, text);
    }

    private static List<string> WrapLines(string text, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var current = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && ImGui.CalcTextSize(candidate).X > maxWidth)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            lines.Add(current);
        }
        return lines;
    }
}
