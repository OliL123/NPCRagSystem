namespace NPCRAGSystem.Interfaces.Core;

public interface IEmbeddingService
{
	// nomic-embed-text is asymmetric: passages to be retrieved are embedded with a
	// "search_document" task prefix, and search queries with "search_query". Pass
	// isDocument: true when embedding lore/passages, false (default) for queries.
	Task<float[]> GetEmbeddingAsync(string text, bool isDocument = false);
}
