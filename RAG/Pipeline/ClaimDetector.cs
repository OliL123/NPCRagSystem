using NPCRAGSystem.Utils;
using System.Text.Json;
using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Domain.Npc;
using NPCRAGSystem.Interfaces.Core.NPC;

namespace NPCRAGSystem.RAG.Pipeline;

public class ClaimDetectionResult
{
    public string Type { get; set; } = "none"; // none | contradiction | accusation | joke
    public string? ConflictingMemoryId { get; set; }
    public string? Description { get; set; }
    public float Severity { get; set; }
}

public class ClaimDetector
{
    private readonly ILlmService _llm;

    // Don't run detection on trivially short or obviously question-only inputs
    private const int MinLength = 8;

    public ClaimDetector(ILlmService llm)
    {
        _llm = llm;
    }

    public async Task<ClaimDetectionResult> DetectAsync(
        string playerMessage,
        NpcState npc,
        IReadOnlyList<ConversationTurn> recentHistory)
    {
        var none = new ClaimDetectionResult();

        if (playerMessage.Trim().Length < MinLength) return none;
        if (IsLikelyPureQuestion(playerMessage)) return none;

        // Build compact memory list with IDs for reference
        var allMemories = npc.WorldMemories
            .Concat(npc.OrphanMemories)
            .Concat(npc.SuspectMemories)
            .Where(m => m.Fidelity > 0.2f)
            .ToList();

        if (allMemories.Count == 0 && recentHistory.Count == 0) return none;

        var memoryLines = allMemories.Count > 0
            ? string.Join("\n", allMemories.Select(m => $"[{m.Id}] ({m.Nature}) {m.Content}"))
            : "None.";

        var historyLines = recentHistory.Count > 0
            ? string.Join("\n", recentHistory.TakeLast(4).Select(t => $"Traveller: {t.PlayerMessage}\n{npc.Name}: {t.NpcResponse}"))
            : "No prior conversation.";

        var system = $$"""
            You are a reasoning engine for an NPC in a text RPG. Analyze the traveller's latest statement.
            Given what {{npc.Name}} knows, classify it:

            "none"         — consistent with known facts, or no assertable claim made
            "contradiction"— directly contradicts something {{npc.Name}} remembers
            "accusation"   — accuses {{npc.Name}} of wrongdoing, lying, or something negative
            "joke"         — implausible claim (e.g. someone soaked in a trough claiming to be Emperor) — bravado, not deception

            Context matters: consider who the traveller appears to be and what has been established.
            A contradiction requires a specific conflicting memory — do not flag vague inconsistencies.

            Only a STATEMENT can be a claim. A question asks for information and asserts nothing —
            classify questions as "none". Small talk, greetings and opinions assert nothing either.

            A remark about {{npc.Name}}'s visible condition that MATCHES the current condition below
            is simply TRUE — it is "none", never a contradiction. Memories describe the past; the
            condition block describes right now, and the condition block wins.

            Output ONLY valid JSON, no other text:
            {
              "type": "none|contradiction|accusation|joke",
              "conflicting_memory_id": "<id from memory list, or null>",
              "description": "<one sentence explanation, or null>",
              "severity": 0.0
            }
            severity: 0.0–1.0 (how serious the contradiction/accusation is; 0 for none/joke)
            """;

        var user = $"""
            {npc.Name}'s memories about the traveller:
            {memoryLines}

            {npc.Name}'s condition RIGHT NOW (objectively true, and much of it visible to anyone looking):
            {BuildConditionLine(npc)}

            Recent conversation:
            {historyLines}

            Traveller just said: "{playerMessage}"

            Classify this statement. Output JSON only.
            """;

        try
        {
            var raw = await _llm.GenerateJsonAsync(system, user, numKeep: 0);
            var trimmed = LlmJson.Extract(raw);
            if (string.IsNullOrEmpty(trimmed)) return none;

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            // The model's output is untrusted input — the prompt states a contract, so enforce it
            // here rather than letting whatever came back drive state mutation downstream.
            var type = root.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString() ?? "none"
                : "none";

            // Unknown type → treat as none. Anything else would pass the != "none" check, roll the
            // gullibility gate, and then match no case in the handler's switch.
            if (type is not ("contradiction" or "accusation" or "joke")) return none;

            var conflictId = root.TryGetProperty("conflicting_memory_id", out var idProp) && idProp.ValueKind != JsonValueKind.Null
                ? idProp.GetString()
                : null;

            // Only accept an id that actually exists in the list we handed the model. A hallucinated
            // id would silently no-op downstream; worse, an id picked for the wrong reason corrupts
            // a real memory. Contradiction requires a genuine conflicting memory by definition.
            if (conflictId != null && !allMemories.Any(m => m.Id == conflictId))
                conflictId = null;
            if (type == "contradiction" && conflictId == null) return none;

            var description = root.TryGetProperty("description", out var descProp) && descProp.ValueKind != JsonValueKind.Null
                ? descProp.GetString()
                : null;

            var severity = root.TryGetProperty("severity", out var sevProp)
                ? Math.Clamp(sevProp.GetSingle(), 0f, 1f)
                : 0.5f;

            // The prompt specifies severity 0 for a joke; the model does not reliably honour it, and
            // severity scales suspicion and selects the injected constraint.
            if (type == "joke") severity = 0f;

            return new ClaimDetectionResult
            {
                Type = type,
                ConflictingMemoryId = conflictId,
                Description = description,
                Severity = severity,
            };
        }
        catch
        {
            return none;
        }
    }

    // The detector decides whether a statement conflicts with reality, so it needs the NPC's actual
    // condition — not only their memories. Memories describe a past self ("I run the rooftops"); the
    // live state describes now. Without this, telling an exhausted NPC "you look terrible" reads as
    // contradicting a memory of a healthy one, and a true remark gets punished as a lie.
    private static string BuildConditionLine(NpcState npc)
    {
        var e = npc.EmotionalState;
        var p = npc.PhysicalState;
        var notable = new List<string>();

        void Add(string label, float v)
        {
            if (v < 0.4f) return;
            var intensity = v >= 0.8f ? "severe" : v >= 0.6f ? "strong" : "moderate";
            notable.Add($"{label} — {intensity}");
        }

        Add("exhaustion", p.Exhaustion);
        Add("pain", p.Pain);
        Add("illness", p.Illness);
        Add("intoxication", p.Intoxication);
        Add("hunger", p.Hunger);
        Add("anger", e.Anger);
        Add("fear", e.Fear);
        Add("grief", e.Grief);
        Add("anxiety", e.Anxiety);
        Add("suspicion", e.Suspicion);

        return notable.Count > 0
            ? string.Join("; ", notable)
            : "nothing notable — in ordinary condition";
    }

    // Interrogatives that open a question. Matched against the first word, so contracted forms
    // ("what's", "who're") are caught too — the old prefix test looked for "what " and let every
    // contraction through to the LLM, which then invented claims out of plain questions.
    private static readonly HashSet<string> QuestionOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "where", "who", "when", "how", "why", "which", "whose",
        "is", "are", "was", "were", "do", "does", "did", "can", "could",
        "have", "has", "had", "will", "would", "should",
    };

    private static bool IsLikelyPureQuestion(string input)
    {
        var t = input.TrimStart();
        if (t.Length == 0) return false;

        // First whitespace-delimited word, minus any contraction tail ("what's" → "what") and
        // leading punctuation.
        var firstWord = t.Split(' ', '\t', '\n')[0].TrimStart('"', '\'', '(');
        var apostrophe = firstWord.IndexOfAny(new[] { '\'', '’' });
        if (apostrophe > 0) firstWord = firstWord[..apostrophe];
        firstWord = firstWord.Trim(',', '.', '?', '!', ';', ':');

        return QuestionOpeners.Contains(firstWord);
    }
}
