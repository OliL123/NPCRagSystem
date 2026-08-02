using System.Text.Json;
using NPCRAGSystem.Configuration;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Domain;

namespace NPCRAGSystem.State.Managers;

public class PlayerStateManager : IPlayerState
{
    private readonly string _path;
    private readonly PlayerState _state;

    private static readonly JsonSerializerOptions Options = JsonDefaults.Readable;

    public PlayerStateManager(string path, PlayerState state)
    {
        _path = path;
        _state = state;
    }

    public static async Task<PlayerStateManager> LoadAsync(string path, bool resetOnStartup = false)
    {
        PlayerState state;
        if (!resetOnStartup && File.Exists(path))
        {
            var json = await File.ReadAllTextAsync(path);
            state = JsonSerializer.Deserialize<PlayerState>(json) ?? new PlayerState();
        }
        else
        {
            state = new PlayerState();
        }

        var nameDisplay = string.IsNullOrEmpty(state.Name) ? "(unknown)" : state.Name;
        Console.WriteLine($"  Player state loaded. Name: {nameDisplay}, Intro complete: {state.HasCompletedIntro}");
        return new PlayerStateManager(path, state);
    }

    public string Name => _state.Name;
    public string Appearance => _state.Appearance;
    public bool HasCompletedIntro => _state.HasCompletedIntro;
    public bool IsNpcKnown(string npcId) => _state.KnownNpcs.Contains(npcId);

    public async Task SetNameAsync(string name, bool persist = true)
    {
        _state.Name = name;
        if (persist) await SaveAsync();
    }

    public async Task CompleteIntroAsync(bool persist = true)
    {
        _state.HasCompletedIntro = true;
        if (persist) await SaveAsync();
    }

    public async Task RevealNpcAsync(string npcId, bool persist = true)
    {
        if (_state.KnownNpcs.Contains(npcId)) return;
        _state.KnownNpcs.Add(npcId);
        if (persist) await SaveAsync();
    }

    public bool HasFlag(string flag) => _state.Flags.Contains(flag);

    public async Task SetFlagAsync(string flag, bool persist = true)
    {
        if (_state.Flags.Contains(flag)) return;
        _state.Flags.Add(flag);
        if (persist) await SaveAsync();
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_state, Options);
        await File.WriteAllTextAsync(_path, json);
    }
}
