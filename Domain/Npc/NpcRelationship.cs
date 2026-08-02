using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain.Npc;

public class NpcRelationship
{
	[JsonPropertyName("npc_id")]
	public string NpcId { get; set; } = string.Empty;

	[JsonPropertyName("trust")]
	public float Trust { get; set; } = 0.0f;

	// How much this NPC confides in the other — gates sharing of *sensitive* gossip
	// (accusations, uncertain claims, rumours) independently of plain trust. Null means
	// "defaults to trust". Lower it on e.g. parent→child links to share news but not secrets.
	[JsonPropertyName("confide")]
	public float? Confide { get; set; }

	// Effective confide level — falls back to trust when not authored
	[JsonIgnore]
	public float EffectiveConfide => Confide ?? Trust;

	[JsonPropertyName("last_contact")]
	public string? LastContact { get; set; } = null;

	[JsonPropertyName("shared_secrets")]
	public List<string> SharedSecrets { get; set; } = new();
}