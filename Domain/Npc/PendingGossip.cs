using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain.Npc;

// A piece of gossip that could not be delivered immediately because the target NPC
// is in a different settlement. Parked in the pending queue for the Phase 4 events
// system to deliver once enough in-game time has passed for word to travel.
public class PendingGossip
{
    [JsonPropertyName("target_npc_id")]
    public string TargetNpcId { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("fidelity")]
    public float Fidelity { get; set; }

    [JsonPropertyName("credibility")]
    public float Credibility { get; set; }

    [JsonPropertyName("decay_weight")]
    public float DecayWeight { get; set; } = 1.0f;

    [JsonPropertyName("nature")]
    public string Nature { get; set; } = MemoryNature.Rumour;

    [JsonPropertyName("source_name")]
    public string SourceName { get; set; } = string.Empty;

    [JsonPropertyName("created_day")]
    public int CreatedDay { get; set; }

    // Earliest in-game day this gossip may be delivered (created day + travel time).
    [JsonPropertyName("deliverable_after_day")]
    public int DeliverableAfterDay { get; set; }
}
