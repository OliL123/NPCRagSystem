using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Domain.Npc;
using NPCRAGSystem.Utils;

namespace NPCRAGSystem.State.Managers;

public class NpcMemoryManager : INpcMemoryManager
{
	private readonly INpcRegistry _registry;

	// Tracks which orphan memories were created this session per NPC
	private readonly Dictionary<string, List<string>> _sessionOrphanIds = new();

	// Tracks which suspect memories were created this session per NPC (B11)
	private readonly Dictionary<string, List<string>> _sessionSuspectIds = new();

	// ── Constants ─────────────────────────────────────────────────────────────
	private const int MemoryOverwhelmCount = 10;
	private const int MemoryHardCap = 15;
	private const float SuspectFidelityFloor = 0.3f;
	private const float EpisodicAnchorBonus = 0.5f;

	public NpcMemoryManager(INpcRegistry registry)
	{
		_registry = registry;
	}

	// ── Memory Addition ───────────────────────────────────────────────────────

	public void AddMemory(string npcId, NpcMemory memory, bool isPlayerMemory = false)
	{
		var npc = _registry.GetNpc(npcId);
		if (npc == null) return;

		if (memory.InitialFidelity <= 0f)
			memory.InitialFidelity = memory.Fidelity;

		if (!isPlayerMemory)
		{
			var normalisedNew = StringUtils.NormaliseForComparison(memory.Content);
			if (npc.WorldMemories.Any(m =>
				StringUtils.NormaliseForComparison(m.Content)
					.Equals(normalisedNew, StringComparison.OrdinalIgnoreCase))) return;

			npc.WorldMemories.Add(memory);
			return;
		}

		if (string.IsNullOrEmpty(memory.Id))
			memory.Id = Guid.NewGuid().ToString("N")[..8];

		// Route to suspect memories if below fidelity floor
		if (memory.Fidelity < SuspectFidelityFloor)
		{
			var normSuspect = StringUtils.NormaliseForComparison(memory.Content);
			if (!npc.SuspectMemories.Any(m =>
				StringUtils.NormaliseForComparison(m.Content)
					.Equals(normSuspect, StringComparison.OrdinalIgnoreCase)))
			{
				npc.SuspectMemories.Add(memory);

				// B11 — track session suspects for episodic inclusion
				if (!_sessionSuspectIds.TryGetValue(npcId, out var suspectIds))
				{
					suspectIds = new List<string>();
					_sessionSuspectIds[npcId] = suspectIds;
				}
				suspectIds.Add(memory.Id);
			}
			return;
		}

		// Duplicate check
		var normalisedContent = StringUtils.NormaliseForComparison(memory.Content);
		if (npc.OrphanMemories.Any(m =>
			StringUtils.NormaliseForComparison(m.Content)
				.Equals(normalisedContent, StringComparison.OrdinalIgnoreCase))) return;

		// Enforce hard memory cap
		if (npc.OrphanMemories.Count >= MemoryHardCap)
		{
			var toDrop = npc.OrphanMemories
				.Where(m => !m.TraumaTagged)
				.OrderBy(m => m.Fidelity)
				.FirstOrDefault();
			if (toDrop != null) npc.OrphanMemories.Remove(toDrop);
		}

		npc.OrphanMemories.Add(memory);

		// Track this memory as created this session
		if (!_sessionOrphanIds.TryGetValue(npcId, out var sessionIds))
		{
			sessionIds = new List<string>();
			_sessionOrphanIds[npcId] = sessionIds;
		}
		sessionIds.Add(memory.Id);

		// Overwhelm — anxiety bump when memory list gets crowded
		// TODO: inject IWorkingMemoryManager to add overwhelm working memory note here
		if (npc.OrphanMemories.Count == MemoryOverwhelmCount)
		{
			_registry.UpdateState(npcId, "anxiety",
				Math.Min(1f, npc.EmotionalState.Anxiety + 0.1f));
		}
	}

	// ── Decay ─────────────────────────────────────────────────────────────────

	public void DecayMemories(int currentDay)
	{
		foreach (var npc in _registry.GetAllNpcs())
		{
			var episodeAnchorFidelities = npc.EpisodicMemories
				.Where(e => !e.TraumaTagged)
				.SelectMany(e => e.LinkedMemoryIds.Select(id => (id, e.Fidelity)))
				.GroupBy(x => x.id)
				.ToDictionary(g => g.Key, g => g.Max(x => x.Fidelity));

			DecayMemoryList(npc.WorldMemories, currentDay, episodeAnchorFidelities);
			DecayMemoryList(npc.OrphanMemories, currentDay, episodeAnchorFidelities);
			DecayMemoryList(npc.SuspectMemories, currentDay, episodeAnchorFidelities);
			DecayEpisodicList(npc.EpisodicMemories, currentDay);
		}
	}

	private static void DecayMemoryList(
		List<NpcMemory> memories,
		int currentDay,
		Dictionary<string, float> episodeAnchorFidelities)
	{
		foreach (var memory in memories)
		{
			if (memory.TraumaTagged) continue;
			// decay_weight <= 0 means "permanent / never decays" (authored facts use this).
			// Also guards the division below: stability of 0 would zero fidelity outright.
			if (memory.DecayWeight <= 0f) continue;
			if (!int.TryParse(memory.Timestamp.Replace("day-", ""), out var createdDay)) continue;

			var daysElapsed = currentDay - createdDay;
			if (daysElapsed <= 0) continue;

			var anchorBonus = episodeAnchorFidelities.TryGetValue(memory.Id, out var anchorFidelity)
				&& anchorFidelity > 0.5f
				? 1f + (anchorFidelity * EpisodicAnchorBonus)
				: 1f;

			// Absolute Ebbinghaus decay — anchored on InitialFidelity and total elapsed
			// days, so the result is independent of how often this method is called.
			// (Compounding against current Fidelity made decay rate depend on advance
			// granularity: five ×1-day passes ≠ one ×5-day pass.)
			var stability = memory.DecayWeight * 10.0 * anchorBonus;
			var retention = Math.Exp(-daysElapsed / stability);
			memory.Fidelity = (float)(memory.InitialFidelity * retention);
		}
	}

	private static void DecayEpisodicList(List<EpisodicMemory> episodes, int currentDay)
	{
		foreach (var episode in episodes)
		{
			if (episode.TraumaTagged) continue;
			if (episode.DecayWeight <= 0f) continue; // permanent / never decays (also guards /0)
			if (!int.TryParse(episode.Timestamp.Replace("day-", ""), out var createdDay)) continue;

			var daysElapsed = currentDay - createdDay;
			if (daysElapsed <= 0) continue;

			var stability = episode.DecayWeight * 15.0;
			var retention = Math.Exp(-daysElapsed / stability);
			episode.Fidelity = (float)(episode.InitialFidelity * retention);
		}
	}

	// ── Compression ───────────────────────────────────────────────────────────

	public async Task CompressMemoriesIfNeededAsync(
		string npcId,
		IScarTissueCompressor compressor,
		int currentDay,
		bool logMemory = true)
	{
		var npc = _registry.GetNpc(npcId);
		if (npc == null) return;

		var fadedMemories = npc.OrphanMemories
			.Where(m => m.Fidelity < 0.3f && !m.TraumaTagged)
			.ToList();

		if (fadedMemories.Count < 5) return;

		if (logMemory)
		{
			ConsoleEx.Dim($"[memory] compressing {fadedMemories.Count} faded memories for {npc.Name}");
		}

		var compressed = await compressor.CompressAsync(fadedMemories, npc, currentDay);
		if (compressed == null) return;

		foreach (var m in fadedMemories)
			npc.OrphanMemories.Remove(m);

		npc.OrphanMemories.Add(compressed);

		if (logMemory)
		{
			ConsoleEx.Dim($"[memory] compressed: \"{compressed.Content}\" (fidelity: {compressed.Fidelity:F2})");
		}
	}

	// ── Session End ───────────────────────────────────────────────────────────

	public async Task<List<NpcMemory>> EndSessionAsync(
		string npcId,
		IEpisodicMemoryCreator episodicCreator,
		IWorkingMemoryManager workingMemoryManager,
		IMemoryConsolidator consolidator,
		int currentDay,
		bool logMemory = true)
	{
		var npc = _registry.GetNpc(npcId);
		if (npc == null || episodicCreator == null) return new List<NpcMemory>();

		// Get working memory from WorkingMemoryManager
		var workingMemory = workingMemoryManager.GetWorkingMemory(npcId);

		var hasSignificant = workingMemory.Any(m => m.IsSignificant);
		var emotionalSpike = npc.EmotionalState.Fear > 0.7f
								|| npc.EmotionalState.Anger > 0.7f
								|| npc.EmotionalState.Anxiety > 0.7f;
		_sessionOrphanIds.TryGetValue(npcId, out var sessionIds);
		_sessionSuspectIds.TryGetValue(npcId, out var sessionSuspectIds);

		// B11 — suspects count toward the episodic trigger
		var manyMemoriesCreated = (sessionIds?.Count ?? 0) >= 3
								|| (sessionSuspectIds?.Count ?? 0) >= 2;

		if (!hasSignificant && !emotionalSpike && !manyMemoriesCreated)
		{
			_sessionOrphanIds.Remove(npcId);
			_sessionSuspectIds.Remove(npcId);
			return new List<NpcMemory>();
		}

		var sessionMemories = sessionIds != null
			? npc.OrphanMemories.Where(m => sessionIds.Contains(m.Id)).ToList()
			: new List<NpcMemory>();

		// Capture originals before consolidation for reinforcement diffing
		var preConsolidationMemories = sessionMemories.ToList();

		// ── Consolidation ─────────────────────────────────────────────────────
		// Merge duplicate/overlapping session memories before creating an episode
		List<NpcMemory> consolidated = sessionMemories;
		if (consolidator != null && sessionMemories.Count > 1)
		{
			var existingMemories = npc.OrphanMemories
				.Where(m => !sessionIds!.Contains(m.Id))
				.ToList();

			consolidated = await consolidator.ConsolidateAsync(
				npc.Name, sessionMemories, existingMemories);

			// Swap out session memories for their consolidated versions
			foreach (var old in sessionMemories)
				npc.OrphanMemories.Remove(old);

			foreach (var m in consolidated)
			{
				if (m.InitialFidelity <= 0f) m.InitialFidelity = m.Fidelity;
				if (string.IsNullOrEmpty(m.Id)) m.Id = Guid.NewGuid().ToString("N")[..8];
				npc.OrphanMemories.Add(m);
			}

			if (logMemory && consolidated.Count != sessionMemories.Count)
			{
				ConsoleEx.Dim($"[memory] consolidated {sessionMemories.Count} → {consolidated.Count} memories for {npc.Name}");
			}

			sessionMemories = consolidated;
		}

		// ── Memory reinforcement ──────────────────────────────────────────────
		// Session memories dropped by consolidation boost matching existing orphans —
		// re-encountering the same fact makes it stickier.
		var consolidatedIds = consolidated.Select(m => m.Id).ToHashSet();
		var droppedMemories = preConsolidationMemories
			.Where(m => !consolidatedIds.Contains(m.Id))
			.ToList();

		if (droppedMemories.Count > 0)
		{
			var existingForReinforcement = npc.OrphanMemories
				.Where(m => !consolidatedIds.Contains(m.Id))
				.ToList();

			foreach (var dropped in droppedMemories)
			{
				var best = existingForReinforcement
					.Select(m => (memory: m, score: StringUtils.JaccardSimilarity(m.Content, dropped.Content)))
					.Where(x => x.score > 0.35f)
					.OrderByDescending(x => x.score)
					.FirstOrDefault();

				if (best.memory != null)
				{
					best.memory.Fidelity = Math.Min(best.memory.Fidelity + dropped.Fidelity * 0.25f, 0.95f);
					best.memory.InitialFidelity = best.memory.Fidelity;

					if (logMemory)
					{
						ConsoleEx.Dim($"[memory] reinforced: \"{best.memory.Content[..Math.Min(50, best.memory.Content.Length)]}\" → {best.memory.Fidelity:F2}");
					}
				}
			}
		}

		// B11 — collect session suspect memories for sceptical framing in episodic
		var sessionSuspects = sessionSuspectIds != null
			? npc.SuspectMemories.Where(m => sessionSuspectIds.Contains(m.Id)).ToList()
			: new List<NpcMemory>();

		var snapshot = new Dictionary<string, float>();
		void TrySnapshot(string key, float value)
		{
			if (Math.Abs(value) > 0.1f) snapshot[key] = value;
		}

		TrySnapshot("fear", npc.EmotionalState.Fear);
		TrySnapshot("suspicion", npc.EmotionalState.Suspicion);
		TrySnapshot("anxiety", npc.EmotionalState.Anxiety);
		TrySnapshot("trust_player", npc.PlayerRelationship.TrustPlayer);
		TrySnapshot("care_player", npc.PlayerRelationship.CarePlayer);

		var episode = await episodicCreator.CreateAsync(
			workingMemory, sessionMemories, npc, snapshot, currentDay, sessionSuspects);

		if (episode != null)
		{
			foreach (var m in sessionMemories)
			{
				m.EpisodeId = episode.Id;
				episode.LinkedMemoryIds.Add(m.Id);
			}

			if (episode.InitialFidelity <= 0f)
				episode.InitialFidelity = episode.Fidelity;

			npc.EpisodicMemories.Add(episode);

			if (logMemory)
			{
				ConsoleEx.Dim($"[memory] episode created: \"{episode.Content[..Math.Min(60, episode.Content.Length)]}...\"");
			}
		}

		_sessionOrphanIds.Remove(npcId);
		_sessionSuspectIds.Remove(npcId);

		return sessionMemories;
	}

	// ── Suspect reclassification ──────────────────────────────────────────────

	public void MoveToSuspect(string npcId, NpcMemory memory)
	{
		var npc = _registry.GetNpc(npcId);
		if (npc == null) return;

		npc.OrphanMemories.Remove(memory);
		npc.WorldMemories.Remove(memory);

		if (!npc.SuspectMemories.Any(m => m.Id == memory.Id))
		{
			npc.SuspectMemories.Add(memory);

			if (!_sessionSuspectIds.TryGetValue(npcId, out var suspectIds))
			{
				suspectIds = new List<string>();
				_sessionSuspectIds[npcId] = suspectIds;
			}
			if (!suspectIds.Contains(memory.Id))
				suspectIds.Add(memory.Id);
		}
	}
}