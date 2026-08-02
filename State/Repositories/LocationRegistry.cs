using System.Text.Json;
using NPCRAGSystem.Configuration;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Domain;
using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.State.Repositories;

public class LocationRegistry : ILocationRegistry
{
    private readonly Dictionary<string, LocationState> _locations;
    private readonly NpcRegistry _npcRegistry;

    private static readonly JsonSerializerOptions Options = JsonDefaults.Config;

    private LocationRegistry(Dictionary<string, LocationState> locations, NpcRegistry npcRegistry)
    {
        _locations = locations;
        _npcRegistry = npcRegistry;
    }

    public static async Task<LocationRegistry> LoadAsync(string path, NpcRegistry npcRegistry)
    {
        var json = await File.ReadAllTextAsync(path);
        var doc = JsonSerializer.Deserialize<LocationsFile>(json, Options)
            ?? throw new InvalidOperationException("Failed to parse locations.json");

        var dict = doc.Locations.ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"  Locations loaded: {dict.Count}");
        return new LocationRegistry(dict, npcRegistry);
    }

    public LocationState? GetLocation(string id)
        => _locations.TryGetValue(id, out var loc) ? loc : null;

    public IReadOnlyList<LocationState> GetAllLocations() => _locations.Values.ToList();

    public IReadOnlyList<NpcState> GetNpcsAt(string locationId, int currentHour, int currentDay)
    {
        var dayOfWeek = WorldContext.DayOfWeekIndex(currentDay); // 0=Monday, matches schedule "days"
        return _npcRegistry.GetAllNpcs()
            .Where(npc => IsNpcAt(npc, locationId, currentHour, dayOfWeek))
            .ToList();
    }

    public string GetCurrentLocationId(NpcState npc, int currentHour, int currentDay)
    {
        var dayOfWeek = WorldContext.DayOfWeekIndex(currentDay);
        foreach (var entry in npc.Schedule)
        {
            if (!entry.AppliesToDay(dayOfWeek)) continue;
            if (!entry.AppliesToHour(currentHour)) continue;
            return entry.Location;
        }
        return npc.DefaultLocation;
    }

    public string? GetActiveFarewell(NpcState npc, string currentLocationId, int currentHour, int currentDay)
    {
        var dayOfWeek = WorldContext.DayOfWeekIndex(currentDay);

        // A farewell is only owed if the NPC is no longer present where the
        // conversation is taking place. If they're still here, they're not leaving.
        if (IsNpcAt(npc, currentLocationId, currentHour, dayOfWeek))
            return null;

        // They've gone — find the schedule block that placed them here today and has
        // since ended (EndHour <= currentHour catches hours skipped by a long chat),
        // and use its farewell line. Latest-ending matching block wins.
        NpcScheduleEntry? endedHere = null;
        foreach (var entry in npc.Schedule)
        {
            if (!entry.AppliesToDay(dayOfWeek)) continue;
            if (!string.Equals(entry.Location, currentLocationId, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.EndHour <= currentHour &&
                (endedHere == null || entry.EndHour > endedHere.EndHour))
                endedHere = entry;
        }

        if (endedHere == null) return null; // never scheduled here today — no goodbye owed
        return string.IsNullOrEmpty(endedHere.Farewell) ? npc.DefaultFarewell : endedHere.Farewell;
    }

    public string GetFlavourText(string locationId, int currentHour, bool hasNpcs)
    {
        if (!_locations.TryGetValue(locationId, out var loc) || loc.FlavourTexts.Count == 0)
            return string.Empty;

        // Try empty first if no NPCs
        if (!hasNpcs)
        {
            var empty = loc.FlavourTexts.FirstOrDefault(v => v.Condition == "empty");
            if (empty != null) return empty.Text;
        }

        // Evaluate in order, first match wins, fall back to "default"
        foreach (var variant in loc.FlavourTexts)
        {
            if (variant.Condition == "default") continue;
            if (variant.Condition == "empty") continue;
            if (variant.Hours != null && !variant.Hours.Contains(currentHour)) continue;
            return variant.Text;
        }

        return loc.FlavourTexts.FirstOrDefault(v => v.Condition == "default")?.Text ?? string.Empty;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsNpcAt(NpcState npc, string locationId, int currentHour, int dayOfWeek)
    {
        foreach (var entry in npc.Schedule)
        {
            if (!entry.AppliesToDay(dayOfWeek)) continue;
            if (!entry.AppliesToHour(currentHour)) continue;
            return string.Equals(entry.Location, locationId, StringComparison.OrdinalIgnoreCase);
        }
        // No matching schedule entry → the NPC has gone home (off-schedule). They are NOT
        // present in public — find them by knocking (see IsNpcOffSchedule / home_door).
        return false;
    }

    // True when no schedule block applies right now — the NPC is home and can be knocked
    // for at their home_door rather than met in public.
    public bool IsNpcOffSchedule(NpcState npc, int currentHour, int currentDay)
    {
        var dayOfWeek = WorldContext.DayOfWeekIndex(currentDay);
        foreach (var entry in npc.Schedule)
        {
            if (!entry.AppliesToDay(dayOfWeek)) continue;
            if (entry.AppliesToHour(currentHour)) return false;
        }
        return true;
    }

    private class LocationsFile
    {
        public List<LocationState> Locations { get; set; } = new();
    }
}
