using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace MacroFolio.UI;

public static class MacroFolioTheme
{

    public enum GlassLevel
    {
        Regular,
        Clear,
        Opaque,
    }

    public const float MinOpacity = 0.30f;
    public const float MaxGlassOpacity = 1f;
    private const float MinBlurStrength = 1f;
    private const float MaxBlurStrength = 4.5f;
    private const float BlurFadeRange = 0.25f;
    private const float ClearBaseAlpha = 0.55f;
    private const float MinResolvedAlpha = 0.18f;

    private static Configuration? glassConfig;
    public static void AttachConfiguration(Configuration configuration) => glassConfig = configuration;
    public static BackdropBlur? Backdrop { get; set; }
    private static float GlassSetting => Math.Clamp(glassConfig?.UiGlass ?? 1f, 0f, 1f);

    public static float GlassOpacity
    {
        get
        {
            if (glassConfig is null) return 1f;
            return MinOpacity + (MaxGlassOpacity - MinOpacity) * GlassSetting;
        }
    }

    public static float BlurStrength => MinBlurStrength + (MaxBlurStrength - MinBlurStrength) * GlassSetting;
    public static float BlurOpacity => glassConfig is null ? 0f : Math.Clamp(GlassSetting / BlurFadeRange, 0f, 1f);

    public static float GlassAlpha(GlassLevel level)
    {
        if (level == GlassLevel.Opaque) return 1f;

        var baseAlpha = level == GlassLevel.Clear ? ClearBaseAlpha : 1f;
        return Math.Clamp(baseAlpha * GlassOpacity, MinResolvedAlpha, 1f);
    }

    private const float CardBaseAlpha = 0.45f;
    private const float CardCompensation = 0.45f;
    public static float CardAlpha(GlassLevel level = GlassLevel.Regular)
    {
        var window = GlassAlpha(level);
        return Math.Clamp(CardBaseAlpha + (1f - window) * CardCompensation, 0f, 1f);
    }

    public static Vector4 WithAlpha(Vector4 color, float alpha) => color with { W = alpha };

    public static readonly Vector4 AccentColor = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 AccentHoverColor = new(0x40 / 255f, 0x40 / 255f, 0x40 / 255f, 1f);
    public static readonly Vector4 ThemeWindowBg = new(0.11f, 0.11f, 0.11f, 1f);
    public static readonly Vector4 ThemeChildBg = new(0f, 0f, 0f, 0f);
    public static readonly Vector4 ThemeBorder = new(0x5A / 255f, 0x5A / 255f, 0x5A / 255f, 1f);
    public static readonly Vector4 ThemeSeparator = new(0x5A / 255f, 0x5A / 255f, 0x5A / 255f, 0.60f);
    public static readonly Vector4 ThemeTitleBar = new(0x1F / 255f, 0x1F / 255f, 0x1F / 255f, 1f);
    public static readonly Vector4 ThemeFrameBg = new(0.16f, 0.16f, 0.16f, 1f);
    public static readonly Vector4 ThemeFrameBgHovered = new(0x5A / 255f, 0x5A / 255f, 0x5A / 255f, 0.70f);
    public static readonly Vector4 ThemeFrameBgActive = new(0x70 / 255f, 0x70 / 255f, 0x70 / 255f, 1f);
    public static readonly Vector4 ThemeButtonBg = new(0x1F / 255f, 0x1F / 255f, 0x1F / 255f, 1f);
    public static readonly Vector4 ThemeButtonHovered = new(0x5A / 255f, 0x5A / 255f, 0x5A / 255f, 1f);
    public static readonly Vector4 ThemeButtonActive = new(0x70 / 255f, 0x70 / 255f, 0x70 / 255f, 1f);
    public static readonly Vector4 ThemeHeaderBg = new(0x1C / 255f, 0x1C / 255f, 0x1C / 255f, 1f);
    public static readonly Vector4 ThemeHeaderHovered = new(0x5A / 255f, 0x5A / 255f, 0x5A / 255f, 0.70f);
    public static readonly Vector4 ThemeHeaderActive = new(0x70 / 255f, 0x70 / 255f, 0x70 / 255f, 1f);
    public static readonly Vector4 ThemeScrollbarBg = new(0.08f, 0.08f, 0.08f, 0.50f);
    public static readonly Vector4 ThemeScrollbarGrab = new(0x5A / 255f, 0x5A / 255f, 0x5A / 255f, 1f);
    public static readonly Vector4 ThemeScrollbarHover = new(0x70 / 255f, 0x70 / 255f, 0x70 / 255f, 1f);
    public static readonly Vector4 ThemeScrollbarActive = new(0x8A / 255f, 0x8A / 255f, 0x8A / 255f, 1f);
    public static readonly Vector4 ThemeTabNormal = new(0x1C / 255f, 0x1C / 255f, 0x1C / 255f, 0.90f);
    public static readonly Vector4 ThemeTabHovered = new(0x5A / 255f, 0x5A / 255f, 0x5A / 255f, 1f);
    public static readonly Vector4 ThemeTabActive = new(0x70 / 255f, 0x70 / 255f, 0x70 / 255f, 1f);
    public static readonly Vector4 ThemeSliderGrab = new(0xD0 / 255f, 0xD0 / 255f, 0xD0 / 255f, 1f);
    public static readonly Vector4 ThemeSliderGrabActive = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 ThemeCheckMark = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 TextStrong = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 TextSecondary = new(0.72f, 0.72f, 0.72f, 1f);
    public static readonly Vector4 TextDim = new(0.58f, 0.58f, 0.58f, 1f);
    public static readonly Vector4 PrimaryBg = new(0xEC / 255f, 0xEC / 255f, 0xEC / 255f, 1f);
    public static readonly Vector4 PrimaryHovered = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 PrimaryActive = new(0xD2 / 255f, 0xD2 / 255f, 0xD2 / 255f, 1f);
    public static readonly Vector4 PrimaryText = new(0x14 / 255f, 0x14 / 255f, 0x14 / 255f, 1f);
    public static readonly Vector4 WarningColor = new(0.95f, 0.62f, 0.22f, 1f);
    public static readonly Vector4 SuccessColor = new(0.36f, 0.82f, 0.42f, 1f);
    public static readonly Vector4 DangerColor = new(0.92f, 0.36f, 0.36f, 1f);
    public static readonly Vector4 DangerButtonBg = new(0.62f, 0.17f, 0.17f, 1f);
    public static readonly Vector4 DangerButtonHovered = new(0.72f, 0.22f, 0.22f, 1f);
    public static float ListRightMargin => 12f * ImGuiHelpers.GlobalScale;

    private const int ThemeColorCount = 26;
    private const int ThemeStyleVarCount = 5;
    public const float RadiusWindow = 10f;
    public const float RadiusCard = 6f;
    public const float RadiusControl = 4f;
    public static void PushTheme(float glassAlpha = 1f, float? titleAlpha = null)
    {
        var titleGlass = titleAlpha ?? glassAlpha;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, RadiusWindow * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, RadiusCard * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, RadiusCard * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, RadiusControl * scale);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, WithAlpha(ThemeWindowBg, glassAlpha));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ThemeChildBg);
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeBorder);
        ImGui.PushStyleColor(ImGuiCol.Separator, ThemeSeparator);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, WithAlpha(ThemeTitleBar, titleGlass));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, WithAlpha(ThemeTitleBar, titleGlass));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, WithAlpha(ThemeTitleBar, titleGlass));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ThemeFrameBg);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ThemeFrameBgHovered);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ThemeFrameBgActive);
        ImGui.PushStyleColor(ImGuiCol.Button, ThemeButtonBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ThemeButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ThemeButtonActive);
        ImGui.PushStyleColor(ImGuiCol.Header, ThemeHeaderBg);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ThemeHeaderHovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, ThemeHeaderActive);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, ThemeScrollbarBg);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, ThemeScrollbarGrab);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, ThemeScrollbarHover);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, ThemeScrollbarActive);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, ThemeSliderGrab);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ThemeSliderGrabActive);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, ThemeCheckMark);
        ImGui.PushStyleColor(ImGuiCol.Tab, ThemeTabNormal);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, ThemeTabHovered);
        ImGui.PushStyleColor(ImGuiCol.TabActive, ThemeTabActive);
    }

    public static void PopTheme()
    {
        ImGui.PopStyleColor(ThemeColorCount);
        ImGui.PopStyleVar(ThemeStyleVarCount);
    }

    public static void DrawGlassSheen(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float alpha)
    {
        if (alpha <= 0f) return;

        var height = max.Y - min.Y;
        if (height <= 2f || max.X - min.X <= 2f) return;

        var mid = new Vector2(max.X, min.Y + height * 0.42f);
        var lit = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f * alpha));
        var off = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f));

        drawList.PushClipRect(min, max, true);
        drawList.AddRectFilledMultiColor(min, mid, lit, lit, off, off);
        drawList.PopClipRect();

        var inset = Math.Min(rounding, (max.X - min.X) / 2f);
        drawList.AddLine(
            new Vector2(min.X + inset, min.Y + 0.5f),
            new Vector2(max.X - inset, min.Y + 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f * alpha)),
            1f);
    }
}
