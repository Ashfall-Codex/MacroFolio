using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using MacroFolio.Models;

namespace MacroFolio.Services;

public enum MacroSet
{
    Individual,
    Shared,
}

public sealed unsafe class MacroService
{
    public const int SlotsPerSet = 100;
    public const int MaxLinesPerMacro = 15;

    public IReadOnlyList<MacroEntry> Read(MacroSet set)
    {
        var module = RaptureMacroModule.Instance();
        if (module is null)
            return Array.Empty<MacroEntry>();

        var result = new MacroEntry[SlotsPerSet];
        if (set == MacroSet.Individual)
        {
            for (var i = 0; i < SlotsPerSet; i++)
                result[i] = ReadMacro(ref module->Individual[i]);
        }
        else
        {
            for (var i = 0; i < SlotsPerSet; i++)
                result[i] = ReadMacro(ref module->Shared[i]);
        }

        return result;
    }

    public MacroEntry ReadSlot(MacroSet set, int slot)
    {
        var module = RaptureMacroModule.Instance();
        if (module is null)
            return new MacroEntry();

        if (set == MacroSet.Individual)
            return ReadMacro(ref module->Individual[slot]);
        return ReadMacro(ref module->Shared[slot]);
    }

    public void Clear(MacroSet set, int slot)
    {
        var module = RaptureMacroModule.Instance();
        if (module is null) return;

        if (set == MacroSet.Individual)
            module->Individual[slot].Clear();
        else
            module->Shared[slot].Clear();

        module->SetSavePendingFlag(true, SetIndex(set));
        ReloadSlot(set, slot);
    }

    public void Write(MacroSet set, int slot, MacroEntry entry)
    {
        var module = RaptureMacroModule.Instance();
        if (module is null) return;

        if (set == MacroSet.Individual)
            WriteMacro(ref module->Individual[slot], entry);
        else
            WriteMacro(ref module->Shared[slot], entry);

        module->SetSavePendingFlag(true, SetIndex(set));
        ReloadSlot(set, slot);
    }
    
    private static void ReloadSlot(MacroSet set, int slot)
    {
        var hotbar = RaptureHotbarModule.Instance();
        if (hotbar is null) return;
        hotbar->ReloadMacroSlots((byte)SetIndex(set), (byte)slot);
    }

    public void ExecuteSlot(MacroSet set, int slot)
    {
        var module = RaptureMacroModule.Instance();
        if (module is null) return;

        var shell = RaptureShellModule.Instance();
        if (shell is null) return;

        if (set == MacroSet.Individual)
        {
            fixed (RaptureMacroModule.Macro* macro = &module->Individual[slot])
                shell->ExecuteMacro(macro);
        }
        else
        {
            fixed (RaptureMacroModule.Macro* macro = &module->Shared[slot])
                shell->ExecuteMacro(macro);
        }
    }

    private static void WriteMacro(ref RaptureMacroModule.Macro macro, MacroEntry entry)
    {
        macro.Clear();
        macro.Name.SetString(entry.Name);
        macro.SetIcon(entry.IconId);

        var count = Math.Min(entry.Lines.Count, MaxLinesPerMacro);
        for (var i = 0; i < count; i++)
            macro.Lines[i].SetString(entry.Lines[i]);
    }

    private static MacroEntry ReadMacro(ref RaptureMacroModule.Macro macro)
    {
        var entry = new MacroEntry
        {
            Name = macro.Name.ToString(),
            IconId = macro.IconId,
            Lines = new List<string>(MaxLinesPerMacro),
        };

        for (var i = 0; i < MaxLinesPerMacro; i++)
            entry.Lines.Add(macro.Lines[i].ToString());

        while (entry.Lines.Count > 0 && string.IsNullOrEmpty(entry.Lines[^1]))
            entry.Lines.RemoveAt(entry.Lines.Count - 1);

        return entry;
    }

    private static uint SetIndex(MacroSet set) => set == MacroSet.Individual ? 0u : 1u;
}
