using NPCRAGSystem.Interfaces.Retrieval;
using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.Domain;

namespace NPCRAGSystem.RAG.Retrieval;

public class Reranker : IReranker
{
	private readonly ICrossEncoder _crossEncoder;
	private readonly IBM25Index _bm25;

	// Caps concurrent Ollama requests — prevents overwhelming the server
	private readonly SemaphoreSlim _semaphore = new(4);

	// Minimum BM25 keyword overlap to pass Pass 1 filter
	private const double KeywordThreshold = 0.1;

	public Reranker(ICrossEncoder crossEncoder, IBM25Index bm25)
	{
		_crossEncoder = crossEncoder;
		_bm25 = bm25;
	}

	// ── Reranking ───────────────────────────────────────────────────────────

	public async Task<List<DocumentChunk>> RerankAsync(
		string query,
		List<DocumentChunk> chunks)
	{
		if (chunks.Count == 0) return chunks;

		// Pass 1: keyword relevance filter 
		// Eliminates chunks with no meaningful term overlap with the query
		var keywordScores = _bm25.Search(query, chunks.Count, scope: chunks);

		var survivors = keywordScores
			.Where(x => x.Score >= KeywordThreshold)
			.Select(x => x.Chunk)
			.ToList();

		if (survivors.Count == 0)
		{
			Console.WriteLine("[reranker] keyword filter eliminated all chunks, using originals");
			survivors = chunks;
		}

		// Pass 2: cross-encoder scoring (parallel) 
		// BGE reranker judges query-document relevance directly
		// All chunks scored simultaneously — total time = slowest single call
		var scoringTasks = survivors
			.Select(async chunk =>
			{
				await _semaphore.WaitAsync();
				try
				{
					var score = await _crossEncoder.ScoreAsync(query, chunk.ChunkContent);
					return (Chunk: chunk, Score: score);
				}
				finally
				{
					_semaphore.Release();
				}
			})
			.ToList();

		var scoredChunks = await Task.WhenAll(scoringTasks);

		return scoredChunks
			.OrderByDescending(x => x.Score)
			.Select(x => x.Chunk)
			.ToList();
	}
}