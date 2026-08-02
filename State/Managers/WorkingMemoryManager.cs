using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Domain.Npc;
using NPCRAGSystem.Utils;

namespace NPCRAGSystem.State.Managers;

public class WorkingMemoryManager : IWorkingMemoryManager
{
	private readonly INpcRegistry _registry;
	private readonly bool _logMemory;

	private readonly Dictionary<string, List<WorkingMemory>> _authoredWorkingMemory = new();
	private readonly Dictionary<string, List<WorkingMemory>> _dynamicWorkingMemory = new();
	private readonly Dictionary<string, Dictionary<string, int>> _entityMentionCounts = new();

	// ── Constants ─────────────────────────────────────────────────────────────
	private const int AuthoredSlots = 3;
	private const int DynamicSlots = 5;
	private const int RepeatThreshold = 2;

	public WorkingMemoryManager(INpcRegistry registry, bool logMemory = false)
	{
		_registry = registry;
		_logMemory = logMemory;
	}

	// ── Authored Working Memory ───────────────────────────────────────────────

	public void AddAuthoredWorkingMemory(
		string npcId,
		string content,
		string flavourText = "",
		bool isSignificant = false)
	{
		if (!_authoredWorkingMemory.TryGetValue(npcId, out var slots))
		{
			slots = new List<WorkingMemory>();
			_authoredWorkingMemory[npcId] = slots;
		}

		if (slots.Count >= AuthoredSlots) slots.RemoveAt(0);

		slots.Add(new WorkingMemory
		{
			Content = content,
			FlavourText = flavourText,
			IsAuthored = true,
			IsSignificant = isSignificant
		});
	}

	// ── Dynamic Working Memory ────────────────────────────────────────────────

	public void AddDynamicWorkingMemory(string npcId, string content, int currentDay)
	{
		if (!_dynamicWorkingMemory.TryGetValue(npcId, out var slots))
		{
			slots = new List<WorkingMemory>();
			_dynamicWorkingMemory[npcId] = slots;
		}

		if (slots.Any(m => m.Content.Equals(content, StringComparison.OrdinalIgnoreCase))) return;
		if (slots.Count >= DynamicSlots) slots.RemoveAt(0);

		slots.Add(new WorkingMemory
		{
			Content = content,
			IsAuthored = false,
			CreatedAt = currentDay
		});
	}

	public void TrackEntityMentions(string npcId, IEnumerable<string> entities, int currentDay)
	{
		if (!_entityMentionCounts.TryGetValue(npcId, out var counts))
		{
			counts = new Dictionary<string, int>();
			_entityMentionCounts[npcId] = counts;
		}

		foreach (var entity in entities)
		{
			counts.TryGetValue(entity, out var current);
			counts[entity] = current + 1;

			if (counts[entity] == RepeatThreshold)
			{
				AddDynamicWorkingMemory(npcId,
					$"This person has asked about {entity} more than once — they have a specific interest in it.",
					currentDay);

				if (_logMemory)
				{
					ConsoleEx.Dim($"[working memory] dynamic: repeated interest in '{entity}'");
				}
			}
		}
	}

	// ── Retrieval & Clearing ──────────────────────────────────────────────────

	public List<WorkingMemory> GetWorkingMemory(string npcId)
	{
		var authored = _authoredWorkingMemory.TryGetValue(npcId, out var a)
			? a : new List<WorkingMemory>();
		var dynamic = _dynamicWorkingMemory.TryGetValue(npcId, out var d)
			? d : new List<WorkingMemory>();
		return authored.Concat(dynamic).ToList();
	}

	public void ClearWorkingMemory(string npcId)
	{
		_authoredWorkingMemory.Remove(npcId);
		_dynamicWorkingMemory.Remove(npcId);
		_entityMentionCounts.Remove(npcId);
	}
}