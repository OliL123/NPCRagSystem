using System.Text.Json;
using System.Text.Json.Serialization;
using NPCRAGSystem.Configuration;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.State.Repositories;

public class NpcRegistry : INpcRegistry
{
	private readonly string _path;
	private readonly Dictionary<string, NpcState> _npcs;
	private readonly Dictionary<string, string> _npcSourceFiles = new();

	private static readonly JsonSerializerOptions Options = JsonDefaults.Config;

	private NpcRegistry(string path, Dictionary<string, NpcState> npcs, Dictionary<string, string> sourceFiles)
	{
		_path = path;
		_npcs = npcs;
		_npcSourceFiles = sourceFiles;
	}

	// ── Loading & Saving ──────────────────────────────────────────────────────

	public static async Task<NpcRegistry> LoadAsync(string path)
	{
		var sourceFiles = new Dictionary<string, string>();
		var allNpcs = new List<NpcState>();

		var files = Directory.Exists(path)
			? Directory.GetFiles(path, "*.json").OrderBy(f => f).ToArray()
			: [path];

		foreach (var file in files)
		{
			var json = await File.ReadAllTextAsync(file);
			var wrapper = JsonSerializer.Deserialize<NpcListWrapper>(json, Options)
				?? throw new InvalidOperationException($"Failed to deserialise {Path.GetFileName(file)}.");

			foreach (var npc in wrapper.Npcs)
			{
				allNpcs.Add(npc);
				sourceFiles[npc.Id] = file;
			}
		}

		var npcs = allNpcs.ToDictionary(n => n.Id);

		foreach (var npc in npcs.Values)
		{
			BackfillMemoryDefaults(npc);

			// Snapshot the authored baselines ONCE — only when the save doesn't already carry them.
			// Persisted thereafter (see NpcState.Baseline*State), so debug/runtime changes to the
			// live state never become the baseline on a later load.
			npc.BaselineEmotionalState ??= npc.EmotionalState.Clone();
			npc.BaselinePhysicalState ??= npc.PhysicalState.Clone();
		}

		Console.WriteLine($"  NPC registry loaded. {npcs.Count} NPCs from {files.Length} file(s).");
		return new NpcRegistry(path, npcs, sourceFiles);
	}

	public async Task MergeAsync(string path)
	{
		var json = await File.ReadAllTextAsync(path);
		var wrapper = JsonSerializer.Deserialize<NpcListWrapper>(json, Options)
			?? throw new InvalidOperationException($"Failed to deserialise {Path.GetFileName(path)}.");

		foreach (var npc in wrapper.Npcs)
		{
			BackfillMemoryDefaults(npc);

			_npcs[npc.Id] = npc;

			// Preserve the original source file for NPCs that already belong to one.
			// Re-pointing an existing NPC at the debug file would relocate them there on
			// the next SaveAsync — silently removing them from their real regional file.
			if (!_npcSourceFiles.ContainsKey(npc.Id))
				_npcSourceFiles[npc.Id] = path;
		}

		Console.WriteLine($"  Debug NPCs merged. {wrapper.Npcs.Count} added.");
	}

	public async Task SaveAsync()
	{
		if (_npcSourceFiles.Count > 0)
		{
			var byFile = _npcs.Values.GroupBy(n =>
				_npcSourceFiles.TryGetValue(n.Id, out var f) ? f : Path.Combine(_path, "npcs_unsourced.json"));

			foreach (var group in byFile)
			{
				var wrapper = new NpcListWrapper { Npcs = group.ToList() };
				var json = JsonSerializer.Serialize(wrapper, Options);
				await File.WriteAllTextAsync(group.Key, json);
			}
		}
		else
		{
			var wrapper = new NpcListWrapper { Npcs = _npcs.Values.ToList() };
			var json = JsonSerializer.Serialize(wrapper, Options);
			await File.WriteAllTextAsync(_path, json);
		}
	}

	// ── NPC Retrieval ─────────────────────────────────────────────────────────

	public NpcState? GetNpc(string npcId)
		=> _npcs.TryGetValue(npcId, out var npc) ? npc : null;

	public List<NpcState> GetAllNpcs()
		=> _npcs.Values.ToList();

	// Find an NPC by id or name for dev commands (talk/debug/reset). Exact id/name match
	// wins; falls back to a partial name match so "talk cor" finds Corin.
	public NpcState? Resolve(string token)
	{
		if (string.IsNullOrWhiteSpace(token)) return null;
		return _npcs.Values.FirstOrDefault(n =>
				   n.Id.Equals(token, StringComparison.OrdinalIgnoreCase)
				|| n.Name.Equals(token, StringComparison.OrdinalIgnoreCase))
			?? _npcs.Values.FirstOrDefault(n => n.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
	}

	// Ensure every memory has an ID and a decay anchor. InitialFidelity ≤ 0 means the entry
	// predates the field (or was authored without it); current Fidelity is the best anchor.
	private static void BackfillMemoryDefaults(NpcState npc)
	{
		foreach (var m in npc.WorldMemories.Concat(npc.OrphanMemories).Concat(npc.SuspectMemories))
		{
			if (string.IsNullOrEmpty(m.Id)) m.Id = Guid.NewGuid().ToString("N")[..8];
			if (m.InitialFidelity <= 0f) m.InitialFidelity = m.Fidelity;
		}

		foreach (var ep in npc.EpisodicMemories.Where(ep => ep.InitialFidelity <= 0f))
			ep.InitialFidelity = ep.Fidelity;
	}

	// ── State Updates ─────────────────────────────────────────────────────────

	public void UpdateState(string npcId, string attribute, float value)
	{
		if (!_npcs.TryGetValue(npcId, out var npc)) return;

		var clamped = Math.Clamp(value, 0.0f, 1.0f);
		var e = npc.EmotionalState;
		var p = npc.PhysicalState;
		var r = npc.PlayerRelationship;

		switch (attribute.ToLowerInvariant())
		{
			// Emotional
			case "fear": e.Fear = clamped; break;
			case "grief": e.Grief = clamped; break;
			case "hope": e.Hope = clamped; break;
			case "suspicion": e.Suspicion = clamped; break;
			case "anger": e.Anger = clamped; break;
			case "anxiety": e.Anxiety = clamped; break;
			case "disgust": e.Disgust = clamped; break;
			case "guilt": e.Guilt = clamped; break;

			// Physical
			case "exhaustion": p.Exhaustion = clamped; break;
			case "pain": p.Pain = clamped; break;
			case "intoxication": p.Intoxication = clamped; break;
			case "hunger": p.Hunger = clamped; break;
			case "illness": p.Illness = clamped; break;

			// Player relationship
			case "trust_player": r.TrustPlayer = clamped; break;
			case "care_player": r.CarePlayer = clamped; break;
			case "gullibility": r.Gullibility = clamped; break;
			case "infatuation_player": r.InfatuationPlayer = clamped; break;
			case "player_erratic_behaviour": r.PlayerErraticBehaviour = clamped; break;

			default:
				Console.WriteLine($"  [NpcRegistry] Unknown state attribute: {attribute}");
				break;
		}
	}

	public void ResetToBaseline(string npcId)
	{
		if (!_npcs.TryGetValue(npcId, out var npc)) return;

		// Drop everything learned about the player this session. Authored WorldMemories are
		// the base set and stay; only the accumulated player-derived collections are cleared.
		npc.OrphanMemories.Clear();
		npc.SuspectMemories.Clear();
		npc.EpisodicMemories.Clear();

		// Restore mood AND physical state to the authored baselines (snapshotted at load).
		if (npc.BaselineEmotionalState is { } b)
			npc.EmotionalState.CopyFrom(b);
		if (npc.BaselinePhysicalState is { } bp)
			npc.PhysicalState.CopyFrom(bp);
	}

	// ── Internal ──────────────────────────────────────────────────────────────

	private class NpcListWrapper
	{
		[JsonPropertyName("npcs")]
		public List<NpcState> Npcs { get; set; } = new();
	}
}