using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.Interfaces.Core.NPC;

public interface IConversationMemoryCreator
{
	Task<List<NpcMemory>> TryCreateMemoriesAsync(
		string playerMessage,
		string npcResponse,
		NpcState npc,
		float beliefBaseline,
		int currentDay);
}