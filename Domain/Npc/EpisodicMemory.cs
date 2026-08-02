using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain.Npc;

public class EpisodicMemory
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

	[JsonPropertyName("content")]
	public string Content { get; set; } = string.Empty;

	[JsonPropertyName("fidelity")]
	public float Fidelity { get; set; } = 1.0f;

	// Fidelity at creation — anchor for absolute decay (see NpcMemory.InitialFidelity)
	[JsonPropertyName("initial_fidelity")]
	public float InitialFidelity { get; set; } = 0f;

	[JsonPropertyName("decay_weight")]
	public float DecayWeight { get; set; } = 1.5f;

	[JsonPropertyName("trauma_tagged")]
	public bool TraumaTagged { get; set; } = false;

	[JsonPropertyName("timestamp")]
	public string Timestamp { get; set; } = string.Empty;

	// IDs of orphan player memories claimed by this episode
	[JsonPropertyName("linked_memory_ids")]
	public List<string> LinkedMemoryIds { get; set; } = new();

	// Emotional delta from baseline — only floats with |delta| > 0.1 stored
	[JsonPropertyName("emotional_snapshot")]
	public Dictionary<string, float> EmotionalSnapshot { get; set; } = new();

	// Whether this episode was triggered by a significant event
	[JsonPropertyName("is_significant")]
	public bool IsSignificant { get; set; } = false;
}