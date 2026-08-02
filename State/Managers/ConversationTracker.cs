using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.Domain.Npc;

namespace NPCRAGSystem.State.Managers;

public class ConversationTracker : IConversationTracker
{
	private readonly INpcRegistry _registry;

	private readonly Dictionary<string, List<ConversationTurn>> _conversationHistories = new();
	private readonly Dictionary<string, List<float[]>> _playerMessageWindows = new();

	// ── Constants ─────────────────────────────────────────────────────────────
	// How many of the player's recent message embeddings we keep per NPC for the
	// repetition / incoherence (erratic-behaviour) checks.
	private const int PlayerMessageWindowSize = 10;
	private const float RepetitionThreshold = 0.95f;
	private const float IncoherenceThreshold = 0.2f;

	// How many recent player+NPC turns we keep per NPC to feed back into the prompt.
	private readonly int _conversationHistoryWindow;

	public ConversationTracker(INpcRegistry registry, int conversationHistoryWindow = 6)
	{
		_registry = registry;
		_conversationHistoryWindow = conversationHistoryWindow;
	}

	// ── Conversation History ──────────────────────────────────────────────────

	public void AddConversationTurn(string npcId, string playerMessage, string npcResponse)
	{
		if (!_conversationHistories.TryGetValue(npcId, out var history))
		{
			history = new List<ConversationTurn>();
			_conversationHistories[npcId] = history;
		}

		history.Add(new ConversationTurn
		{
			PlayerMessage = playerMessage,
			NpcResponse = npcResponse
		});

		if (history.Count > _conversationHistoryWindow)
			history.RemoveAt(0);
	}

	public List<ConversationTurn> GetConversationHistory(string npcId)
		=> _conversationHistories.TryGetValue(npcId, out var history)
			? history
			: new List<ConversationTurn>();

	// Drop the verbatim recent-turns thread for this NPC. Called when a conversation ends
	// (a new session shouldn't see the last one's exact lines — long-term episodic memory
	// carries the gist) and on demand via the dev 'forget' command. Also clears the player
	// message window so the erratic-behaviour check restarts fresh.
	public void ClearConversationHistory(string npcId)
	{
		_conversationHistories.Remove(npcId);
		_playerMessageWindows.Remove(npcId);
	}

	// ── Player Behaviour Evaluation ───────────────────────────────────────────

	public void EvaluatePlayerBehaviour(string npcId, float[] queryEmbedding)
	{
		var npc = _registry.GetNpc(npcId);
		if (npc == null) return;

		if (!_playerMessageWindows.TryGetValue(npcId, out var window))
		{
			window = new List<float[]>();
			_playerMessageWindows[npcId] = window;
		}

		// First few exchanges weigh more heavily — a strong first impression (good or
		// bad) sticks. Evaluated before this message is appended to the window.
		var isFirstImpression = window.Count < 3;

		if (window.Count > 0)
		{
			var weightedSimilarities = window
				.Select((e, i) => (
					Similarity: VectorMath.CosineSimilarity(queryEmbedding, e),
					// window is ordered oldest→newest, so more recent messages
					// (higher index) carry more weight in the coherence average
					Weight: (float)(i + 1) / window.Count
				)).ToList();

			var maxSimilarity = weightedSimilarities.Max(x => x.Similarity);
			var avgSimilarity = weightedSimilarities.Sum(x => x.Similarity * x.Weight)
							  / weightedSimilarities.Sum(x => x.Weight);

			if (maxSimilarity >= RepetitionThreshold)
			{
				var increment = isFirstImpression ? 0.3f : 0.15f;
				_registry.UpdateState(npcId, "player_erratic_behaviour",
					npc.PlayerRelationship.PlayerErraticBehaviour + increment);
			}
			else if (avgSimilarity <= IncoherenceThreshold && window.Count >= 3)
			{
				var increment = isFirstImpression ? 0.25f : 0.1f;
				_registry.UpdateState(npcId, "player_erratic_behaviour",
					npc.PlayerRelationship.PlayerErraticBehaviour + increment);
			}
			else if (window.Count >= 5)
			{
				_registry.UpdateState(npcId, "player_erratic_behaviour",
					Math.Max(0f, npc.PlayerRelationship.PlayerErraticBehaviour - 0.02f));
			}
		}

		window.Add(queryEmbedding);
		if (window.Count > PlayerMessageWindowSize) window.RemoveAt(0);
	}
}