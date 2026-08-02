using NPCRAGSystem.RAG.Classification;

namespace NPCRAGSystem.Interfaces.Classification;

public interface IComplexityClassifier
{
	// Pass a precomputed embedding of the query to avoid embedding it twice per turn
	// (once here, once for retrieval). When null, the classifier embeds it itself.
	Task<QueryComplexity> ClassifyAsync(string query, float[]? queryEmbedding = null);
}