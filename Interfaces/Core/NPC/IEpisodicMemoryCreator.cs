using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.Interfaces.Core.NPC;

public interface IEpisodicMemoryCreator
{
    Task<EpisodicMemory?> CreateAsync(
        List<WorkingMemory> workingMemory,
        List<NpcMemory> sessionMemories,
        NpcState npc,
        Dictionary<string, float> emotionalSnapshot,
        int currentDay,
        List<NpcMemory>? suspectMemories = null);
}
