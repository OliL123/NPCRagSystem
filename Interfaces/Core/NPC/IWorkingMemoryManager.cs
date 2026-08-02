using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.Interfaces.Core.NPC;

public interface IWorkingMemoryManager
{
	// ── Authored ──────────────────────────────────────────────────────────────
	void AddAuthoredWorkingMemory(string npcId, string content, string flavourText = "", bool isSignificant = false);

	// ── Dynamic ───────────────────────────────────────────────────────────────
	void AddDynamicWorkingMemory(string npcId, string content, int currentDay);
	void TrackEntityMentions(string npcId, IEnumerable<string> entities, int currentDay);

	// ── Retrieval & Clearing ──────────────────────────────────────────────────
	List<WorkingMemory> GetWorkingMemory(string npcId);
	void ClearWorkingMemory(string npcId);
}