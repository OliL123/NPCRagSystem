using NPCRAGSystem.Domain;

namespace NPCRAGSystem.Interfaces.Retrieval;

public interface IChunkCompressor
{
	List<DocumentChunk> Compress(string query, List<DocumentChunk> chunks);
}