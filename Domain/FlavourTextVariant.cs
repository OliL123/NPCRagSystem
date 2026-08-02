using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain;

public class FlavourTextVariant
{
    // "default", "morning", "evening", "empty", etc.
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "default";

    // Optional — restrict this variant to a specific location id. Used for NPC intro
    // flavours so e.g. a sawyer is described working at the logging hall but drinking
    // at the bar. Null/empty means the variant applies regardless of location.
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    // Null means the variant applies regardless of hour
    [JsonPropertyName("hours")]
    public List<int>? Hours { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
