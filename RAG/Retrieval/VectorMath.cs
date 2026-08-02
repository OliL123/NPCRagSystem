namespace NPCRAGSystem.RAG.Retrieval;

public static class VectorMath
{
	public static float CosineSimilarity(float[] a, float[] b)
	{
		if (a.Length != b.Length)
			throw new ArgumentException("Embedding dimensions do not match");

		float dot = 0f, normA = 0f, normB = 0f;

		// Upgrade path: System.Numerics.Vector<float> SIMD operations would
		// give ~4-8x speedup on large corpora. Worth implementing if chunk
		// count exceeds ~50,000 or if search latency becomes a bottleneck.
		for (int i = 0; i < a.Length; i++)
		{
			dot += a[i] * b[i];
			normA += a[i] * a[i];
			normB += b[i] * b[i];
		}

		// 1e-8f there to prevent division by zero
		return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB) + 1e-8f);
	}
}