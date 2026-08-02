using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain;

// A "happening" is a time-gated goings-on inside a location — the band at a tavern, a hiring
// crowd on a street, a soup line — that the player can check out. It is NOT a separate
// location. It can gather named NPCs (who then appear under it instead of loose in the room),
// and it can be either a fixture (Chance >= 1) or random (Chance < 1, but decided once per day
// so it stays put for the whole day instead of flickering on every render).
public class Happening
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    // Menu label, e.g. "the hiring crowd" -> shown as "check out the hiring crowd".
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    // Printed when the player checks it out.
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    // Null/empty = any hour.
    [JsonPropertyName("hours")]
    public List<int>? Hours { get; set; }

    // Day-of-week indices (0-6) it can occur on. Null/empty = any day.
    [JsonPropertyName("days")]
    public List<int>? Days { get; set; }

    // 1.0 = always on within its hours/days (a fixture). < 1.0 = random, but rolled once per
    // day from a stable seed so it persists for that whole day.
    [JsonPropertyName("chance")]
    public double Chance { get; set; } = 1.0;

    // NPCs gathered here while it is active (and present at the location by their schedule).
    [JsonPropertyName("npc_ids")]
    public List<string> NpcIds { get; set; } = new();

    public bool IsActive(int hour, int dayOfWeek, int absoluteDay)
    {
        if (Hours is { Count: > 0 } && !Hours.Contains(hour)) return false;
        if (Days is { Count: > 0 } && !Days.Contains(dayOfWeek)) return false;
        if (Chance >= 1.0) return true;
        return StableUnit($"{Id}:{absoluteDay}") < Chance;
    }

    // Deterministic value in [0,1) from a string (FNV-1a). string.GetHashCode is randomised
    // per process, so it can't be used for a day-stable roll.
    private static double StableUnit(string s)
    {
        uint hash = 2166136261;
        foreach (char c in s)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash / 4294967296.0;
    }
}
