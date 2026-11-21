using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using MacroFolio.Localization;
using MacroFolio.UI.Components;

namespace MacroFolio.UI;

public sealed class TestBuildWarningWindow : MacroFolioWindowBase, IDisposable
{
    private readonly Configuration config;
    private readonly string versionLabel;

    public TestBuildWarningWindow(Configuration config)
        : base(
            Loc.Get("TestBuildWarning.WindowTitle"),
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        versionLabel = version.ToString();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 0),
            MaximumSize = new Vector2(440, 9999),
        };
        ShowCloseButton = false;
        RespectCloseHotkey = false;

        var isTestBuild = Plugin.PluginInterface.IsDev || Plugin.PluginInterface.IsTesting;
        if (isTestBuild
            && !string.Equals(config.LastTestBuildWarningVersionSeen, versionLabel, StringComparison.Ordinal))
        {
            IsOpen = true;
        }
    }

    public void Dispose() { }

    public override void OnOpen()
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
    }

    protected override void DrawContents()
    {
        var avail = ImGui.GetContentRegionAvail().X;

        var header = Loc.Get("TestBuildWarning.Header");
        var headerW = ImGui.CalcTextSize(header).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, (avail - headerW) / 2f));
        ImGui.TextColored(MacroFolioTheme.WarningColor, header);

        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(4f);

        ImGui.TextWrapped(Loc.Get("TestBuildWarning.Body"));

        ImGuiHelpers.ScaledDummy(8f);

        var signature = Loc.Get("TestBuildWarning.Signature");
        var heart = FontAwesomeIcon.Heart.ToIconString();
        var sigW = ImGui.CalcTextSize(signature).X;
        float heartW;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            heartW = ImGui.CalcTextSize(heart).X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, (avail - (sigW + spacing + heartW)) / 2f));
        ImGui.TextColored(MacroFolioTheme.TextSecondary, signature);
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(MacroFolioTheme.TextSecondary, heart);

        ImGuiHelpers.ScaledDummy(8f);
        ImGui.Separator();

        var buttonLabel = Loc.Get("TestBuildWarning.Acknowledge");
        var buttonW = 180f * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, (avail - buttonW) / 2f));
        if (Buttons.Primary(buttonLabel, buttonW))
        {
            config.LastTestBuildWarningVersionSeen = versionLabel;
            config.Save();
            IsOpen = false;
        }
    }
}
