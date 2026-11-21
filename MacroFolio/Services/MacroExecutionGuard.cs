using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using MacroFolio.Localization;

namespace MacroFolio.Services;

// Applies the anti-automation rule to macros the player launches from the hotbar, but only
// to macros MacroFolio wrote: inside a restricted instance, a MacroFolio macro that uses a
// combat action or chains to the next macro does not run. Native macros are never touched,
// and any doubt or error lets the macro run as usual.
public sealed unsafe class MacroExecutionGuard : IDisposable
{
    private delegate void ExecuteMacroDelegate(RaptureShellModule* thisPtr, RaptureMacroModule.Macro* macro);

    private const double MessageCooldownSeconds = 2.0;

    private readonly Hook<ExecuteMacroDelegate>? hook;
    private readonly MacroService macroService;
    private readonly MacroLibrary library;
    private readonly IChatGui chat;
    private DateTime lastMessage = DateTime.MinValue;

    public MacroExecutionGuard(IGameInteropProvider interop, MacroService macroService, MacroLibrary library, IChatGui chat)
    {
        this.macroService = macroService;
        this.library = library;
        this.chat = chat;

        try
        {
            hook = interop.HookFromAddress<ExecuteMacroDelegate>(
                RaptureShellModule.MemberFunctionPointers.ExecuteMacro,
                ExecuteMacroDetour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to hook RaptureShellModule.ExecuteMacro; hotbar macro guard disabled.");
            hook = null;
        }
    }

    private void ExecuteMacroDetour(RaptureShellModule* thisPtr, RaptureMacroModule.Macro* macro)
    {
        bool block;
        try
        {
            block = ShouldBlock(macro);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Macro guard check failed; letting the macro run.");
            block = false;
        }

        if (block)
        {
            NotifyBlocked();
            return;
        }

        hook!.Original(thisPtr, macro);
    }

    private bool ShouldBlock(RaptureMacroModule.Macro* macro)
    {
        if (!AutomationGuard.IsInRestrictedInstance()) return false;
        if (!macroService.TryLocate(macro, out var set, out var slot)) return false;

        var game = macroService.ReadSlot(set, slot);
        if (!game.Lines.Any(AutomationGuard.IsRestrictedMacroLine)) return false;

        return library.IsMacroFolioMacro(set, slot, game);
    }

    // Holding a hotbar key can fire the macro repeatedly: one message is enough.
    private void NotifyBlocked()
    {
        var now = DateTime.UtcNow;
        if ((now - lastMessage).TotalSeconds < MessageCooldownSeconds) return;
        lastMessage = now;
        chat.PrintError(Loc.Get("Guard.MacroBlocked"), Constants.PluginName);
    }

    public void Dispose()
    {
        hook?.Disable();
        hook?.Dispose();
    }
}
