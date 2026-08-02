using NPCRAGSystem.Utils;
using System.Text.Json;
using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Retrieval;

namespace NPCRAGSystem.RAG.Retrieval;

// Pointwise LLM relevance judge. Replaces the old embedding-cosine "cross encoder",
// which could not work via Ollama — bge-reranker's classification head isn't exposed
// through the embeddings API, so query/document cosine was dominated by the query text
// appearing in both vectors. Here a small model scores each passage's relevance directly.
//
// Scoring is per-passage and parallelised by the caller (Reranker), so prefer a fast
// model. Disabled by default (SystemConfig.UseReranker) pending tuning.
public class LlmRerankerService : ICrossEncoder
{
	private readonly ILlmService _llm;

	public LlmRerankerService(ILlmService llm)
	{
		_llm = llm;
	}

	public async Task<float> ScoreAsync(string query, string document)
	{
		var system =
			"""
			You are a relevance judge for a retrieval system in a fantasy-world RPG.
			Rate how well the passage helps answer or relate to the query, from 0 to 10:
			  0  = entirely unrelated
			  5  = touches on the topic but doesn't answer it
			  10 = directly and fully relevant
			Judge only relevance, not writing quality. Output JSON only: {"score": <number>}
			""";

		var user =
			$"""
			Query: {query}

			Passage: {document}

			Score the passage's relevance to the query. Output JSON only.
			""";

		try
		{
			var raw = await _llm.GenerateJsonAsync(system, user, numKeep: 0);
			var trimmed = LlmJson.Extract(raw);
			if (string.IsNullOrEmpty(trimmed)) return 0f;

			using var doc = JsonDocument.Parse(trimmed);
			if (!doc.RootElement.TryGetProperty("score", out var scoreEl)) return 0f;

			var score = scoreEl.ValueKind == JsonValueKind.Number
				? scoreEl.GetSingle()
				: float.TryParse(scoreEl.GetString(), out var s) ? s : 0f;

			// Normalise to 0–1 — Reranker only needs a consistent ordering
			return Math.Clamp(score / 10f, 0f, 1f);
		}
		catch
		{
			// On failure, return neutral-low so the chunk falls back behind confident hits
			return 0f;
		}
	}
}
