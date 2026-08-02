using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain;

public class GameState
{
    [JsonPropertyName("current_day")]
    public int CurrentDay { get; set; } = 1;

    [JsonPropertyName("current_hour")]
    public int CurrentHour { get; set; } = 8;

    [JsonPropertyName("current_minute")]
    public int CurrentMinute { get; set; } = 0;

    [JsonPropertyName("current_location")]
    public string CurrentLocation { get; set; } = "sleeping_hound_bar";
}
