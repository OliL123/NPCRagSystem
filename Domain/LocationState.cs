using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain;

public class LocationState
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    // Settlement this location belongs to ("carvallen", "antitheis", "wilderness").
    // Used to gate gossip propagation — rumours don't cross settlements instantly.
    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    // IDs of locations reachable in one step from here
    [JsonPropertyName("connected_locations")]
    public List<string> ConnectedLocations { get; set; } = new();

    // True if travelling here from a different major location advances the day
    [JsonPropertyName("travel_advances_day")]
    public bool TravelAdvancesDay { get; set; }

    // Minutes of in-game time this move costs. 0 = sub-room (no time cost). Defaults to 15.
    [JsonPropertyName("travel_time_minutes")]
    public int TravelTimeMinutes { get; set; } = 15;

    [JsonPropertyName("flavour_texts")]
    public List<FlavourTextVariant> FlavourTexts { get; set; } = new();

    // Time-gated goings-on inside this location (a band, a hiring crowd, a random commotion).
    // Rendered as "check out X" options; can gather NPCs out of the loose room list.
    [JsonPropertyName("happenings")]
    public List<Happening> Happenings { get; set; } = new();

    // Opening hours. When open_hour == close_hour (default 0/0) the place is always open.
    // Otherwise it is open for [open_hour, close_hour); outside that it is locked and the
    // player can't enter — shown faded in the exit list. (e.g. school, auction house.)
    [JsonPropertyName("open_hour")]
    public int OpenHour { get; set; } = 0;

    [JsonPropertyName("close_hour")]
    public int CloseHour { get; set; } = 0;

    public bool IsOpenAt(int hour)
    {
        if (OpenHour == CloseHour) return true;          // always open
        if (OpenHour < CloseHour) return hour >= OpenHour && hour < CloseHour;
        return hour >= OpenHour || hour < CloseHour;     // wraps past midnight
    }
}
