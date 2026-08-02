using System.Text;
using System.Text.Json;

namespace NPCRAGSystem.Services;

// Captures dialogue turns as JSONL for fine-tuning. Each line is one
// (system prompt + player line) → reply pair plus metadata, which is exactly the
// shape supervised fine-tuning consumes. The most recent turn is held in a buffer
// so it can be tagged in-game ('tag good|edit|discard …'); an untagged turn is
// flushed automatically when the next turn arrives or the conversation ends.
public class TrainingDataLogger
{
	private readonly string _path;          // machine format (JSONL) — fed to fine-tuning
	private readonly string _readablePath;  // human format (TXT) — for judging/curation
	private readonly object _lock = new();
	private Pending? _pending;

	private sealed class Pending
	{
		public required string Model;
		public required string NpcId;
		public required string NpcName;
		public required string SystemPrompt;
		public required string UserQuery;
		public required string Response;
		public required Dictionary<string, float> State;
		public required int Turn;   // 0-based depth in the conversation (0 = first turn / fresh context)
		public bool Written;
	}

	public TrainingDataLogger(string path)
	{
		_path = path;
		_readablePath = Path.ChangeExtension(path, ".txt");
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
	}

	// Stage a turn. Flushes the previous one as "untagged" first, so at most one turn
	// is ever awaiting a tag.
	public void BufferTurn(
		string model, string npcId, string npcName,
		string systemPrompt, string userQuery, string response,
		Dictionary<string, float> state, int turn = 0)
	{
		lock (_lock)
		{
			FlushUntaggedLocked();
			_pending = new Pending
			{
				Model = model,
				NpcId = npcId,
				NpcName = npcName,
				SystemPrompt = systemPrompt,
				UserQuery = userQuery,
				Response = response,
				State = state,
				Turn = turn
			};
		}
	}

	// Tag the buffered turn and write it. Returns false if there's nothing to tag.
	public bool Tag(string tag, string? texture, string? note)
	{
		lock (_lock)
		{
			if (_pending == null || _pending.Written) return false;
			WriteLocked(_pending, tag, texture, note);
			return true;
		}
	}

	// Write any buffered-but-untagged turn (called on conversation end / shutdown).
	public void FlushUntagged()
	{
		lock (_lock) FlushUntaggedLocked();
	}

	private void FlushUntaggedLocked()
	{
		if (_pending != null && !_pending.Written)
			WriteLocked(_pending, "untagged", null, null);
	}

	private void WriteLocked(Pending t, string tag, string? texture, string? note)
	{
		var record = new
		{
			ts = DateTime.UtcNow.ToString("o"),
			model = t.Model,
			npc_id = t.NpcId,
			npc = t.NpcName,
			tag,
			texture,
			note,
			turn = t.Turn,
			state = t.State,
			system = t.SystemPrompt,
			user = t.UserQuery,
			response = t.Response
		};

		var line = JsonSerializer.Serialize(record);
		File.AppendAllText(_path, line + "\n", Encoding.UTF8);

		// Human-readable companion — drops the full system prompt, keeps what you read while
		// judging: tag, who, state (strongest first), the player line and the reply.
		var stateStr = t.State.Count > 0
			? string.Join(", ", t.State.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0.00}"))
			: "(neutral)";

		var human = new StringBuilder();
		human.Append("\n──────────────────────────────────────────────\n");
		human.Append($"[{tag}] {t.Model} · {t.NpcName} · {DateTime.Now:yyyy-MM-dd HH:mm}\n");
		if (!string.IsNullOrEmpty(texture)) human.Append($"texture: {texture}\n");
		if (!string.IsNullOrEmpty(note)) human.Append($"note: {note}\n");
		human.Append($"state: {stateStr}\n");
		human.Append($"> {t.UserQuery}\n");
		human.Append($"  {t.Response.Replace("\n", "\n  ")}\n");
		File.AppendAllText(_readablePath, human.ToString(), Encoding.UTF8);

		t.Written = true;
	}
}
