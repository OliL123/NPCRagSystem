namespace NPCRAGSystem.Interfaces.Retrieval;

public interface ICrossEncoder
{
	Task<float> ScoreAsync(string query, string document);
}