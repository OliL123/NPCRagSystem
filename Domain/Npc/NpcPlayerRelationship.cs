using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain.Npc;

public class NpcPlayerRelationship
{
	[JsonPropertyName("trust_player")]
	public float TrustPlayer { get; set; }

	[JsonPropertyName("care_player")]
	public float CarePlayer { get; set; }

	[JsonPropertyName("gullibility")]
	public float Gullibility { get; set; }

	[JsonPropertyName("infatuation_player")]
	public float InfatuationPlayer { get; set; }

	// Tracked via semantic clustering — persisted between sessions
	[JsonPropertyName("player_erratic_behaviour")]
	public float PlayerErraticBehaviour { get; set; }
}