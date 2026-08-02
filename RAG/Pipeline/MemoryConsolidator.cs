using NPCRAGSystem.Utils;
using System.Text.Json;
using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.RAG.Pipeline;

public class MemoryConsolidator : IMemoryConsolidator
{
    private readonly ILlmService _llm;

    public MemoryConsolidator(ILlmService llm)
    {
        _llm = llm;
    }

    public async Task<List<NpcMemory>> ConsolidateAsync(
        string npcName,
        List<NpcMemory> sessionMemories,
        List<NpcMemory> existingMemories)
    {
        if (sessionMemories.Count == 0)
            return sessionMemories;

        // Only worth running if there are potential duplicates/overlaps
        var allCandidates = sessionMemories.Concat(existingMemories).ToList();
        if (allCandidates.Count < 2)
            return sessionMemories;

        var sessionBlock = string.Join("\n", sessionMemories.Select((m, i) =>
            $"[{i}] (fidelity {m.Fidelity:F2}) {m.Content}"));

        var existingBlock = existingMemories.Count > 0
            ? string.Join("\n", existingMemories.Select(m =>
                $"- (fidelity {m.Fidelity:F2}) {m.Content}"))
            : "None.";

        var systemPrompt =
            $$"""
            You are consolidating {{npcName}}'s memories about a traveller after a conversation session.

            EXISTING memories (already stored — do not duplicate these):
            {{existingBlock}}

            NEW memories from this session (these are your input to consolidate):
            {{sessionBlock}}

            Rules:
            1. Merge any new memories that describe the same fact or observation — keep the most
               informative wording and use the HIGHEST fidelity of the merged group.
            2. Drop any new memory that is already covered by the existing memories list.
            3. Keep memories that add genuinely new information even if fidelity is low.
            4. Return only the consolidated new memories (not the existing ones) as a JSON array.
               If nothing survives consolidation, return: []
               No preamble. JSON array only:
               [
                 { "content": "The traveller told me [fact]", "fidelity": 0.75 },
                 { "content": "The traveller seemed interested in [observation]", "fidelity": 0.40 }
               ]
            """;

        var userPrompt = "Consolidate the session memories. Return a JSON array or [].";

        var raw = await _llm.GenerateJsonAsync(systemPrompt, userPrompt);

        // Tolerates preamble, trailing prose and truncated arrays; an unparseable/empty reply
        // yields an empty list (nothing survived consolidation).
        return LlmJson.ParseList(raw, e => ParseMemories(e, sessionMemories));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<NpcMemory> ParseMemories(
        JsonElement root, List<NpcMemory> originals)
    {
        var results = new List<NpcMemory>();

        var items = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : new List<JsonElement> { root };

        foreach (var item in items)
        {
            if (!item.TryGetProperty("content", out var contentEl)) continue;
            if (!item.TryGetProperty("fidelity", out var fidelityEl)) continue;

            var content = contentEl.GetString() ?? string.Empty;
            var fidelity = Math.Clamp(fidelityEl.GetSingle(), 0f, 1f);

            if (string.IsNullOrWhiteSpace(content)) continue;

            // Preserve metadata from the original this consolidated memory most resembles.
            // Exact match first; otherwise the closest original by token overlap, so a
            // merged/reworded memory still inherits a decay anchor, nature and trauma flag.
            var original = originals.FirstOrDefault(m =>
                    m.Content.Equals(content, StringComparison.OrdinalIgnoreCase))
                ?? originals
                    .Select(m => (memory: m, score: StringUtils.JaccardSimilarity(m.Content, content)))
                    .Where(x => x.score > 0.3f)
                    .OrderByDescending(x => x.score)
                    .Select(x => x.memory)
                    .FirstOrDefault();

            // Final fallback: any session original. They share this session's timestamp,
            // so an unmatched merge never ends up with an empty (and thus non-decaying) stamp.
            var anchor = original ?? originals.FirstOrDefault();

            results.Add(new NpcMemory
            {
                Id = original?.Id ?? Guid.NewGuid().ToString("N")[..8],
                Content = content,
                Fidelity = fidelity,
                InitialFidelity = fidelity,
                DecayWeight = anchor?.DecayWeight ?? 1.0f,
                TraumaTagged = anchor?.TraumaTagged ?? false,
                Timestamp = anchor?.Timestamp ?? string.Empty,
                Nature = anchor?.Nature ?? MemoryNature.Fact,
                Credibility = anchor?.Credibility ?? 1.0f
            });
        }

        return results;
    }
}
