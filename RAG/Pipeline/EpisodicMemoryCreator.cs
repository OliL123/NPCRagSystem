using NPCRAGSystem.Utils;
﻿using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Domain.Npc;
using System.Text.Json;

namespace NPCRAGSystem.RAG.Pipeline;

public class EpisodicMemoryCreator : IEpisodicMemoryCreator
{
	private readonly ILlmService _llm;

	public EpisodicMemoryCreator(ILlmService llm)
	{
		_llm = llm;
	}

	public async Task<EpisodicMemory?> CreateAsync(
		List<WorkingMemory> workingMemory,
		List<NpcMemory> sessionMemories,
		NpcState npc,
		Dictionary<string, float> emotionalSnapshot,
		int currentDay,
		List<NpcMemory>? suspectMemories = null)
	{
		if (workingMemory.Count == 0 && sessionMemories.Count == 0)
			return null;

		var workingMemoryText = workingMemory.Count > 0
			? string.Join("\n", workingMemory.Select(m =>
				$"- {(m.IsSignificant ? "[significant] " : "")}{m.Content}"))
			: "Nothing notable in the environment.";

		var sessionMemoryText = sessionMemories.Count > 0
			? string.Join("\n", sessionMemories.Select(m =>
				$"- [{m.Fidelity:F1}] {m.Content}"))
			: "Nothing was learned about the traveller this session.";

		var suspectMemoryText = suspectMemories?.Count > 0
			? "\n\nThings this traveller said that you found hard to believe or suspect:\n" +
			  string.Join("\n", suspectMemories.Select(m => $"- [{m.Fidelity:F1}] {m.Content}"))
			: "";

		var snapshotText = emotionalSnapshot.Count > 0
			? string.Join(", ", emotionalSnapshot.Select(kvp =>
				$"{kvp.Key}: {(kvp.Value > 0 ? "+" : "")}{kvp.Value:F2}"))
			: "No significant emotional shift.";

		var systemPrompt =
			$$"""
            You are forming a single episodic memory for {{npc.Name}} about an encounter
            with a traveller that just ended.

            Write in {{npc.Name}}'s voice — first person, past tense.
            The memory should feel like a genuine human recollection — specific enough
            to be meaningful, vague enough to feel remembered rather than recorded.
            Weave together the environment, what was learned, and how it felt.

            Respond with a JSON object only:
            {
              "content": "The episodic memory in first person",
              "fidelity": 0.8,
              "is_significant": true
            }

            Set fidelity between 0.6 and 1.0 based on how memorable the encounter was.
            Set is_significant to true if anything notable happened, false otherwise.
            Respond with a JSON object only. No preamble, no explanation, no text before the JSON.
            
            """;

		var userPrompt =
			$"""
            Environment during the encounter:
            {workingMemoryText}

            What was learned about the traveller:
            {sessionMemoryText}{suspectMemoryText}

            Emotional shifts during the encounter (delta from baseline):
            {snapshotText}

            Write a single episodic memory capturing this encounter.
            """;

		var raw = await _llm.GenerateJsonAsync(systemPrompt, userPrompt);
		var trimmed = LlmJson.Extract(raw);

		try
		{
			using var doc = JsonDocument.Parse(trimmed);
			var content = doc.RootElement.GetProperty("content").GetString() ?? string.Empty;
			var fidelity = doc.RootElement.GetProperty("fidelity").GetSingle();
			var isSignificant = doc.RootElement.GetProperty("is_significant").GetBoolean();

			if (string.IsNullOrWhiteSpace(content)) return null;

			var clampedFidelity = Math.Clamp(fidelity, 0.6f, 1.0f);

			return new EpisodicMemory
			{
				Content = content,
				Fidelity = clampedFidelity,
				InitialFidelity = clampedFidelity,
				DecayWeight = 1.5f,
				TraumaTagged = false,
				Timestamp = $"day-{currentDay}",
				IsSignificant = isSignificant,
				EmotionalSnapshot = emotionalSnapshot
			};
		}
		catch
		{
			return null;
		}
	}
}