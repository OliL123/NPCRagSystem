using NPCRAGSystem.RAG.Classification;

namespace NPCRAGSystem.Interfaces.Classification;

public interface ITopicClassifier
{
	List<Topic> Classify(string query);
}