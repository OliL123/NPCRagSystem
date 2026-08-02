using System.Text.Json;
using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain.Npc;

public class NpcScheduleEntry
{
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("start_hour")]
    public int StartHour { get; set; }

    [JsonPropertyName("end_hour")]
    public int EndHour { get; set; }

    // Stored as raw JsonElement — either the string "all" or an int array like [0,1,2]
    [JsonPropertyName("days")]
    public JsonElement Days { get; set; }

    [JsonPropertyName("farewell")]
    public string Farewell { get; set; } = string.Empty;

    // 0 = Monday, 6 = Sunday. Returns true if "all" or if the day int is in the array.
    public bool AppliesToDay(int dayOfWeek)
    {
        if (Days.ValueKind == JsonValueKind.String)
            return Days.GetString() == "all";

        if (Days.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in Days.EnumerateArray())
                if (el.TryGetInt32(out var d) && d == dayOfWeek)
                    return true;
        }

        return false;
    }

    // Returns true if the given hour falls within [StartHour, EndHour)
    public bool AppliesToHour(int hour) => hour >= StartHour && hour < EndHour;
}
