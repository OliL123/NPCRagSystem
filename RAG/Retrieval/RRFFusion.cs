using NPCRAGSystem.Domain;

namespace NPCRAGSystem.RAG.Retrieval;

public static class RRFFusion
{
	// Standard RRF constant — softens penalty for lower ranks
	// Higher value = more forgiving to lower ranked results
	private const int K = 60;

	public static List<DocumentChunk> Fuse(
		List<(DocumentChunk Chunk, float Score)> vectorResults,
		List<(DocumentChunk Chunk, double Score)> bm25Results,
		int topNumChunk = 5)
	{
		var scores = new Dictionary<string, (DocumentChunk Chunk, double RrfScore)>();

		// Score vector results by rank
		for (int rank = 0; rank < vectorResults.Count; rank++)
		{
			var chunk = vectorResults[rank].Chunk;
			var rrfScore = 1.0 / (rank + 1 + K);

			scores[chunk.ID] = (chunk, rrfScore);
		}

		// Score BM25 results by rank
		for (int rank = 0; rank < bm25Results.Count; rank++)
		{
			var chunk = bm25Results[rank].Chunk;
			var rrfScore = 1.0 / (rank + 1 + K);

			if (scores.TryGetValue(chunk.ID, out var existing))
				scores[chunk.ID] = (chunk, existing.RrfScore + rrfScore);
			else
				scores[chunk.ID] = (chunk, rrfScore);
		}

		return scores.Values
			.OrderByDescending(x => x.RrfScore)
			.Take(topNumChunk)
			.Select(x => x.Chunk)
			.ToList();
	}
}
