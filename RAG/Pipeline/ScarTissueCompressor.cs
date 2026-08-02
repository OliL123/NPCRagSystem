using NPCRAGSystem.Utils;
﻿using System.Text.Json;
using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.RAG.Pipeline;

public class ScarTissueCompressor : IScarTissueCompressor
{
	private readonly ILlmService _llm;

	public ScarTissueCompressor(ILlmService llm)
	{
		_llm = llm;
	}

	public async Task<NpcMemory?> CompressAsync(
		List<NpcMemory> memories,
		NpcState npc,
		int currentDay)
	{
		if (memories.Count == 0) return null;

		var memoryList = string.Join("\n", memories.Select(m =>
			$"- [{m.Fidelity:F2}] {m.Content}"));

		var systemPrompt =
			$$"""
            You are determining what {{npc.Name}} vaguely remembers about a traveller they met some time ago.
            The following memories have faded significantly over time. Compress them into a single
            hazy recollection that captures the general impression without specific details.

            Write in {{npc.Name}}'s voice. The result should feel like a half-remembered impression
            rather than a clear memory. Use vague language — "I think", "if I recall", "something about".

            Respond with a JSON object only, no markdown:
            {
              "content": "The compressed memory in first person",
              "fidelity": 0.3
            }

            Set fidelity between 0.2 and 0.4. More memories = slightly higher fidelity.
            Never above 0.4 since these are all faded memories.
            Respond with a JSON object only. No preamble, no explanation, no text before the JSON.
            
            """;

		var userPrompt =
			$"""
            Faded memories to compress:
            {memoryList}

            Write a single hazy recollection that captures the general impression.
            """;

		var raw = await _llm.GenerateJsonAsync(systemPrompt, userPrompt);
		var trimmed = LlmJson.Extract(raw);

		try
		{
			using var doc = JsonDocument.Parse(trimmed);
			var content = doc.RootElement.GetProperty("content").GetString() ?? string.Empty;
			var fidelity = doc.RootElement.GetProperty("fidelity").GetSingle();

			if (string.IsNullOrWhiteSpace(content)) return null;

			var clampedFidelity = Math.Clamp(fidelity, 0.1f, 0.4f);

			return new NpcMemory
			{
				Content = content,
				Fidelity = clampedFidelity,
				InitialFidelity = clampedFidelity,
				// Higher weight = higher stability in the Ebbinghaus curve, so the
				// hazy compressed impression lingers longer than a normal memory.
				// (Was 0.5f, which contradicted this intent — lower weight decays FASTER.)
				DecayWeight = 2.0f,
				TraumaTagged = false,
				Timestamp = $"day-{currentDay}"
			};
		}
		catch
		{
			return null;
		}
	}
}