using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.Interfaces.Core.NPC;

public interface INpcRegistry
{
	// ── Retrieval ─────────────────────────────────────────────────────────────
	NpcState? GetNpc(string npcId);
	List<NpcState> GetAllNpcs();

	// Find an NPC by id or name (exact wins, then partial name) — for dev commands.
	NpcState? Resolve(string token);

	// ── State Updates ─────────────────────────────────────────────────────────
	void UpdateState(string npcId, string attribute, float value);

	// Restore an NPC to its authored baseline: drop all player-derived memories (orphan,
	// suspect, episodic) and reset mood to the snapshotted BaselineEmotionalState.
	void ResetToBaseline(string npcId);

	// ── Persistence ───────────────────────────────────────────────────────────
	Task MergeAsync(string path);
	Task SaveAsync();
}