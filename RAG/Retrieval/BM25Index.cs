using NPCRAGSystem.Interfaces.Retrieval;
using NPCRAGSystem.Domain;
using NPCRAGSystem.RAG.Classification;

namespace NPCRAGSystem.RAG.Retrieval;

public class BM25Index : IBM25Index
{
	private readonly List<DocumentChunk> _chunks = new();
	private readonly List<int> _chunkLengths = new();
	private readonly Dictionary<string, double> _idf = new();
	private readonly List<Dictionary<string, int>> _termFrequencies = new();
	private readonly Dictionary<string, int> _chunkIndexById = new();
	private double _averageChunkLength = 0;

	// Tuning Parameters, K1: Term Frequency Saturation, B: Length Norm. Strength
	private const double K1 = 1.5;
	private const double B = 0.75;

	// Guard against calling Build() more than once
	private bool _built = false;

	// Common words excluded from indexing — carry no search signal.
	private static readonly HashSet<string> Stopwords = new()
	{
		// Articles and short prepositions
		"the", "and", "but", "for", "not", "this", "that",
		"with", "from", "have", "been", "they", "were", "are",
		"his", "her", "him", "its", "our", "your", "their",
		"was", "has", "had", "did", "does", "will", "would",
		"could", "should", "may", "might", "shall", "can",
		"about", "which", "there", "here", "when", "where",
		"who", "what", "how", "all", "any", "some", "such",
		"into", "onto", "upon", "also", "than", "then", "just",
		"yes", 

		// Two letter words to explicitly exclude
		"at", "by", "in", "on", "to", "of", "or", "an",
		"as", "is", "it", "be", "do", "if", "up", "so",
		"we", "he", "me", "my", "no", "us", "am"
	};

	// ── Indexing ──────────────────────────────────────────────────────

	// Tokenise chunk content once at ingestion, store term frequencies and length
	public void IndexChunk(DocumentChunk chunk)
	{
		_chunkIndexById[chunk.ID] = _chunks.Count;
		_chunks.Add(chunk);
		var terms = Tokenise(chunk.ChunkContent);
		var tf = new Dictionary<string, int>();

		foreach (var term in terms)
			tf[term] = tf.GetValueOrDefault(term, 0) + 1;

		_termFrequencies.Add(tf);
		_chunkLengths.Add(terms.Count);
	}

	// Call once after all chunks are added — computes corpus-wide IDF scores
	public void Build()
	{
		if (_built) throw new InvalidOperationException("BM25 index already built. Create a new instance to reindex.");
		_built = true;

		if (_chunks.Count == 0)
		{
			Console.WriteLine("  Warning: BM25 index built with no chunks.");
			return;
		}

		_averageChunkLength = _chunkLengths.Average();
		if (_averageChunkLength == 0) _averageChunkLength = 1;

		// IDF: rare terms across corpus score higher than common ones
		_idf.Clear();
		var totalChunks = _chunks.Count;
		var allTerms = _termFrequencies.SelectMany(tf => tf.Keys).Distinct();

		foreach (var term in allTerms)
		{
			var chunksWithTerm = _termFrequencies.Count(tf => tf.ContainsKey(term));
			_idf[term] = Math.Log((totalChunks - chunksWithTerm + 0.5) /
								  (chunksWithTerm + 0.5) + 1);
		}

		Console.WriteLine($"  BM25 index built. {_chunks.Count} chunks, " +
						  $"{_idf.Count} unique terms.");
	}

	// ── Scoring ───────────────────────────────────────────────────────

	// Score all chunks against query, return top K by BM25 score
	public List<(DocumentChunk Chunk, double Score)> Search(
		string query, 
		int topNumChunk = 5,
		List<DocumentChunk>? scope = null,
		List<Topic>? topics = null)

	{
		if (!_built) throw new InvalidOperationException("Call Build() before searching.");

		var queryTerms = Tokenise(query);
		var chunksToSearch = scope ?? GetTopicFilteredChunks(topics, topNumChunk);
		var scores = new List<(DocumentChunk, double)>();

		for (int i = 0; i < chunksToSearch.Count; i++)
		{
			// Resolve the chunk's position in the master index via O(1) ID lookup.
			// chunksToSearch may be a caller scope or a topic-filtered subset, so its
			// local index must never be used against _termFrequencies/_chunkLengths.
			if (!_chunkIndexById.TryGetValue(chunksToSearch[i].ID, out var realIndex))
			{
				scores.Add((chunksToSearch[i], 0));
				continue;
			}

			var score = ScoreTerms(_termFrequencies[realIndex], _chunkLengths[realIndex], queryTerms, _averageChunkLength);
			scores.Add((chunksToSearch[i], score));
		}

		return scores
			.Where(x => x.Item2 > 0)
			.OrderByDescending(x => x.Item2)
			.Take(topNumChunk)
			.ToList();
	}
	public double ScoreText(string text, string query, int? referenceLength = null)
	{
		if (!_built) throw new InvalidOperationException("Call Build() before searching.");

		var queryTerms = Tokenise(query);
		var textTerms = Tokenise(text);
		var tf = new Dictionary<string, int>();

		foreach (var term in textTerms)
			tf[term] = tf.GetValueOrDefault(term, 0) + 1;

		var docLengthReference = referenceLength.HasValue
			? (double)referenceLength.Value
			: _averageChunkLength;

		return ScoreTerms(tf, textTerms.Count, queryTerms, docLengthReference);
	}

	private double ScoreTerms(
		Dictionary<string, int> tf, 
		int textLength, 
		List<string> queryTerms,
		double  referenceLength)
	{
		var score = 0.0;

		foreach (var term in queryTerms)
		{
			if (!_idf.TryGetValue(term, out var idf)) continue;

			var termFreq = tf.GetValueOrDefault(term, 0);
			var normalisedTf = (termFreq * (K1 + 1)) /
							   (termFreq + K1 * (1 - B + B * textLength / referenceLength));

			score += idf * normalisedTf;
		}

		return score;
	}

	// ── Helpers ───────────────────────────────────────────────────────

	private List<DocumentChunk> GetTopicFilteredChunks(List<Topic>? topics, int topNumChunk)
	{
		if (topics == null)
			return _chunks;

		var filtered = _chunks
			.Where(c => c.Tags.Any(t => topics.Contains(t)))
			.ToList();

		// Soft fallback — if filtered pool is too small, use full corpus
		return filtered.Count >= topNumChunk ? filtered : _chunks;
	}

	// Split text into cleaned lowercase tokens, preserving short proper nouns
	private static List<string> Tokenise(string text)
	{
		var words = text
			.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':', '"', '\'' },
				StringSplitOptions.RemoveEmptyEntries);

		return words
			.Where(w => w.Length > 2 || (w.Length > 1 && char.IsUpper(w[0])))
			.Where(w => !Stopwords.Contains(w.ToLowerInvariant()))
			.Select(w => w.ToLowerInvariant())	
			.ToList();
	}
}
