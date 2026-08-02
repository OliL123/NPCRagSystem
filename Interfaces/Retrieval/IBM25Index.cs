using NPCRAGSystem.Domain;
using NPCRAGSystem.RAG.Classification;

namespace NPCRAGSystem.Interfaces.Retrieval;

public interface IBM25Index
{
	void IndexChunk(DocumentChunk chunk);
	void Build();
	List<(DocumentChunk Chunk, double Score)> Search(
		string query,
		int topNumChunk = 5,
		List<DocumentChunk>? scope = null,
		List<Topic>? topics = null);
	double ScoreText(string text, string query, int? referenceLength = null);
}