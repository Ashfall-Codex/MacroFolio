using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace MacroFolio.Services;

public sealed unsafe class ChatSenderService
{
    // The chat input accepts up to 500 bytes (UTF-8). Keep a small safety margin.
    public const int MaxChatBytes = 500;

    public static List<string> Split(string text)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        // Normalize newlines; treat each line as its own message boundary first.
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0) continue;
            SplitLine(trimmed, chunks);
        }
        return chunks;
    }

    private static void SplitLine(string line, List<string> chunks)
    {
        if (Encoding.UTF8.GetByteCount(line) <= MaxChatBytes)
        {
            chunks.Add(line);
            return;
        }

        // Greedy word packing; fall back to hard char split for very long words.
        var current = new StringBuilder();
        foreach (var word in line.Split(' '))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (Encoding.UTF8.GetByteCount(candidate) <= MaxChatBytes)
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
                continue;
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (Encoding.UTF8.GetByteCount(word) <= MaxChatBytes)
            {
                current.Append(word);
            }
            else
            {
                HardSplit(word, chunks);
            }
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());
    }

    private static void HardSplit(string word, List<string> chunks)
    {
        var current = new StringBuilder();
        foreach (var ch in word)
        {
            if (Encoding.UTF8.GetByteCount(current.ToString() + ch) > MaxChatBytes)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            current.Append(ch);
        }
        if (current.Length > 0)
            chunks.Add(current.ToString());
    }

    public static List<string> BuildPreviewChunks(string text) => Split(text);
    public const float SendIntervalSeconds = 3f;

    private readonly Queue<string> sendQueue = new();
    private float sendTimer;

    public bool IsSending => sendQueue.Count > 0;
    public int PendingCount => sendQueue.Count;
    public int SendDirect(string text)
    {
        var chunks = Split(text);
        foreach (var chunk in chunks)
            sendQueue.Enqueue(chunk);

        // Fire the first one immediately on the next tick.
        if (chunks.Count > 0)
            sendTimer = 0f;

        return chunks.Count;
    }

    /// <summary>Advance the timed send queue. Call once per frame with the frame delta.</summary>
    public void Tick(float deltaSeconds)
    {
        if (sendQueue.Count == 0) return;

        sendTimer -= deltaSeconds;
        if (sendTimer > 0f) return;

        var message = sendQueue.Dequeue();
        SendOne(message);

        // Schedule the next message only if more remain.
        sendTimer = sendQueue.Count > 0 ? SendIntervalSeconds : 0f;
    }

    private static bool SendOne(string message)
    {
        var ui = UIModule.Instance();
        if (ui is null) return false;

        var bytes = Encoding.UTF8.GetByteCount(message);
        if (bytes is 0 or > MaxChatBytes) return false;

        var utf8 = Utf8String.FromString(message);
        try
        {
            ui->ProcessChatBoxEntry(utf8);
            return true;
        }
        finally
        {
            utf8->Dtor(true);
        }
    }
}
