using NPCRAGSystem.Domain;

namespace NPCRAGSystem.Interfaces.Retrieval;

public interface IMMRSelector
{
	List<DocumentChunk> Select(float[] queryEmbedding, List<DocumentChunk> candidates, int topK);
}
