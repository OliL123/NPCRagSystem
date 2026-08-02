using NPCRAGSystem.Domain;

namespace NPCRAGSystem.Interfaces.Core;

public interface IGameState
{
    int CurrentDay { get; }
    int CurrentHour { get; }
    int CurrentMinute { get; }
    string CurrentLocation { get; set; }

    WorldContext BuildWorldContext();

    Task AdvanceDayAsync(int days = 1, bool persist = true);

    // Advance time by minutes. Returns true if the day wrapped (for decay trigger).
    Task<bool> AdvanceTimeAsync(int minutes, bool persist = true);

    Task SaveAsync();
}
