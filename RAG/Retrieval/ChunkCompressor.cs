using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Retrieval;
using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.Domain;

namespace NPCRAGSystem.RAG.Retrieval;

public class ChunkCompressor : IChunkCompressor
{
	private readonly IBM25Index _bm25;

	// Maximum tokens to allow in the full context block
	private const int MaxContextTokens = 1200;

	// Rough Token Estimate
	private const int CharsPerToken = 4;

	// Minimum sentences to keep per chunk
	private const int MinSentencesPerChunk = 2;

	public ChunkCompressor(IBM25Index bm25)
	{
		_bm25 = bm25;
	}

	public List<DocumentChunk> Compress(string query,List<DocumentChunk> chunks)
	{
		var totalChars = chunks.Sum(c => c.ChunkContent.Length);
		var maxChars = MaxContextTokens * CharsPerToken;
		
		// Check if chunk fits
		if (totalChars <= maxChars)
			return chunks;

		var compressed = new List<DocumentChunk>();

		foreach (var chunk in chunks)
		{
			var sentences = TextSplitter.SplitIntoSentences(chunk.ChunkContent);

			if (sentences.Count <= MinSentencesPerChunk)
			{
				compressed.Add(chunk);
				continue;
			}

			var avgSentenceLength = (int)sentences
				.Average(s => TextSplitter.SplitIntoWords(s).Count);

			// Score each sentence against query with BM25
			var scoredSentences = sentences
				.Select(s => (Sentence: s, Score: _bm25.ScoreText(s, query, avgSentenceLength)))
				.OrderByDescending(x => x.Score)
				.ToList();
			
			var targetLength = chunk.ChunkContent.Length / 2;
			var kept = new List<string>();
			var currentLength = 0;

			foreach (var (sentence, _) in scoredSentences)
			{
				if (currentLength >= targetLength && kept.Count >= MinSentencesPerChunk)
					break;

				kept.Add(sentence);
				currentLength += sentence.Length;
			}

			// Preserve original sentence order
			var orderedKept = sentences.Where(s => kept.Contains(s)).ToList();

			compressed.Add(new DocumentChunk
			{
				ID = chunk.ID,
				SourceTxtFile = chunk.SourceTxtFile,
				ChunkContent = string.Join(" ", orderedKept),
				ChunkIndex = chunk.ChunkIndex,
				Embedding = chunk.Embedding,
				Tags = chunk.Tags,
			});
		}

		return compressed;
	}
}
