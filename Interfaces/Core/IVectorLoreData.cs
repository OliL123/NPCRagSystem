using NPCRAGSystem.Domain;
using NPCRAGSystem.RAG.Classification;

namespace NPCRAGSystem.Interfaces.Core;

public interface IVectorLoreData
{
	void Add(DocumentChunk chunk);
	List<(DocumentChunk Chunk, float Score)> Search(
		float[] queryEmbedding,
		int topNumChunk = 5,
		List<Topic>? topics = null);
	int Count { get; }

	// Similarity distribution over the candidate pool of the most recent Search. Lets a caller
	// judge a chunk against that query's own baseline instead of an absolute cosine cutoff, which
	// does not survive a change of corpus or embedding model.
	float LastScoreMean { get; }
	float LastScoreStdDev { get; }
} 