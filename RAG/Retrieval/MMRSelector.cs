using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.Domain;
using NPCRAGSystem.Interfaces.Retrieval;

namespace NPCRAGSystem.RAG.Retrieval;

public class MMRSelector : IMMRSelector
{
	// Controls relevance vs diversity tradeoff
	// 1.0 = pure relevance, 0.0 = pure diversity
	// 0.6 recommended — mostly relevant, meaningfully diverse
	private readonly float _lambda;

	public MMRSelector (float lambda = 0.6f)
	{
		_lambda = lambda;
	}

	public List<DocumentChunk> Select(
		float[] queryEmbedding,
		List<DocumentChunk> candidates,
		int topNumChunk)
	{
		if (candidates.Count <= topNumChunk)
			return candidates;

		var selected = new List<DocumentChunk>();
		var remaining = new List<DocumentChunk>(candidates);

		// O(topK × candidates) iterations — acceptable at current scale.
		// Upgrade path: precompute full similarity matrix between all candidates
		// once before selection loop, eliminating redundant recalculations.
		// Worth implementing if candidates regularly exceed 50.
		while (selected.Count < topNumChunk && remaining.Count > 0)
		{
			DocumentChunk? best = null;
			var bestScore = float.MinValue;

			foreach (var candidate in remaining)
			{
				// Relevance to query
				var relevance = VectorMath.CosineSimilarity(queryEmbedding, candidate.Embedding);

				// Similarity to already selected chunks
				var maxSimilarityToSelected = selected.Count == 0
					? 0f
					: selected.Max(s => VectorMath.CosineSimilarity(candidate.Embedding, s.Embedding));

				// MMR score
				var mmrScore = _lambda * relevance - (1 - _lambda) * maxSimilarityToSelected;

				if (mmrScore > bestScore)
				{
					bestScore = mmrScore;
					best = candidate;
				}
			}

			if (best != null)
			{
				selected.Add(best);
				remaining.Remove(best);
			}
		}

		return selected; 
	}
}
