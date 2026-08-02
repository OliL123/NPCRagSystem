using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain.Npc;

public class NpcMemory
{
	// Generated on creation — used for episodic linking
	[JsonPropertyName("id")]
	public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

	[JsonPropertyName("content")]
	public string Content { get; set; } = string.Empty;

	[JsonPropertyName("fidelity")]
	public float Fidelity { get; set; } = 1.0f;

	// Fidelity at creation — decay is computed absolutely from this anchor and
	// the timestamp, so repeated decay passes are idempotent and self-healing.
	// 0 means "not yet set"; backfilled to current Fidelity on load/insert.
	[JsonPropertyName("initial_fidelity")]
	public float InitialFidelity { get; set; } = 0f;

	[JsonPropertyName("decay_weight")]
	public float DecayWeight { get; set; } = 1.0f;

	[JsonPropertyName("trauma_tagged")]
	public bool TraumaTagged { get; set; } = false;

	[JsonPropertyName("timestamp")]
	public string Timestamp { get; set; } = string.Empty;

	// How believable the NPC found this information when it was formed (0–1).
	// Derived from trust, suspicion, gullibility etc. at the moment of memory creation.
	[JsonPropertyName("credibility")]
	public float Credibility { get; set; } = 1.0f;

	// The epistemic nature of this memory — see MemoryNature for valid values.
	[JsonPropertyName("nature")]
	public string Nature { get; set; } = MemoryNature.Fact;

	// Set when claimed by an episodic memory
	[JsonPropertyName("episode_id")]
	public string? EpisodeId { get; set; } = null;
}