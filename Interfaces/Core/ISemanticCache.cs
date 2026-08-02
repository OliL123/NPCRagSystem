namespace NPCRAGSystem.Interfaces.Core;

public interface ISemanticCache
{
	void Store(string npcId, float[] queryEmbedding, string response);
	string? TryGet(string npcId, float[] queryEmbedding);
	int Count { get; }
}