using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain;

public class PlayerState
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("has_completed_intro")]
    public bool HasCompletedIntro { get; set; } = false;

    [JsonPropertyName("known_npcs")]
    public List<string> KnownNpcs { get; set; } = new();

    // Generic world/quest flags the player has tripped (e.g. "gate_south_cleared").
    [JsonPropertyName("flags")]
    public List<string> Flags { get; set; } = new();

    // What the player looks like on sight — injected into NPC prompts so appearance
    // reactions are grounded rather than confabulated. Fixed for the pilot.
    [JsonPropertyName("appearance")]
    public string Appearance { get; set; } =
        "a road-weary traveller of average height and build, with black hair, and nothing else remarkable about them";
}
