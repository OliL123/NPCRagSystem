using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.Interfaces.Core.NPC;

public interface IConversationTracker
{
	void AddConversationTurn(string npcId, string playerMessage, string npcResponse);
	List<ConversationTurn> GetConversationHistory(string npcId);
	void ClearConversationHistory(string npcId);
	void EvaluatePlayerBehaviour(string npcId, float[] queryEmbedding);
}