using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace MacroFolio.Services;

public sealed unsafe class EmoteRecordingService : IDisposable
{
    public readonly record struct CapturedEmote(string Command, float GapSeconds);

    private delegate bool ExecuteEmoteDelegate(
        EmoteManager* thisPtr, ushort emoteId, EmoteController.PlayEmoteOption* option);

    private readonly Hook<ExecuteEmoteDelegate>? hook;
    private DateTime? lastCapture;

    private readonly List<(ushort Id, float Wait)> playQueue = new();
    private int playIndex;
    private float playTimer;
    private static Dictionary<string, ushort>? commandToEmoteId;

    public bool IsRecording { get; private set; }
    public int CapturedCount { get; private set; }
    public bool IsPlaying { get; private set; }
    public int PlayTotal { get; private set; }
    public int PlayDone => playIndex;
    public event Action<CapturedEmote>? OnCaptured;

    public bool IsAvailable => hook is not null;

    public EmoteRecordingService(IGameInteropProvider interop)
    {
        try
        {
            hook = interop.HookFromAddress<ExecuteEmoteDelegate>(
                EmoteManager.MemberFunctionPointers.ExecuteEmote,
                ExecuteEmoteDetour);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to hook EmoteManager.ExecuteEmote; recording disabled.");
            hook = null;
        }
    }

    public void Start()
    {
        if (hook is null) return;
        IsRecording = true;
        CapturedCount = 0;
        lastCapture = null;
        hook.Enable();
    }

    public void Stop()
    {
        IsRecording = false;
        hook?.Disable();
    }

    private bool ExecuteEmoteDetour(EmoteManager* thisPtr, ushort emoteId, EmoteController.PlayEmoteOption* option)
    {
        var result = hook!.Original(thisPtr, emoteId, option);

        if (IsRecording && result)
        {
            try
            {
                var command = ResolveCommand(emoteId);
                if (!string.IsNullOrEmpty(command))
                {
                    var now = DateTime.UtcNow;
                    var gapRaw = lastCapture.HasValue
                        ? (now - lastCapture.Value).TotalSeconds
                        : 0d;
                    var gap = (float)Math.Round(gapRaw, MidpointRounding.AwayFromZero);
                    lastCapture = now;
                    CapturedCount++;
                    OnCaptured?.Invoke(new CapturedEmote(command, gap));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Emote capture failed for id {Id}", emoteId);
            }
        }

        return result;
    }
    
    public void Play(IEnumerable<string> lines)
    {
        BuildReverseLookup();
        playQueue.Clear();
        foreach (var raw in lines)
        {
            var (cmd, wait) = StripWait(raw);
            if (cmd.Length == 0) continue;
            if (commandToEmoteId!.TryGetValue(cmd, out var id))
                playQueue.Add((id, wait));
        }

        if (playQueue.Count == 0) return;
        playIndex = 0;
        playTimer = 0f;
        PlayTotal = playQueue.Count;
        IsPlaying = true;
    }

    public void StopPlayback()
    {
        IsPlaying = false;
        playQueue.Clear();
        playIndex = 0;
        PlayTotal = 0;
    }

    public void Tick(float deltaSeconds)
    {
        if (!IsPlaying) return;

        playTimer -= deltaSeconds;
        if (playTimer > 0f) return;

        if (playIndex >= playQueue.Count)
        {
            StopPlayback();
            return;
        }

        var (id, wait) = playQueue[playIndex];
        try
        {
            var mgr = EmoteManager.Instance();
            if (mgr is not null)
                mgr->ExecuteEmote(id, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Emote playback failed for id {Id}", id);
        }

        playTimer = Math.Max(0.1f, wait);
        playIndex++;
    }

    private static (string Command, float Wait) StripWait(string line)
    {
        var s = line.Trim();
        if (s.Length == 0) return ("", 0f);

        var open = s.LastIndexOf("<wait", StringComparison.OrdinalIgnoreCase);
        if (open >= 0 && s.EndsWith('>'))
        {
            var inner = s[(open + 5)..].TrimEnd('>').TrimStart('.', ':');
            if (float.TryParse(inner,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var w))
            {
                return (s[..open].Trim(), w);
            }
        }
        return (s, 0f);
    }

    private static void BuildReverseLookup()
    {
        if (commandToEmoteId is not null) return;
        commandToEmoteId = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>();

        foreach (var e in sheet)
        {
            if (e.RowId == 0) continue;
            var tc = e.TextCommand.ValueNullable;
            if (tc is null) continue;
            foreach (var raw in new[] { tc.Value.Command, tc.Value.ShortCommand, tc.Value.Alias, tc.Value.ShortAlias })
            {
                var cmd = raw.ExtractText();
                if (!string.IsNullOrWhiteSpace(cmd) && cmd.StartsWith('/'))
                    commandToEmoteId[cmd] = (ushort)e.RowId;
            }
        }
    }

    private static string ResolveCommand(uint emoteId)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>();

        var row = sheet.GetRowOrDefault(emoteId);
        if (row is null) return "";

        var textCmd = row.Value.TextCommand.ValueNullable;
        if (textCmd is null) return "";

        return textCmd.Value.Command.ExtractText();
    }

    public void Dispose()
    {
        hook?.Dispose();
    }
}
