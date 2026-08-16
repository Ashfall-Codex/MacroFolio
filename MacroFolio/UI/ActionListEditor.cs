using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using MacroFolio.Localization;

namespace MacroFolio.UI;

public sealed class ActionListEditor
{
    private const int MaxLineLength = 180;
    // Generous input buffer so pasted/long lines are NEVER silently truncated in
    // the editor. The real per-line game limit only matters at publish time.
    private const int EditBufferSize = 2000;
    private const string ActionDragType = "MF_ACTION_ROW";
    private static readonly byte[] DragPayloadDummy = new byte[1];

    public int MaxLines { get; set; } = 15;
    private bool Unlimited => MaxLines == int.MaxValue;

    public float StartOffset => startOffsetSeconds;

    private string draftActions = "";
    private List<SimplifiedAction> draftActionList = new();
    private ActionEditorMode actionMode = ActionEditorMode.Classique;
    private bool requestOpenAddMenu;
    private string emoteSearch = "";
    private string actionSearch = "";

    private float startOffsetSeconds;
    private string startOffsetText = "0:00";
    private int draggedActionIndex = -1;
    private (int From, int To)? pendingActionMove;
    private int pendingInsertIndex = -1; // -1 = append (before any trailing /nextmacro)
    private int editingNoteRow = -1;
    // Notes are anchored to LINE CONTENT, not to position, so adding/removing a
    // line in Classique mode no longer shifts the notes of the other lines.
    // stashedLines[i] is the macro line that stashedNotes[i] belongs to.
    private List<string>? stashedNotes;
    private List<string>? stashedLines;

    private static List<EmoteOption>? unlockedEmotes;
    private static HashSet<string>? unlockedEmoteCommands; // every command variant, for recognition
    private static List<JobActionOption>? cachedJobActions;
    private static uint cachedJobActionsForId;

    private enum ActionEditorMode { Classique, Simplifié }

    private enum ActionKind
    {
        Emote,
        EmotePredefini,
        SkillAction,
        SayParler,
        SayCrier,
        SayHurler,
        SayGroupe,
        Countdown,
        Cibler,
        CiblerSoi,
        CiblerPj,
        Echo,
        EchoSound,
        Timer,
        NextMacro,
        Brut,
    }

    private sealed record EmoteOption(string Command, string DisplayName);
    private sealed record JobActionOption(string Name);

    private sealed class SimplifiedAction
    {
        public ActionKind Kind { get; set; }
        public string Text { get; set; } = "";
        public float WaitSeconds { get; set; } = 1f;
        public float? WaitAfter { get; set; }
        public bool SilentAnimation { get; set; }
        public int SoundId { get; set; } = 1;
        public string Note { get; set; } = "";
    }
    

    public void Reset()
    {
        draftActions = "";
        draftActionList = new List<SimplifiedAction>();
        actionMode = ActionEditorMode.Classique;
        startOffsetSeconds = 0f;
        startOffsetText = "0:00";
        editingNoteRow = -1;
        emoteSearch = "";
        actionSearch = "";
        stashedNotes = null;
        stashedLines = null;
    }

    public void LoadFromLines(IEnumerable<string> lines, float startOffset = 0f, IReadOnlyList<string>? notes = null)
    {
        Reset();
        var lineList = lines.ToList();
        draftActions = string.Join("\n", lineList);
        startOffsetSeconds = Math.Max(0f, startOffset);
        startOffsetText = FormatTimestamp(startOffsetSeconds);
        if (notes is not null)
        {
            // Anchor each incoming note to its line's content.
            stashedNotes = new List<string>(notes);
            stashedLines = lineList.Take(notes.Count).ToList();
            while (stashedLines.Count < stashedNotes.Count) stashedLines.Add("");
        }
    }

    public List<string> ExportLines()
    {
        var lines = actionMode == ActionEditorMode.Classique
            ? draftActions.Replace("\r\n", "\n").Split('\n').ToList()
            : draftActionList.Select(SerializeAction).ToList();

        while (lines.Count > 0 && string.IsNullOrEmpty(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        return Unlimited ? lines : lines.Take(MaxLines).ToList();
    }
    
    public List<string> ExportNotes()
    {
        var lines = ExportLines();
        List<string> notes;
        if (actionMode == ActionEditorMode.Simplifié)
            notes = draftActionList.Take(lines.Count).Select(a => a.Note).ToList();
        else
            // Re-anchor stashed notes to the current lines by content, so edits in
            // Classique mode keep each note on its line.
            notes = MatchNotesToLines(lines);

        if (notes.Count > lines.Count) notes = notes.Take(lines.Count).ToList();
        while (notes.Count < lines.Count) notes.Add("");
        return notes;
    }

    /// <summary>
    /// Build a note-per-line list for <paramref name="lines"/> by matching each
    /// line to its stashed (line content → note) pair. Greedy first-unused match,
    /// so duplicate lines keep distinct notes in order.
    /// </summary>
    private List<string> MatchNotesToLines(List<string> lines)
    {
        var result = new List<string>(new string[lines.Count]);
        for (var i = 0; i < lines.Count; i++) result[i] = "";
        if (stashedNotes is null || stashedLines is null) return result;

        var used = new bool[stashedLines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            for (var j = 0; j < stashedLines.Count; j++)
            {
                if (used[j] || stashedLines[j] != lines[i]) continue;
                if (string.IsNullOrEmpty(stashedNotes[j])) { used[j] = true; continue; }
                result[i] = stashedNotes[j];
                used[j] = true;
                break;
            }
        }
        return result;
    }
    
    public void AppendCapturedEmote(string command, float gapSeconds)
    {
        var lines = ExportLines();
        if (lines.Count > 0 && gapSeconds >= 0.1f)
            lines[^1] = $"{lines[^1].TrimEnd()} <wait.{FormatWait(gapSeconds)}>";
        lines.Add(command);

        if (actionMode == ActionEditorMode.Classique)
            draftActions = string.Join("\n", lines);
        else
            draftActionList = lines.Select(ParseAction).ToList();
    }

    public int LineCount => ExportLines().Count;

    public void Draw(float availWidth)
    {
        DrawModeSelector(availWidth);
        ImGuiHelpers.ScaledDummy(8f);

        if (actionMode == ActionEditorMode.Classique)
            DrawClassiqueEditor(availWidth);
        else
            DrawSimplifiedEditor(availWidth);
    }

    public string CurrentModeLabel =>
        actionMode == ActionEditorMode.Classique
            ? Loc.Get("Editor.Mode.Classic")
            : Loc.Get("Editor.Mode.Simplified");

    public string CurrentModeSubtitle =>
        actionMode == ActionEditorMode.Classique
            ? Loc.Get("Editor.Mode.ClassicSubtitle")
            : Loc.Get("Editor.Mode.SimplifiedSubtitle");


    private void DrawModeSelector(float availWidth)
    {
        var spacing = 6f * ImGuiHelpers.GlobalScale;
        var btnW = (220f * ImGuiHelpers.GlobalScale - spacing) / 2f;
        var btnH = 24f * ImGuiHelpers.GlobalScale;
        var totalW = btnW * 2f + spacing;

        ImGui.SetCursorPosX((availWidth - totalW) / 2f);
        DrawModeButton(Loc.Get("Editor.Mode.Classic"), ActionEditorMode.Classique, new Vector2(btnW, btnH));
        ImGui.SameLine(0f, spacing);
        DrawModeButton(Loc.Get("Editor.Mode.Simplified"), ActionEditorMode.Simplifié, new Vector2(btnW, btnH));
    }

    private void DrawModeButton(string label, ActionEditorMode mode, Vector2 size)
    {
        var isActive = actionMode == mode;
        var bg = isActive ? MacroFolioTheme.AccentColor : MacroFolioTheme.ThemeButtonBg;
        var textColor = isActive ? MacroFolioTheme.ThemeWindowBg : MacroFolioTheme.AccentColor;

        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MacroFolioTheme.ThemeButtonActive);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * ImGuiHelpers.GlobalScale);

        if (ImGui.Button(label + "##mode_" + mode, size))
            SwitchMode(mode);

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();
    }

    private void SwitchMode(ActionEditorMode target)
    {
        if (target == actionMode) return;

        if (target == ActionEditorMode.Simplifié)
        {
            var lines = draftActions
                .Replace("\r\n", "\n")
                .Split('\n')
                .ToList();
            while (lines.Count > 0 && string.IsNullOrEmpty(lines[^1]))
                lines.RemoveAt(lines.Count - 1);
            draftActionList = lines.Select(ParseAction).ToList();

            // Re-attach notes by line content (robust to lines added/removed in Classique).
            var matched = MatchNotesToLines(lines);
            for (var i = 0; i < draftActionList.Count && i < matched.Count; i++)
                draftActionList[i].Note = matched[i];
        }
        else
        {
            draftActions = string.Join("\n", draftActionList.Select(SerializeAction));
            // Stash notes anchored to their serialized line content.
            stashedLines = draftActionList.Select(SerializeAction).ToList();
            stashedNotes = draftActionList.Select(a => a.Note).ToList();
        }

        actionMode = target;
    }


    private void DrawClassiqueEditor(float availWidth)
    {
        var lineCount = CountLines(draftActions);
        var overLimit = !Unlimited && lineCount > MaxLines;

        var inputW = availWidth - 40f * ImGuiHelpers.GlobalScale;
        // Fill the remaining height: reserve room for the spacing + line counter
        // below so the textarea grows with the window instead of leaving dead space.
        var reserved = ImGui.GetTextLineHeightWithSpacing() + 12f * ImGuiHelpers.GlobalScale;
        var inputH = Math.Max(120f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().Y - reserved);
        ImGui.SetCursorPosX(20f * ImGuiHelpers.GlobalScale);
        var body = draftActions;
        ImGui.InputTextMultiline(
            "##actions",
            ref body,
            Math.Max(65536, body.Length + 4096),
            new Vector2(inputW, inputH));
        draftActions = body;

        ImGuiHelpers.ScaledDummy(6f);
        DrawLineCounter(lineCount, overLimit, availWidth);
    }


    private static unsafe bool AcceptActionDrop()
    {
        var payload = ImGui.AcceptDragDropPayload(ActionDragType);
        return payload.Handle is not null;
    }

    private void DrawSimplifiedEditor(float availWidth)
    {
        var rowSpacing = 4f * ImGuiHelpers.GlobalScale;
        var delBtnW = 28f * ImGuiHelpers.GlobalScale;
        var prefixW = 60f * ImGuiHelpers.GlobalScale;
        var stampW = 52f * ImGuiHelpers.GlobalScale;
        var gripW = 20f * ImGuiHelpers.GlobalScale;
        var marginX = 20f * ImGuiHelpers.GlobalScale;
        var rowAvail = availWidth - marginX * 2f;
        var waitAreaW = 140f * ImGuiHelpers.GlobalScale;
        var inputW = rowAvail - gripW - stampW - prefixW - waitAreaW - delBtnW * 3f - rowSpacing * 7f;

        ImGui.SetCursorPosX(marginX);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(Loc.Get("Editor.LaunchAt"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("##start_offset", ref startOffsetText, 8))
        {
            if (TryParseTimestamp(startOffsetText, out var parsed))
                startOffsetSeconds = Math.Max(0f, parsed);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
            startOffsetText = FormatTimestamp(startOffsetSeconds);
        ImGuiHelpers.ScaledDummy(4f);

        var fireTimes = new float[draftActionList.Count];
        var acc = startOffsetSeconds;
        for (var k = 0; k < draftActionList.Count; k++)
        {
            fireTimes[k] = acc;
            acc += StepSeconds(draftActionList[k]);
        }

        // Fill the available height so all rows are visible when the window is
        // enlarged. Reserve room for the "+ Ajouter" button and the line counter
        // below the list.
        var scale = ImGuiHelpers.GlobalScale;
        var reserved = ImGui.GetFrameHeight() + ImGui.GetTextLineHeightWithSpacing() + 26f * scale;
        var listHeight = Math.Max(160f * scale, ImGui.GetContentRegionAvail().Y - reserved);
        ImGui.SetCursorPosX(marginX);
        if (ImGui.BeginChild("##simplified_list", new Vector2(rowAvail, listHeight), true))
        {
            var removeIndex = -1;
            for (var i = 0; i < draftActionList.Count; i++)
            {
                ImGui.PushID(i);
                var action = draftActionList[i];

                ImGui.AlignTextToFramePadding();
                using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    ImGui.Selectable(FontAwesomeIcon.GripVertical.ToIconString() + "##grip", false, ImGuiSelectableFlags.None, new Vector2(gripW, 0));
                if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
                {
                    draggedActionIndex = i;
                    ImGui.SetDragDropPayload(ActionDragType, DragPayloadDummy);
                    ImGui.TextUnformatted(action.Kind == ActionKind.Brut
                        ? Loc.Get("Editor.MoveLine")
                        : string.Format(Loc.Get("Editor.MovePrefix"), KindPrefix(action.Kind)));
                    ImGui.EndDragDropSource();
                }
                if (ImGui.BeginDragDropTarget())
                {
                    if (AcceptActionDrop() && draggedActionIndex >= 0 && draggedActionIndex != i)
                        pendingActionMove = (draggedActionIndex, i);
                    ImGui.EndDragDropTarget();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(Loc.Get("Editor.DragToReorder"));
                    ImGui.EndTooltip();
                }
                ImGui.SameLine(gripW, rowSpacing);

                // Indicative cumulative timestamp (lancement à + waits). Read-only.
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(MacroFolioTheme.AccentColor with { W = 0.85f }, FormatTimestamp(fireTimes[i]));
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(Loc.Get("Editor.TimestampTooltip"));
                    ImGui.EndTooltip();
                }
                ImGui.SameLine(gripW + stampW + rowSpacing, 0f);

                ImGui.AlignTextToFramePadding();
                var prefix = action.Kind == ActionKind.Brut ? Loc.Get("Action.Label.Brut") : KindPrefix(action.Kind);
                ImGui.TextColored(MacroFolioTheme.AccentColor with { W = 0.7f }, prefix);
                ImGui.SameLine(gripW + stampW + rowSpacing + prefixW, rowSpacing);

                if (action.Kind == ActionKind.Timer)
                {
                    var seconds = action.WaitSeconds;
                    ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
                    if (ImGui.InputFloat("##wait", ref seconds, 0f, 0f, "%.1f"))
                        action.WaitSeconds = Math.Max(0.1f, seconds);
                    ImGui.SameLine(0f, rowSpacing);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled(Loc.Get("Editor.Seconds"));
                }
                else if (action.Kind == ActionKind.EmotePredefini)
                {
                    var emotes = EnsureUnlockedEmotes();
                    var current = emotes.FirstOrDefault(e => e.Command == action.Text);
                    var preview = current is null
                        ? (string.IsNullOrEmpty(action.Text) ? Loc.Get("Editor.ChooseEmote") : action.Text)
                        : $"{current.DisplayName} ({current.Command})";

                    var animBoxW = 28f * ImGuiHelpers.GlobalScale;
                    ImGui.SetNextItemWidth(inputW - animBoxW - rowSpacing);
                    if (ImGui.BeginCombo("##emote_combo", preview))
                    {
                        if (ImGui.IsWindowAppearing())
                        {
                            emoteSearch = "";
                            ImGui.SetKeyboardFocusHere();
                        }

                        ImGui.SetNextItemWidth(-1);
                        ImGui.InputTextWithHint("##emote_search", Loc.Get("Editor.SearchHint"), ref emoteSearch, 64);
                        ImGui.Separator();

                        if (emotes.Count == 0)
                        {
                            ImGui.TextDisabled(Loc.Get("Editor.NoEmoteUnlocked"));
                        }
                        else
                        {
                            var query = emoteSearch.Trim();
                            IEnumerable<EmoteOption> filtered = emotes;
                            if (query.Length > 0)
                                filtered = emotes.Where(e =>
                                    e.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                    e.Command.Contains(query, StringComparison.OrdinalIgnoreCase));

                            var any = false;
                            foreach (var opt in filtered)
                            {
                                any = true;
                                var isSelected = opt.Command == action.Text;
                                if (ImGui.Selectable($"{opt.DisplayName}  ({opt.Command})", isSelected))
                                    action.Text = opt.Command;
                                if (isSelected) ImGui.SetItemDefaultFocus();
                            }

                            if (!any)
                                ImGui.TextDisabled(Loc.Get("Editor.NoResult"));
                        }

                        ImGui.EndCombo();
                    }

                    ImGui.SameLine(0f, rowSpacing);
                    var visibleInChat = !action.SilentAnimation;
                    var eyeIcon = visibleInChat ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash;
                    var eyeColor = visibleInChat
                        ? new Vector4(0.30f, 0.85f, 0.30f, 1f)
                        : new Vector4(0.90f, 0.30f, 0.30f, 1f);
                    var eyeH = ImGui.GetFrameHeight();
                    ImGui.PushStyleColor(ImGuiCol.Text, eyeColor);
                    using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    {
                        if (ImGui.Button(eyeIcon.ToIconString() + "##chat_vis", new Vector2(eyeH, eyeH)))
                            action.SilentAnimation = !action.SilentAnimation;
                    }
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(visibleInChat
                            ? Loc.Get("Action.Tooltip.ChatVisible")
                            : Loc.Get("Action.Tooltip.ChatHidden"));
                        ImGui.EndTooltip();
                    }
                }
                else if (action.Kind == ActionKind.NextMacro)
                {
                    var prefixEndX = gripW + stampW + rowSpacing
                        + ImGui.CalcTextSize(KindPrefix(action.Kind)).X + rowSpacing * 2f;
                    if (ImGui.GetCursorPosX() < prefixEndX)
                        ImGui.SetCursorPosX(prefixEndX);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled(Loc.Get("Action.Hint.NextMacro"));
                }
                else if (action.Kind == ActionKind.Countdown)
                {
                    if (!int.TryParse(action.Text, out var cd) || cd < 1) cd = 5;
                    ImGui.SetNextItemWidth(70f * ImGuiHelpers.GlobalScale);
                    if (ImGui.InputInt("##cd_secs", ref cd))
                        action.Text = Math.Max(1, cd).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ImGui.SameLine(0f, rowSpacing);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled(Loc.Get("Editor.Seconds"));
                }
                else if (action.Kind == ActionKind.CiblerPj)
                {
                    var text = action.Text;
                    ImGui.SetNextItemWidth(inputW);
                    if (ImGui.InputTextWithHint("##target_pj", Loc.Get("Action.Hint.CiblerPj"), ref text, EditBufferSize))
                        action.Text = text;
                }
                else if (action.Kind == ActionKind.Cibler)
                {
                    var text = action.Text;
                    ImGui.SetNextItemWidth(inputW);
                    if (ImGui.InputTextWithHint("##target_gen", Loc.Get("Action.Hint.Cibler"), ref text, EditBufferSize))
                        action.Text = text;
                }
                else if (action.Kind == ActionKind.CiblerSoi)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled(Loc.Get("Action.Hint.CiblerSoi"));
                }
                else if (action.Kind == ActionKind.Echo)
                {
                    var text = action.Text;
                    ImGui.SetNextItemWidth(inputW);
                    if (ImGui.InputTextWithHint("##echo_plain", Loc.Get("Action.Hint.Echo"), ref text, EditBufferSize))
                        action.Text = text;
                }
                else if (action.Kind == ActionKind.EchoSound)
                {
                    var seBlockW = 72f * ImGuiHelpers.GlobalScale;
                    var text = action.Text;
                    ImGui.SetNextItemWidth(inputW - seBlockW - rowSpacing);
                    if (ImGui.InputTextWithHint("##echo_msg", Loc.Get("Action.Hint.Message"), ref text, EditBufferSize))
                        action.Text = text;

                    ImGui.SameLine(0f, rowSpacing);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled("<se.");
                    ImGui.SameLine(0f, 2f);
                    var sid = action.SoundId;
                    ImGui.SetNextItemWidth(36f * ImGuiHelpers.GlobalScale);
                    if (ImGui.InputInt("##se_id", ref sid))
                        action.SoundId = Math.Clamp(sid, 1, 16);
                    ImGui.SameLine(0f, 2f);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled(">");
                }
                else if (action.Kind == ActionKind.SkillAction)
                {
                    var actions = EnsureJobActions();
                    var preview = string.IsNullOrEmpty(action.Text)
                        ? Loc.Get("Editor.ChooseAction")
                        : action.Text;

                    ImGui.SetNextItemWidth(inputW);
                    if (ImGui.BeginCombo("##action_combo", preview))
                    {
                        if (ImGui.IsWindowAppearing())
                        {
                            actionSearch = "";
                            ImGui.SetKeyboardFocusHere();
                        }

                        ImGui.SetNextItemWidth(-1);
                        ImGui.InputTextWithHint("##action_search", Loc.Get("Editor.SearchHint"), ref actionSearch, 64);
                        ImGui.Separator();

                        if (actions.Count == 0)
                        {
                            ImGui.TextDisabled(Loc.Get("Editor.NoActionAvailable"));
                        }
                        else
                        {
                            var query = actionSearch.Trim();
                            IEnumerable<JobActionOption> filtered = actions;
                            if (query.Length > 0)
                                filtered = actions.Where(a =>
                                    a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));

                            var any = false;
                            foreach (var opt in filtered)
                            {
                                any = true;
                                var isSelected = opt.Name == action.Text;
                                if (ImGui.Selectable(opt.Name, isSelected))
                                    action.Text = opt.Name;
                                if (isSelected) ImGui.SetItemDefaultFocus();
                            }
                            if (!any)
                                ImGui.TextDisabled(Loc.Get("Editor.NoResult"));
                        }
                        ImGui.EndCombo();
                    }
                }
                else
                {
                    var text = action.Text;
                    var hint = action.Kind == ActionKind.Brut
                        ? Loc.Get("Action.Hint.Brut")
                        : Loc.Get("Action.Hint.Message");
                    ImGui.SetNextItemWidth(inputW);
                    if (ImGui.InputTextWithHint("##text", hint, ref text, EditBufferSize))
                        action.Text = text;
                }

                if (action.Kind != ActionKind.Timer && action.Kind != ActionKind.NextMacro)
                {
                    ImGui.SameLine(rowAvail - waitAreaW - delBtnW * 3f - rowSpacing * 3f);
                    if (action.WaitAfter.HasValue)
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextDisabled("<wait.");
                        ImGui.SameLine(0f, 2f);
                        var waitVal = action.WaitAfter.Value;
                        ImGui.SetNextItemWidth(54f * ImGuiHelpers.GlobalScale);
                        if (ImGui.InputFloat("##wait_inline", ref waitVal, 0f, 0f, "%.1f"))
                            action.WaitAfter = Math.Max(0.1f, waitVal);
                        ImGui.SameLine(0f, 2f);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextDisabled(">");
                        ImGui.SameLine(0f, 4f);
                        if (ImGui.SmallButton("x##nowait"))
                            action.WaitAfter = null;
                    }
                    else
                    {
                        if (ImGui.SmallButton(Loc.Get("Editor.AddWait") + "##addwait"))
                            action.WaitAfter = 1f;
                    }
                }

                var hasNote = !string.IsNullOrEmpty(action.Note);
                ImGui.SameLine(rowAvail - delBtnW * 3f - rowSpacing * 2f - 4f * ImGuiHelpers.GlobalScale);
                if (hasNote) ImGui.PushStyleColor(ImGuiCol.Text, MacroFolioTheme.AccentColor);
                bool noteClicked;
                using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    noteClicked = ImGui.Button(FontAwesomeIcon.PencilAlt.ToIconString() + "##note", new Vector2(delBtnW, 0));
                if (noteClicked)
                    editingNoteRow = editingNoteRow == i ? -1 : i;
                if (hasNote) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(Loc.Get("Editor.NoteTooltip"));
                    ImGui.EndTooltip();
                }

                ImGui.SameLine(rowAvail - delBtnW * 2f - rowSpacing - 4f * ImGuiHelpers.GlobalScale);
                if (ImGui.Button("+##ins", new Vector2(delBtnW, 0)))
                {
                    pendingInsertIndex = i + 1;
                    requestOpenAddMenu = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(Loc.Get("Editor.InsertBelow"));
                    ImGui.EndTooltip();
                }

                ImGui.SameLine(rowAvail - delBtnW - 4f * ImGuiHelpers.GlobalScale);
                if (ImGui.Button("X##del", new Vector2(delBtnW, 0)))
                    removeIndex = i;

                var indent = gripW + stampW + rowSpacing * 2f;
                if (editingNoteRow == i)
                {
                    ImGui.SetCursorPosX(indent);
                    ImGui.SetNextItemWidth(rowAvail - indent - delBtnW * 3f - rowSpacing * 3f);
                    var note = action.Note;
                    if (ImGui.InputTextWithHint("##note_edit", Loc.Get("Editor.NoteEditHint"), ref note, 256))
                        action.Note = note;
                    if (ImGui.IsItemDeactivated())
                        editingNoteRow = -1;
                }
                else if (hasNote)
                {
                    var noteColor = MacroFolioTheme.AccentColor with { W = 0.6f };
                    ImGui.SetCursorPosX(indent);
                    using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                        ImGui.TextColored(noteColor, FontAwesomeIcon.StickyNote.ToIconString());
                    ImGui.SameLine(0f, 4f);
                    ImGui.TextColored(noteColor, action.Note);
                }

                ImGui.PopID();
            }
            if (removeIndex >= 0)
                draftActionList.RemoveAt(removeIndex);

            if (pendingActionMove is { } move &&
                move.From >= 0 && move.From < draftActionList.Count &&
                move.To >= 0 && move.To < draftActionList.Count &&
                move.From != move.To)
            {
                var item = draftActionList[move.From];
                draftActionList.RemoveAt(move.From);
                draftActionList.Insert(move.To, item);
            }
            pendingActionMove = null;
        }
        ImGui.EndChild();

        ImGuiHelpers.ScaledDummy(6f);

        var atMax = !Unlimited && draftActionList.Count >= MaxLines;
        if (atMax) ImGui.BeginDisabled();
        var addLabel = Loc.Get("Editor.AddAction");
        var addBtnW = ImGui.CalcTextSize(addLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SetCursorPosX((availWidth - addBtnW) / 2f);
        if (ImGui.Button(addLabel))
        {
            pendingInsertIndex = -1;
            requestOpenAddMenu = true;
        }
        if (atMax) ImGui.EndDisabled();

        if (requestOpenAddMenu)
        {
            ImGui.OpenPopup("##add_action_menu");
            requestOpenAddMenu = false;
        }
        if (ImGui.BeginPopup("##add_action_menu"))
        {
            var canAddRegular = Unlimited || draftActionList.Count < MaxLines;
            var canAddTimer = canAddRegular && CanAddTimer();
            var canAddNext = canAddRegular && CanAddNextMacro();

            if (ImGui.MenuItem(KindLabel(ActionKind.Emote), "", false, canAddRegular))           AppendAction(ActionKind.Emote);
            if (ImGui.MenuItem(KindLabel(ActionKind.EmotePredefini), "", false, canAddRegular))  AppendAction(ActionKind.EmotePredefini);
            if (ImGui.MenuItem(KindLabel(ActionKind.SkillAction), "", false, canAddRegular))     AppendAction(ActionKind.SkillAction);
            ImGui.Separator();
            if (ImGui.MenuItem(KindLabel(ActionKind.SayParler), "", false, canAddRegular))       AppendAction(ActionKind.SayParler);
            if (ImGui.MenuItem(KindLabel(ActionKind.SayCrier), "", false, canAddRegular))        AppendAction(ActionKind.SayCrier);
            if (ImGui.MenuItem(KindLabel(ActionKind.SayHurler), "", false, canAddRegular))       AppendAction(ActionKind.SayHurler);
            if (ImGui.MenuItem(KindLabel(ActionKind.SayGroupe), "", false, canAddRegular))       AppendAction(ActionKind.SayGroupe);
            if (ImGui.MenuItem(KindLabel(ActionKind.Echo), "", false, canAddRegular))            AppendAction(ActionKind.Echo);
            ImGui.Separator();
            if (ImGui.MenuItem(KindLabel(ActionKind.Countdown), "", false, canAddRegular))       AppendAction(ActionKind.Countdown);
            if (ImGui.MenuItem(KindLabel(ActionKind.Cibler), "", false, canAddRegular))          AppendAction(ActionKind.Cibler);
            if (ImGui.MenuItem(KindLabel(ActionKind.CiblerSoi), "", false, canAddRegular))       AppendAction(ActionKind.CiblerSoi);
            if (ImGui.MenuItem(KindLabel(ActionKind.CiblerPj), "", false, canAddRegular))        AppendAction(ActionKind.CiblerPj);
            if (ImGui.MenuItem(KindLabel(ActionKind.EchoSound), "", false, canAddRegular))       AppendAction(ActionKind.EchoSound);
            ImGui.Separator();
            if (ImGui.MenuItem(KindLabel(ActionKind.Timer), "", false, canAddTimer))             AppendAction(ActionKind.Timer);
            if (ImGui.MenuItem(KindLabel(ActionKind.NextMacro), "", false, canAddNext))          AppendAction(ActionKind.NextMacro);
            ImGui.Separator();
            if (ImGui.MenuItem(KindLabel(ActionKind.Brut), "", false, canAddRegular))            AppendAction(ActionKind.Brut);
            ImGui.EndPopup();
        }

        ImGuiHelpers.ScaledDummy(4f);
        DrawLineCounter(draftActionList.Count, !Unlimited && draftActionList.Count > MaxLines, availWidth);
    }
    
    private int InsertIndex()
    {
        var idx = draftActionList.Count;
        if (idx > 0 && draftActionList[idx - 1].Kind == ActionKind.NextMacro)
            idx--;
        return idx;
    }
    
    private int ResolveInsertIndex()
    {
        var max = InsertIndex();
        return pendingInsertIndex < 0 ? max : Math.Clamp(pendingInsertIndex, 0, max);
    }

    private bool CanAddTimer()
    {
        var idx = ResolveInsertIndex();
        if (idx <= 0) return false;
        var prev = draftActionList[idx - 1];
        return prev.Kind != ActionKind.Timer
               && prev.Kind != ActionKind.NextMacro
               && prev.WaitAfter is null;
    }

    private bool CanAddNextMacro()
        => draftActionList.All(a => a.Kind != ActionKind.NextMacro);

    private void AppendAction(ActionKind kind)
    {
        var insertAt = ResolveInsertIndex();

        if (kind == ActionKind.Timer)
        {
            if (insertAt > 0)
            {
                var prev = draftActionList[insertAt - 1];
                if (prev.Kind != ActionKind.Timer && prev.Kind != ActionKind.NextMacro && prev.WaitAfter is null)
                    prev.WaitAfter = 1;
            }
            pendingInsertIndex = -1;
            return;
        }

        if (kind == ActionKind.NextMacro)
        {
            if (CanAddNextMacro() && (Unlimited || draftActionList.Count < MaxLines))
                draftActionList.Add(new SimplifiedAction { Kind = kind });
            pendingInsertIndex = -1;
            return;
        }

        if (Unlimited || draftActionList.Count < MaxLines)
            draftActionList.Insert(insertAt, new SimplifiedAction { Kind = kind });
        pendingInsertIndex = -1;
    }

    private void DrawLineCounter(int lineCount, bool overLimit, float availWidth)
    {
        var counter = Unlimited
            ? string.Format(Loc.Get(lineCount > 1 ? "Editor.LineCounterUnlimitedPlural" : "Editor.LineCounterUnlimited"), lineCount)
            : string.Format(Loc.Get("Editor.LineCounter"), lineCount, MaxLines);
        var counterColor = overLimit ? new Vector4(0.9f, 0.3f, 0.3f, 1f) : new Vector4(0.6f, 0.6f, 0.6f, 1f);
        var counterSize = ImGui.CalcTextSize(counter);
        ImGui.SetCursorPosX((availWidth - counterSize.X) / 2f);
        ImGui.TextColored(counterColor, counter);
    }


    private static float StepSeconds(SimplifiedAction a) =>
        a.Kind switch
        {
            ActionKind.Timer => Math.Max(0f, a.WaitSeconds),
            _ => a.WaitAfter is > 0f ? a.WaitAfter.Value : 0f,
        };

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 1;
        foreach (var c in text)
            if (c == '\n') count++;
        return count;
    }


    private static string FormatTimestamp(float seconds)
    {
        seconds = Math.Max(0f, seconds);
        var min = (int)(seconds / 60f);
        var sec = seconds - min * 60f;
        var rounded = (float)Math.Round(sec, 1);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.05f)
            return $"{min}:{(int)Math.Round(rounded):D2}";
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1:00.0}", min, rounded);
    }

    private static bool TryParseTimestamp(string s, out float seconds)
    {
        seconds = 0f;
        if (string.IsNullOrWhiteSpace(s)) return true;
        var trimmed = s.Trim();
        var parts = trimmed.Split(':');
        if (parts.Length == 1)
            return float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var m) &&
            float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s2))
        {
            seconds = m * 60f + s2;
            return true;
        }
        return false;
    }

    private static string FormatWait(float v)
        => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static bool TryParseWait(string s, out float v)
        => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);


    private static string KindLabel(ActionKind k) => k switch
    {
        ActionKind.Emote => Loc.Get("Action.Label.Emote"),
        ActionKind.EmotePredefini => Loc.Get("Action.Label.EmotePredefini"),
        ActionKind.SkillAction => Loc.Get("Action.Label.SkillAction"),
        ActionKind.SayParler => Loc.Get("Action.Label.SayParler"),
        ActionKind.SayCrier => Loc.Get("Action.Label.SayCrier"),
        ActionKind.SayHurler => Loc.Get("Action.Label.SayHurler"),
        ActionKind.SayGroupe => Loc.Get("Action.Label.SayGroupe"),
        ActionKind.Echo => Loc.Get("Action.Label.Echo"),
        ActionKind.Countdown => Loc.Get("Action.Label.Countdown"),
        ActionKind.Cibler => Loc.Get("Action.Label.Cibler"),
        ActionKind.CiblerSoi => Loc.Get("Action.Label.CiblerSoi"),
        ActionKind.CiblerPj => Loc.Get("Action.Label.CiblerPj"),
        ActionKind.EchoSound => Loc.Get("Action.Label.EchoSound"),
        ActionKind.Timer => Loc.Get("Action.Label.Timer"),
        ActionKind.NextMacro => Loc.Get("Action.Label.NextMacro"),
        ActionKind.Brut => Loc.Get("Action.Label.Brut"),
        _ => "?",
    };

    private static string KindPrefix(ActionKind k) => k switch
    {
        ActionKind.Emote => "/em",
        ActionKind.EmotePredefini => "Emote",
        ActionKind.SkillAction => "/ac",
        ActionKind.SayParler => "/say",
        ActionKind.SayCrier => "/yell",
        ActionKind.SayHurler => "/shout",
        ActionKind.SayGroupe => "/party",
        ActionKind.Echo => "/e",
        ActionKind.Countdown => "/cdr",
        ActionKind.Cibler => "/cibler",
        ActionKind.CiblerSoi => "/cibler",
        ActionKind.CiblerPj => "/ciblerpj",
        ActionKind.EchoSound => "/e",
        ActionKind.Timer => "<wait>",
        ActionKind.NextMacro => "/nextmacro",
        _ => "",
    };


    private static string SerializeAction(SimplifiedAction a)
    {
        var body = a.Kind switch
        {
            ActionKind.Emote => $"/em {a.Text}".TrimEnd(),
            ActionKind.EmotePredefini => SerializeEmotePredefini(a),
            ActionKind.SkillAction => string.IsNullOrEmpty(a.Text) ? "" : $"/ac \"{a.Text}\"",
            ActionKind.SayParler => $"/say {a.Text}".TrimEnd(),
            ActionKind.SayCrier => $"/yell {a.Text}".TrimEnd(),
            ActionKind.SayHurler => $"/shout {a.Text}".TrimEnd(),
            ActionKind.SayGroupe => $"/party {a.Text}".TrimEnd(),
            ActionKind.Countdown => string.IsNullOrWhiteSpace(a.Text)
                ? "/compteàrebours"
                : $"/compteàrebours {a.Text.Trim()}",
            ActionKind.Echo => $"/e {a.Text}".TrimEnd(),
            ActionKind.Cibler => string.IsNullOrWhiteSpace(a.Text)
                ? "/cibler"
                : $"/cibler {a.Text.Trim()}",
            ActionKind.CiblerSoi => "/cibler <1>",
            ActionKind.CiblerPj => string.IsNullOrWhiteSpace(a.Text)
                ? "/ciblerpj"
                : $"/ciblerpj {a.Text.Trim()}",
            ActionKind.EchoSound => SerializeEchoSound(a),
            ActionKind.Timer => $"<wait.{FormatWait(Math.Max(0.1f, a.WaitSeconds))}>",
            ActionKind.NextMacro => "/nextmacro",
            _ => a.Text,
        };

        if (a.Kind != ActionKind.Timer && a.Kind != ActionKind.NextMacro && a.WaitAfter is > 0f)
            body = $"{body} <wait.{FormatWait(a.WaitAfter.Value)}>".TrimStart();

        return body;
    }

    private static string SerializeEmotePredefini(SimplifiedAction a)
    {
        if (string.IsNullOrEmpty(a.Text)) return "";
        return a.SilentAnimation ? $"{a.Text} animation" : a.Text;
    }

    private static string SerializeEchoSound(SimplifiedAction a)
    {
        var sid = Math.Clamp(a.SoundId, 1, 16);
        var msg = string.IsNullOrWhiteSpace(a.Text) ? "" : a.Text.Trim();
        return msg.Length == 0 ? $"/e <se.{sid}>" : $"/e {msg} <se.{sid}>";
    }

    // --- Parsing ---

    // Matches a <wait.N> / <wait:N> token ANYWHERE in the line (not only at the
    // end), so pasted macros with a trailing manual timestamp (e.g.
    // "/montrer animation <wait.5> 0:59") still have their wait extracted.
    private static readonly System.Text.RegularExpressions.Regex InlineWaitRegex =
        new(@"<wait[.:](\d+(?:\.\d+)?)>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex SoundTagRegex =
        new(@"\s*<se[.:](\d+)>\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static SimplifiedAction ParseAction(string line)
    {
        float? inlineWait = null;
        var working = line;

        // Pull out the first <wait.N> wherever it sits; the remaining text is the
        // actual command. Whitespace left by the removal is collapsed.
        var waitMatch = InlineWaitRegex.Match(working);
        if (waitMatch.Success && TryParseWait(waitMatch.Groups[1].Value, out var ws) && ws > 0f)
        {
            inlineWait = ws;
            working = (working.Remove(waitMatch.Index, waitMatch.Length)).Replace("  ", " ").Trim();
        }

        // A line that was only a wait token becomes a Timer.
        if (inlineWait.HasValue && working.Length == 0)
            return new SimplifiedAction { Kind = ActionKind.Timer, WaitSeconds = inlineWait.Value };

        var action = ParseBody(working);
        if (inlineWait.HasValue && action.Kind != ActionKind.Timer)
            action.WaitAfter = inlineWait;
        return action;
    }

    private static SimplifiedAction ParseBody(string line)
    {
        static bool Match(string src, string prefix, out string rest)
        {
            if (src.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                rest = src.Substring(prefix.Length).TrimStart();
                return true;
            }
            rest = "";
            return false;
        }

        if (Match(line, "/em ", out var emText)) return new SimplifiedAction { Kind = ActionKind.Emote, Text = emText };

        if (Match(line, "/compteàrebours ", out var cd1)) return new SimplifiedAction { Kind = ActionKind.Countdown, Text = cd1 };
        if (Match(line, "/countdown ", out var cd2)) return new SimplifiedAction { Kind = ActionKind.Countdown, Text = cd2 };

        if (Match(line, "/ciblerpj ", out var tgtPj1)) return new SimplifiedAction { Kind = ActionKind.CiblerPj, Text = tgtPj1 };
        if (Match(line, "/targetpc ", out var tgtPj2)) return new SimplifiedAction { Kind = ActionKind.CiblerPj, Text = tgtPj2 };
        if (line.Trim().Equals("/ciblerpj", StringComparison.OrdinalIgnoreCase)
            || line.Trim().Equals("/targetpc", StringComparison.OrdinalIgnoreCase))
            return new SimplifiedAction { Kind = ActionKind.CiblerPj };

        var selfTrim = line.Trim();
        if (selfTrim.Equals("/cibler <1>", StringComparison.OrdinalIgnoreCase)
            || selfTrim.Equals("/target <1>", StringComparison.OrdinalIgnoreCase))
            return new SimplifiedAction { Kind = ActionKind.CiblerSoi };

        if (Match(line, "/cibler ", out var tgt1)) return new SimplifiedAction { Kind = ActionKind.Cibler, Text = tgt1 };
        if (Match(line, "/target ", out var tgt2)) return new SimplifiedAction { Kind = ActionKind.Cibler, Text = tgt2 };
        if (line.Trim().Equals("/cibler", StringComparison.OrdinalIgnoreCase)
            || line.Trim().Equals("/target", StringComparison.OrdinalIgnoreCase))
            return new SimplifiedAction { Kind = ActionKind.Cibler };

        string? echoBody = null;
        if (Match(line, "/e ", out var er1)) echoBody = er1;
        else if (Match(line, "/echo ", out var er2)) echoBody = er2;
        if (echoBody is not null)
        {
            var seMatch = SoundTagRegex.Match(echoBody);
            if (seMatch.Success && int.TryParse(seMatch.Groups[1].Value, out var sid) && sid is >= 1 and <= 16)
            {
                var msg = echoBody.Substring(0, seMatch.Index).TrimEnd();
                return new SimplifiedAction { Kind = ActionKind.EchoSound, Text = msg, SoundId = sid };
            }
            return new SimplifiedAction { Kind = ActionKind.Echo, Text = echoBody };
        }

        string? acRest = null;
        if (Match(line, "/ac ", out var ac1)) acRest = ac1;
        else if (Match(line, "/action ", out var ac2)) acRest = ac2;
        if (acRest is not null)
        {
            var name = acRest.Trim();
            if (name.Length >= 2 && name.StartsWith('"') && name.EndsWith('"'))
                name = name[1..^1];
            if (name.Length > 0)
                return new SimplifiedAction { Kind = ActionKind.SkillAction, Text = name };
        }

        if (Match(line, "/say ", out var sayText)) return new SimplifiedAction { Kind = ActionKind.SayParler, Text = sayText };
        if (Match(line, "/yell ", out var yellText)) return new SimplifiedAction { Kind = ActionKind.SayCrier, Text = yellText };
        if (Match(line, "/shout ", out var shoutText)) return new SimplifiedAction { Kind = ActionKind.SayHurler, Text = shoutText };
        if (Match(line, "/party ", out var pText)) return new SimplifiedAction { Kind = ActionKind.SayGroupe, Text = pText };
        if (Match(line, "/wait ", out var wText))
        {
            if (int.TryParse(wText.Trim(), out var s) && s > 0)
                return new SimplifiedAction { Kind = ActionKind.Timer, WaitSeconds = s };
        }

        if (string.Equals(line.Trim(), "/nextmacro", StringComparison.OrdinalIgnoreCase))
            return new SimplifiedAction { Kind = ActionKind.NextMacro };

        var waitTrim = line.Trim();
        if ((waitTrim.StartsWith("<wait.", StringComparison.OrdinalIgnoreCase)
             || waitTrim.StartsWith("<wait:", StringComparison.OrdinalIgnoreCase))
            && waitTrim.EndsWith('>'))
        {
            var body = waitTrim[6..^1].Trim();
            if (int.TryParse(body, out var s) && s > 0)
                return new SimplifiedAction { Kind = ActionKind.Timer, WaitSeconds = s };
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith('/'))
        {
            var parts = trimmed.Split(' ', 2);
            var cmd = parts[0];
            var rest = parts.Length > 1 ? parts[1].Trim() : "";
            if (rest.Length == 0 || string.Equals(rest, "animation", StringComparison.OrdinalIgnoreCase))
            {
                EnsureUnlockedEmotes();
                if (unlockedEmoteCommands is not null && unlockedEmoteCommands.Contains(cmd))
                {
                    return new SimplifiedAction
                    {
                        Kind = ActionKind.EmotePredefini,
                        Text = cmd,
                        SilentAnimation = rest.Length > 0,
                    };
                }
            }
        }

        return new SimplifiedAction { Kind = ActionKind.Brut, Text = line };
    }


    private static List<JobActionOption> EnsureJobActions()
    {
        var jobId = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        if (cachedJobActions is not null && cachedJobActionsForId == jobId)
            return cachedJobActions;

        cachedJobActionsForId = jobId;
        var list = new List<JobActionOption>();

        if (jobId == 0)
            return cachedJobActions = list;

        try
        {
            var actionSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            var jobSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();

            var jobIds = new HashSet<uint>();
            var current = jobId;
            while (jobIds.Add(current))
            {
                var row = jobSheet.GetRowOrDefault(current);
                if (row is null) break;
                var parent = row.Value.ClassJobParent.RowId;
                if (parent == 0 || parent == current) break;
                current = parent;
            }

            foreach (var act in actionSheet)
            {
                if (act.RowId == 0) continue;
                if (!act.IsPlayerAction) continue;
                if (!jobIds.Contains(act.ClassJob.RowId)) continue;

                var name = act.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name)) continue;

                list.Add(new JobActionOption(name));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to enumerate job actions.");
        }

        cachedJobActions = list
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return cachedJobActions;
    }

    private static unsafe List<EmoteOption> EnsureUnlockedEmotes()
    {
        if (unlockedEmotes is not null)
            return unlockedEmotes;

        var list = new List<EmoteOption>();
        var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>();
            var uiState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
            if (uiState == null)
            {
                unlockedEmoteCommands = commands;
                return unlockedEmotes = list;
            }

            foreach (var emote in sheet)
            {
                if (emote.RowId == 0) continue;
                if (!uiState->IsEmoteUnlocked((ushort)emote.RowId)) continue;

                var textCmdRef = emote.TextCommand.ValueNullable;
                if (textCmdRef is null) continue;
                
                var variants = EmoteCommandVariants(textCmdRef.Value);
                if (variants.Count == 0) continue;
                foreach (var v in variants) commands.Add(v);

                var name = emote.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    name = variants[0];

                list.Add(new EmoteOption(variants[0], name));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to enumerate unlocked emotes.");
        }

        unlockedEmoteCommands = commands;
        unlockedEmotes = list
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return unlockedEmotes;
    }

    private static List<string> EmoteCommandVariants(Lumina.Excel.Sheets.TextCommand tc)
    {
        var result = new List<string>(4);
        foreach (var raw in new[] { tc.Command, tc.ShortCommand, tc.Alias, tc.ShortAlias })
        {
            var s = raw.ExtractText();
            if (!string.IsNullOrWhiteSpace(s) && s.StartsWith('/') && !result.Contains(s))
                result.Add(s);
        }
        return result;
    }
}
