using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.Interfaces.Core.NPC;

public interface IScarTissueCompressor
{
	Task<NpcMemory?> CompressAsync(List<NpcMemory> memories, NpcState npc, int currentDay);
}