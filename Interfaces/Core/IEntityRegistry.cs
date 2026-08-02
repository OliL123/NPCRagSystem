using NPCRAGSystem.RAG.Classification;

namespace NPCRAGSystem.Interfaces.Core;

public interface IEntityRegistry
{
	List<Topic> GetTopicsForText(string text);
	List<string> GetEntitiesForText(string text);
}