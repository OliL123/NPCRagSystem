using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.Interfaces.Core.NPC;

public interface IMemoryConsolidator
{
    // Merges a set of session memories into a deduplicated, consolidated list.
    // Returns the replacement list — caller is responsible for swapping it in.
    Task<List<NpcMemory>> ConsolidateAsync(
        string npcName,
        List<NpcMemory> sessionMemories,
        List<NpcMemory> existingMemories);
}
