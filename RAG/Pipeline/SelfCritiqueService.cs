using NPCRAGSystem.Utils;
﻿using System.Text.Json;
using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Core.NPC;

namespace NPCRAGSystem.RAG.Pipeline;

public class SelfCritiqueService : ISelfCritiqueService
{
	private readonly ILlmService _llm;

	public SelfCritiqueService(ILlmService llm)
	{
		_llm = llm;
	}

	public async Task<CritiqueResult> CritiqueAsync(
		string npcName,
		string dynamicPersona,
		string response,
		string contextBlock)
	{
		var systemPrompt =
			$"""
            You are a strict quality reviewer for NPC dialogue in a text-based RPG.
            Your job is to evaluate whether an NPC response meets the two criteria below.
            Always provide a non-empty reason explaining which criteria passed or failed.
            Respond with a JSON object only.
            """;

		const string responseFormat =
			"""
			{
			  "passed": true or false,
			  "reason": "brief explanation of pass or fail"
			}
			""";

		var userPrompt =
			$"""
			NPC Name: {npcName}
			NPC Persona: {dynamicPersona}

			Retrieved context the NPC has access to:
			{contextBlock}

			NPC Response to evaluate:
			{response}

			Evaluate against these two criteria:
			1. IN CHARACTER — Does the response match the NPC's persona, voice, and emotional state?
			   Fail if the NPC sounds generic, breaks character, or ignores their emotional state.
			2. NO HALLUCINATION — Does the response only use facts present in the context or persona?
			   Fail if the NPC invents names, events, dates, or facts not in the context.

			Respond with:
			{responseFormat}

			If both criteria pass, set passed to true.
			If either criterion fails, set passed to false and explain which one failed.
			""";

		var raw = await _llm.GenerateJsonAsync(systemPrompt, userPrompt);
		var trimmed = LlmJson.Extract(raw);

		try
		{
			using var doc = JsonDocument.Parse(trimmed);
			var passed = doc.RootElement.GetProperty("passed").GetBoolean();
			var reason = doc.RootElement.GetProperty("reason").GetString() ?? string.Empty;

			return new CritiqueResult { Passed = passed, Reason = reason };
		}
		catch
		{
			return new CritiqueResult { Passed = true, Reason = "Critique parse failed — passing through" };
		}
	}
}