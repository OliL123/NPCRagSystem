using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Domain;
using NPCRAGSystem.RAG.Classification;


namespace NPCRAGSystem.RAG.Retrieval;

public class InMemoryLoreData : IVectorLoreData
{
	private readonly List<DocumentChunk> _chunks = new();

	public int Count => _chunks.Count;

	public void Add(DocumentChunk chunk)
	{
		if (chunk.Embedding.Length == 0)
			throw new ArgumentException($"Chunk {chunk.ID} has no embedding.");
		_chunks.Add(chunk);
	}	

	// Mean and population standard deviation of the query's similarity to every candidate it was
	// scored against, set on each Search. The pipeline floors on this rather than on an absolute
	// cosine: nomic-embed-text compresses similarities into a narrow, corpus-specific band (~0.5–0.7
	// here), so any fixed cutoff is a magic number fitted to one corpus and one embedding model.
	// "How far above this query's own baseline" travels; "0.62" does not.
	public float LastScoreMean { get; private set; }
	public float LastScoreStdDev { get; private set; }

	public List<(DocumentChunk Chunk, float Score)> Search(
		float[] queryEmbedding,
		int topNumChunk = 5,
		List<Topic>? topics = null)
	{
		// Filter candidates to chunks with at least one matching topic tag.
		// If no topics provided or filtered pool is too small, fall back to full corpus.
		var candidates = topics != null
			? _chunks.Where(c => c.Tags.Any(t => topics.Contains(t))).ToList()
			: _chunks;

		// Soft fallback — if filter returns fewer than topNumChunk candidates,
		// not enough context to work with, so search the full corpus instead.
		if (candidates.Count < topNumChunk)
			candidates = _chunks;

		var scored = candidates
			.Select(c => (Chunk: c, Score: VectorMath.CosineSimilarity(queryEmbedding, c.Embedding)))
			.ToList();

		// Capture the distribution over the whole candidate pool before trimming to the top k — the
		// scoring pass above already visited every candidate, so this costs one more walk and gives
		// the pipeline a per-query, per-corpus baseline to floor against.
		if (scored.Count > 0)
		{
			var mean = scored.Average(x => x.Score);
			var variance = scored.Average(x => (x.Score - mean) * (x.Score - mean));
			LastScoreMean = mean;
			LastScoreStdDev = MathF.Sqrt(variance);
		}
		else
		{
			LastScoreMean = 0f;
			LastScoreStdDev = 0f;
		}

		return scored
			.OrderByDescending(x => x.Score)
			.Take(topNumChunk)
			.ToList();
	}
}
