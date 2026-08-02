using System.Text.Json;
using NPCRAGSystem.Configuration;
using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.State.Repositories;

// Append-only queue of cross-settlement gossip awaiting delivery. Persisted to JSON.
// Nothing consumes it yet — the Phase 4 events system will drain DueItems() after travel
// time elapses. For now it simply stops gossip from teleporting between settlements.
public class PendingGossipStore
{
    private readonly string _path;
    private readonly List<PendingGossip> _pending;

    private static readonly JsonSerializerOptions Options = JsonDefaults.Readable;

    private PendingGossipStore(string path, List<PendingGossip> pending)
    {
        _path = path;
        _pending = pending;
    }

    public static async Task<PendingGossipStore> LoadAsync(string path)
    {
        List<PendingGossip> pending;
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                pending = JsonSerializer.Deserialize<List<PendingGossip>>(json, Options) ?? new();
            }
            catch
            {
                pending = new();
            }
        }
        else
        {
            pending = new();
        }

        return new PendingGossipStore(path, pending);
    }

    public int Count => _pending.Count;

    public async Task EnqueueRangeAsync(IEnumerable<PendingGossip> items, bool persist = true)
    {
        _pending.AddRange(items);
        if (persist) await SaveAsync();
    }

    // Gossip whose travel time has elapsed — for the Phase 4 delivery pass to consume.
    public IReadOnlyList<PendingGossip> DueItems(int currentDay)
        => _pending.Where(g => currentDay >= g.DeliverableAfterDay).ToList();

    public async Task RemoveAsync(IEnumerable<PendingGossip> delivered, bool persist = true)
    {
        foreach (var item in delivered) _pending.Remove(item);
        if (persist) await SaveAsync();
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_pending, Options);
        await File.WriteAllTextAsync(_path, json);
    }
}
