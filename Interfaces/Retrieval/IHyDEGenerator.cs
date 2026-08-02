namespace NPCRAGSystem.Interfaces.Retrieval;

public interface IHyDEGenerator
{
	Task<float[]> GenerateHypotheticalEmbeddingAsync(string query);
}
