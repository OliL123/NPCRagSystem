using NPCRAGSystem.Utils;
﻿using System.Text.Json;
using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.RAG.Pipeline;

public class ConversationMemoryCreator : IConversationMemoryCreator
{
	private readonly ILlmService _llm;

	private const int MinInputLength = 10;

	public ConversationMemoryCreator(ILlmService llm)
	{
		_llm = llm;
	}

	public async Task<List<NpcMemory>> TryCreateMemoriesAsync(
		string playerMessage,
		string npcResponse,
		NpcState npc,
		float beliefBaseline,
		int currentDay)
	{
		if (playerMessage.Trim().Length < MinInputLength)
			return new List<NpcMemory>();

		var existingMemories = npc.OrphanMemories.Count > 0
			? string.Join("\n", npc.OrphanMemories.Select(m =>
				$"- [{m.Fidelity:F1}] {m.Content}"))
			: "None yet.";

		var systemPrompt =
			$$"""
            You are analysing a single conversation turn to decide what {{npc.Name}} should
            remember about this traveller afterwards. Write in {{npc.Name}}'s voice, referring
            to the other person as "the traveller."

            The NPC's current credibility baseline for this traveller is {{beliefBaseline:F2}}
            (0 = complete disbelief, 1 = complete trust).

            What {{npc.Name}} already knows — DO NOT repeat these facts:
            {{existingMemories}}

            ── WHAT TO EXTRACT ─────────────────────────────────────────────────────────
            Only store things the TRAVELLER revealed about themselves:
              • Facts about their identity, history, origin, profession, relationships
              • Their stated goals, plans, or reasons for being here
              • Deductions you can draw from their behaviour or word choice
              • Claims you're not sure are true
              • Things they said that were clearly a joke or bravado
              • Direct accusations they made against you

            ── WHAT TO IGNORE (respond with []) ─────────────────────────────────────────
            The NPC response is context only — do NOT extract from it.
            Respond with an empty array [] for any of these:
              • Greetings, pleasantries ("How are you?", "Nice to meet you")
              • Questions that reveal nothing about the traveller ("What do you do?",
                "How many letters in strawberry?", "What time is it?")
              • Observations about the world, the time, or the weather
              • The shape of the conversation ("the traveller asked me something",
                "the traveller seemed interested", "an appropriate response was expected")
              • Anything already in the existing memories list above

            ── EXAMPLES ────────────────────────────────────────────────────────────────
            Traveller: "How are you?" → []
            Traveller: "What do you do?" → []
            Traveller: "It's morning." → []
            Traveller: "Interesting." → []
            Traveller: "I came south from Carvallen last week." →
              [{"content": "The traveller came south from Carvallen recently.", "fidelity": 0.75, "nature": "fact"}]
            Traveller: "I used to be a soldier." →
              [{"content": "The traveller claims to have been a soldier.", "fidelity": 0.55, "nature": "claim"}]
            Traveller: "I can lift a horse." →
              [{"content": "The traveller claims they can lift a horse.", "fidelity": 0.20, "nature": "joke"}]

            ── FORMAT ──────────────────────────────────────────────────────────────────
            If nothing worth storing: respond with an empty array []
            Otherwise respond with a JSON array only — no preamble, no explanation:
            [
              { "content": "The traveller told me [fact]", "fidelity": 0.75, "nature": "fact" }
            ]
            Nature values: "fact" | "claim" | "joke" | "deduction" | "accusation"
            Fidelity bands:
              fact/accusation: 0.65–0.85 | claim/deduction: 0.35–0.65 | joke: 0.15–0.40
            Scale within the band using the credibility baseline ({{beliefBaseline:F2}}).
            Never use fidelity 0.0 — omit the memory entirely instead.
            """;

		var userPrompt =
			$"""
            Traveller said: "{playerMessage}"
            {npc.Name}'s response (context only — do not extract from this): "{npcResponse}"

            Does the traveller's message reveal anything meaningful about who they are?
            Respond with an empty array [] or a JSON array of memories only.
            """;

		var raw = await _llm.GenerateJsonAsync(systemPrompt, userPrompt);

		// Tolerates preamble, trailing prose and truncated arrays; an unparseable/empty/"NONE"
		// reply yields an empty list (no memories stored this turn).
		return LlmJson.ParseList(raw, e => ParseMemories(e, currentDay));
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static List<NpcMemory> ParseMemories(JsonElement root, int currentDay)
	{
		var memories = new List<NpcMemory>();

		var items = root.ValueKind == JsonValueKind.Array
			? root.EnumerateArray().ToList()
			: new List<JsonElement> { root };

		foreach (var item in items)
		{
			var content = item.GetProperty("content").GetString() ?? string.Empty;
			var fidelity = item.GetProperty("fidelity").GetSingle();
			var nature = item.TryGetProperty("nature", out var natureProp)
				? natureProp.GetString() ?? MemoryNature.Fact
				: MemoryNature.Fact;

			if (string.IsNullOrWhiteSpace(content)) continue;

			var clampedFidelity = Math.Clamp(fidelity, 0f, 1f);
			if (clampedFidelity < 0.1f) continue;

			memories.Add(new NpcMemory
			{
				Content = content,
				Fidelity = clampedFidelity,
				InitialFidelity = clampedFidelity,
				Nature = nature,
				DecayWeight = 1.0f,
				TraumaTagged = false,
				Timestamp = $"day-{currentDay}"
			});
		}

		return memories;
	}
}