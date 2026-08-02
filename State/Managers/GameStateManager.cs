using System.Text.Json;
using NPCRAGSystem.Configuration;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Domain;

namespace NPCRAGSystem.State.Managers;

public class GameStateManager : IGameState
{
    private readonly string _path;
    private readonly GameState _state;
    private readonly INpcMemoryManager _npcMemoryManager;

    private static readonly JsonSerializerOptions Options = JsonDefaults.Readable;

    public GameStateManager(string path, GameState state, INpcMemoryManager npcMemoryManager)
    {
        _path = path;
        _state = state;
        _npcMemoryManager = npcMemoryManager;
    }

    // ── Loading & Saving ─────────────────────────────────────────────────────

    public static async Task<GameStateManager> LoadAsync(
        string path,
        INpcMemoryManager npcMemoryManager,
        bool resetDayOnStartup = false)
    {
        GameState state;

        if (File.Exists(path) && !resetDayOnStartup)
        {
            var json = await File.ReadAllTextAsync(path);
            state = JsonSerializer.Deserialize<GameState>(json) ?? new GameState();
        }
        else
        {
            state = new GameState();
        }

        Console.WriteLine($"  Game state loaded. Day {state.CurrentDay}, {state.CurrentHour:D2}:00");
        return new GameStateManager(path, state, npcMemoryManager);
    }

    // ── Time ─────────────────────────────────────────────────────────────────

    public int CurrentDay => _state.CurrentDay;
    public int CurrentHour => _state.CurrentHour;
    public int CurrentMinute => _state.CurrentMinute;

    public WorldContext BuildWorldContext() => new(
        Hour:      _state.CurrentHour,
        Minute:    _state.CurrentMinute,
        GameDay:   _state.CurrentDay,
        TimeLabel: WorldContext.TimeLabelFromHour(_state.CurrentHour),
        DayOfWeek: WorldContext.DayNameFromDay(_state.CurrentDay),
        Season:    WorldContext.SeasonFromDay(_state.CurrentDay),
        Weather:   WorldContext.WeatherFromDay(_state.CurrentDay)
    );

    public string CurrentLocation
    {
        get => _state.CurrentLocation;
        set => _state.CurrentLocation = value;
    }

    public async Task AdvanceDayAsync(int days = 1, bool persist = true)
    {
        _state.CurrentDay += days;
        _npcMemoryManager.DecayMemories(_state.CurrentDay);
        Console.WriteLine($"[time] Day {_state.CurrentDay}, {_state.CurrentHour:D2}:{_state.CurrentMinute:D2} — {WorldContext.WeatherFromDay(_state.CurrentDay)}");
        if (persist) await SaveAsync();
    }

    // Returns true if day wrapped (triggers decay)
    public async Task<bool> AdvanceTimeAsync(int minutes, bool persist = true)
    {
        var totalMinutes = _state.CurrentHour * 60 + _state.CurrentMinute + minutes;
        var newDay = totalMinutes / (24 * 60);
        var remaining = totalMinutes % (24 * 60);
        _state.CurrentHour = remaining / 60;
        _state.CurrentMinute = remaining % 60;

        bool dayWrapped = newDay > 0;
        if (dayWrapped)
        {
            _state.CurrentDay += newDay;
            _npcMemoryManager.DecayMemories(_state.CurrentDay);
        }

        if (persist) await SaveAsync();
        return dayWrapped;
    }

    // Estimate elapsed minutes for an exchange from its word count. Modelled at ~80 words a
    // minute of wall-clock time (speech plus the pauses, thinking and reading around it),
    // rounded up, minimum 1 minute. NOTE: this must use floating-point division — the old
    // `words / 140` was integer division, so anything under 700 words collapsed to the 5-min
    // floor and the clock advanced a flat 5 minutes regardless of how long the exchange was.
    public static int WordCountToMinutes(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Ceiling(words / 80.0));
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_state, Options);
        await File.WriteAllTextAsync(_path, json);
    }
}
