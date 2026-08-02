using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.Interfaces.Core;

namespace NPCRAGSystem.RAG.Experimental;

// NOT CURRENTLY WIRED INTO THE PIPELINE — parked for Phase 4.
// Removed because cached responses are frozen snapshots: they ignore the NPC's
// current emotional state, new memories, and conversation history. Re-enabling
// requires state-aware keying (npcId + coarse state fingerprint) and per-NPC
// invalidation on memory writes.
//
// Cache is keyed by NPC — same query produces different responses per character
public class SemanticCache : ISemanticCache
{
	private readonly Dictionary<string, List<CacheEntry>> _entriesByNpc = new();
	private readonly float _similarityThreshold;
	private readonly int _maxEntries;

	public SemanticCache(float similarityThreshold = 0.92f, int maxEntries = 100)
	{
		_similarityThreshold = similarityThreshold;
		_maxEntries = maxEntries;
	}

	// ── Storage ─────────────────────────────────────────────────────────────

	public void Store(string npcId, float[] queryEmbedding, string response)
	{
		if (queryEmbedding.Length == 0) return;

		if (!_entriesByNpc.TryGetValue(npcId, out var entries))
		{
			entries = new List<CacheEntry>();
			_entriesByNpc[npcId] = entries;
		}

		// Simple FIFO eviction — removes oldest inserted entry when capacity is reached.
		// Upgrade path: true LRU would update entry position on TryGet hit.
		// Worth implementing in Phase 4 when hit rates matter more.
		if (entries.Count >= _maxEntries)
			entries.RemoveAt(0);

		entries.Add(new CacheEntry
		{
			QueryEmbedding = queryEmbedding,
			Response = response,
			CreatedAt = DateTime.UtcNow,
		});
	}

	// ── Lookup ──────────────────────────────────────────────────────────────

	public string? TryGet(string npcId, float[] queryEmbedding)
	{
		if (!_entriesByNpc.TryGetValue(npcId, out var entries))
			return null;

		foreach (var entry in entries)
		{
			var similarity = VectorMath.CosineSimilarity(queryEmbedding, entry.QueryEmbedding);
			if (similarity >= _similarityThreshold)
				return entry.Response;
		}

		return null;
	}

	public int Count => _entriesByNpc.Values.Sum(e => e.Count);

	// ── Internal ────────────────────────────────────────────────────────────

	private class CacheEntry
	{
		public float[] QueryEmbedding { get; set; } = Array.Empty<float>();
		public string Response { get; set; } = string.Empty;

		// Reserved for future TTL-based cache invalidation — not yet read
		public DateTime CreatedAt { get; set; }
	}
}