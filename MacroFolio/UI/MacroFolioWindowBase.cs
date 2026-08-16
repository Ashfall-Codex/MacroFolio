using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MacroFolio.UI;

public abstract class MacroFolioWindowBase(
    string name,
    ImGuiWindowFlags flags = ImGuiWindowFlags.None,
    bool forceMainWindow = false)
    : Window(name, flags, forceMainWindow)
{
    public override void PreDraw() => MacroFolioTheme.PushTheme();

    public override void PostDraw() => MacroFolioTheme.PopTheme();

    public sealed override void Draw() => DrawContents();

    protected abstract void DrawContents();
}
