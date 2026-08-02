using NPCRAGSystem.Domain;
using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.Interfaces.Core;

public interface ILocationRegistry
{
    LocationState? GetLocation(string id);

    // All known locations (dev tooling — e.g. 'setloc' with no arg lists ids).
    IReadOnlyList<LocationState> GetAllLocations();

    // Returns the NPCs present at a location right now (based on schedules)
    IReadOnlyList<NpcState> GetNpcsAt(string locationId, int currentHour, int currentDay);

    // Returns the location id the NPC is currently at (schedule entry, or default).
    // May be an "off-map" id with no LocationState — callers resolve region defensively.
    string GetCurrentLocationId(NpcState npc, int currentHour, int currentDay);

    // True when no schedule block applies right now — the NPC is home (knockable).
    bool IsNpcOffSchedule(NpcState npc, int currentHour, int currentDay);

    // Returns a farewell line if the NPC has left the given location (their schedule
    // block there has ended), else null. Used to gracefully close a conversation.
    string? GetActiveFarewell(NpcState npc, string currentLocationId, int currentHour, int currentDay);

    // Selects the best flavour text variant for the location given current state
    string GetFlavourText(string locationId, int currentHour, bool hasNpcs);
}
