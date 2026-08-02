using NPCRAGSystem.Domain;

namespace NPCRAGSystem.Interfaces.Retrieval;

public interface IReranker
{
	Task<List<DocumentChunk>> RerankAsync(string query, List<DocumentChunk> chunks);
}