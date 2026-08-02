using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.Interfaces.Core.NPC;

public interface INpcMemoryManager
{
	// ── Memory Addition ───────────────────────────────────────────────────────
	void AddMemory(string npcId, NpcMemory memory, bool isPlayerMemory = false);

	// ── Decay ─────────────────────────────────────────────────────────────────
	void DecayMemories(int currentDay);

	// ── Compression ───────────────────────────────────────────────────────────
	Task CompressMemoriesIfNeededAsync(
		string npcId,
		IScarTissueCompressor compressor,
		int currentDay,
		bool logMemory = true);

	// ── Session End ───────────────────────────────────────────────────────────
	// Returns the consolidated session memories so callers can propagate them.
	Task<List<NpcMemory>> EndSessionAsync(
		string npcId,
		IEpisodicMemoryCreator episodicCreator,
		IWorkingMemoryManager workingMemoryManager,
		IMemoryConsolidator consolidator,
		int currentDay,
		bool logMemory = true);

	// ── Suspect reclassification ──────────────────────────────────────────────
	void MoveToSuspect(string npcId, NpcMemory memory);
}