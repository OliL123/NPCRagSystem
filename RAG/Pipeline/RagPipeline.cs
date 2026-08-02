using NPCRAGSystem.Configuration;
using NPCRAGSystem.State.Managers;
using NPCRAGSystem.State.Repositories;
using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Interfaces.Classification;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Interfaces.Retrieval;
using NPCRAGSystem.Domain;
using NPCRAGSystem.Domain.Npc;
using NPCRAGSystem.RAG.Classification;
using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.Services;
using System.Text;
using NPCRAGSystem.Utils;

namespace NPCRAGSystem.RAG.Pipeline;

public class RagPipeline
{
	private readonly SystemConfig _systemConfig;
	private readonly OutputConfig _outputConfig;
	private readonly IEmbeddingService _embeddingService;
	private readonly ILlmService _llm;
	private readonly IVectorLoreData _vectorLoreData;
	private readonly IComplexityClassifier _complexityClassifier;
	private readonly ITopicClassifier _topicClassifier;
	private readonly IBM25Index _bm25;
	private readonly IReranker _reranker;
	private readonly IChunkCompressor _chunkCompressor;
	private readonly IHyDEGenerator _hyde;
	private readonly IMMRSelector _mmr;
	private readonly IEntityRegistry _entityRegistry;
	private readonly INpcRegistry _npcRegistry;
	private readonly INpcMemoryManager _npcMemoryManager;
	private readonly IConversationTracker _conversationTracker;
	private readonly IWorkingMemoryManager _workingMemoryManager;
	private readonly IConversationMemoryCreator _conversationMemoryCreator;
	private readonly IEpisodicMemoryCreator _episodicCreator;
	private readonly IMemoryConsolidator _memoryConsolidator;
	private readonly IGameState _gameState;
	private readonly IScarTissueCompressor _scarTissueCompressor;
	private readonly ISelfCritiqueService _selfCritiqueService;
	private readonly ILocationRegistry _locationRegistry;
	private readonly IPlayerState? _playerState;
	private readonly ClaimDetector? _claimDetector;
	private readonly int _topNumChunk;
	private readonly TrainingDataLogger? _trainingLogger;
	private readonly string _primaryModelName;

	// Constraint injected by claim detection — consumed once per query turn
	private string? _pendingConstraint;

	// Per-NPC background task running this turn's memory bookkeeping (extraction +
	// scar-tissue + save). Flushed before the next interaction with the NPC, so at most
	// one is ever pending — the work can never build up into a backlog.
	private readonly Dictionary<string, Task> _pendingPostTurn = new();

	// Cached embeddings of player-derived memories (orphan/suspect/episodic), keyed by
	// memory id. Computed once per memory and reused to rank memories by relevance to the
	// current query. In-memory only — not persisted. Pruned of stale entries once it grows
	// past the threshold (memories dropped by the cap/decay/consolidation leave orphans here).
	private readonly Dictionary<string, float[]> _memoryEmbeddings = new();
	private const int MemoryEmbeddingPruneThreshold = 512;

	public RagPipeline(RagPipelineConfig config)
	{
		_systemConfig = config.SystemConfig;
		_outputConfig = config.OutputConfig;
		_embeddingService = config.EmbeddingService;
		_llm = config.Llm;
		_vectorLoreData = config.LoreData;
		_complexityClassifier = config.ComplexityClassifier;
		_topicClassifier = config.TopicClassifier;
		_bm25 = config.Bm25;
		_reranker = config.Reranker;
		_chunkCompressor = config.ChunkCompressor;
		_hyde = config.HyDE;
		_mmr = config.MMR;
		_entityRegistry = config.EntityRegistry;
		_npcRegistry = config.NpcRegistry;
		_npcMemoryManager = config.NpcMemoryManager;
		_conversationTracker = config.ConversationTracker;
		_workingMemoryManager = config.WorkingMemoryManager;
		_conversationMemoryCreator = config.ConversationMemoryCreator;
		_episodicCreator = config.EpisodicMemoryCreator;
		_memoryConsolidator = config.MemoryConsolidator;
		_gameState = config.GameState;
		_scarTissueCompressor = config.ScarTissueCompressor;
		_selfCritiqueService = config.SelfCritiqueService;
		_locationRegistry = config.LocationRegistry;
		_playerState = config.PlayerState;
		_claimDetector = config.ClaimDetector;
		_topNumChunk = config.TopNumChunk;
		_trainingLogger = config.TrainingLogger;
		_primaryModelName = config.PrimaryModelName;
	}

	// The base world context carries only time/weather; enrich it with the current location so the
	// NPC actually knows WHERE it is. Used everywhere a persona/prompt is built.
	private WorldContext BuildWorld()
	{
		var world = _gameState.BuildWorldContext();
		var loc = _locationRegistry.GetLocation(_gameState.CurrentLocation);
		return loc == null ? world : world with { LocationName = loc.Name, LocationRegion = loc.Region };
	}

	// ── Entry point ───────────────────────────────────────────────────────────
	// Orchestrates the full query lifecycle. Each stage lives in its own method
	// below, in pipeline order.

	public async Task<string> QueryAsync(string npcId, string userQuery, string? displayName = null)
	{
		var npc = _npcRegistry.GetNpc(npcId)
			?? throw new InvalidOperationException($"NPC '{npcId}' not found in registry.");

		// Finish last turn's background memory work before reading/mutating this NPC
		await FlushPendingMemoryWorkAsync(npcId);

		// Embed the player's message once — reused for complexity classification, memory
		// relevance ranking, and (unless HyDE replaces it) retrieval. Avoids embedding twice.
		var plainEmbedding = await _embeddingService.GetEmbeddingAsync(userQuery);

		// ── Classification ────────────────────────────────────────────────────
		var complexity = await ClassifyComplexityAsync(userQuery, plainEmbedding);
		var topics = ClassifyTopics(userQuery);

		// Track entity mentions for dynamic working memory
		var mentionedEntities = _entityRegistry.GetEntitiesForText(userQuery);
		_workingMemoryManager.TrackEntityMentions(npcId, mentionedEntities, _gameState.CurrentDay);

		// ── Embedding ─────────────────────────────────────────────────────────
		// Complex queries retrieve via a HyDE hypothetical-answer embedding; everything
		// else reuses the plain query embedding above.
		var queryEmbedding = (complexity == QueryComplexity.Complex && _systemConfig.UseHyDE)
			? await _hyde.GenerateHypotheticalEmbeddingAsync(userQuery)
			: plainEmbedding;

		// Evaluate player behaviour using the PLAIN query embedding — never the HyDE one.
		// This detector compares the message against the player's recent messages via
		// cosine similarity, so every turn must live in the same vector space. queryEmbedding
		// is the HyDE hypothetical-answer vector on Complex queries, which would make the
		// cross-turn comparison meaningless; plainEmbedding is consistent across all turns.
		if (_systemConfig.UsePlayerBehaviourEvaluation && !_systemConfig.CollectionMode)
			_conversationTracker.EvaluatePlayerBehaviour(npcId, plainEmbedding);

		// ── Retrieval ─────────────────────────────────────────────────────────
		var retrievedChunks = await RetrieveAsync(userQuery, queryEmbedding, topics);

		if (retrievedChunks.Count == 0)
		{
			// Narration, not dialogue — printed here because the caller discards
			// the return value, but kept out of conversation history
			var fallback = $"{npc.Name} stares at you blankly.";
			Console.WriteLine($"\n{fallback}");
			return fallback;
		}

		// ── Claim detection ───────────────────────────────────────────────────
		_pendingConstraint = null;
		if (_systemConfig.UseClaimDetection && !_systemConfig.CollectionMode && _claimDetector != null)
		{
			var history = _conversationTracker.GetConversationHistory(npcId);
			var claimResult = await _claimDetector.DetectAsync(userQuery, npc, history);
			if (claimResult.Type != "none")
				await HandleClaimDetectionAsync(npc, npcId, claimResult);
		}

		// ── Prompt construction ───────────────────────────────────────────────
		var contextBlock = BuildContextBlock(retrievedChunks);
		var memoryEmbeddings = await EnsureMemoryEmbeddingsAsync(npc);
		var memLog = _systemConfig.LogMemory
			? (Action<string>)(s => { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine(s); Console.ResetColor(); })
			: null;
		var dynamicPersona = PersonaBuilder.Build(
			npc, _playerState?.Name, BuildWorld(), plainEmbedding, memoryEmbeddings, memLog, _playerState?.Appearance);
		var (systemPrompt, numKeep) = BuildSystemPrompt(npc, npcId, dynamicPersona, contextBlock, _pendingConstraint);

		// ── Response generation ───────────────────────────────────────────────
		PrintUnshownFlavourText(npcId);
		PrintNpcPrefix(displayName ?? npc.Name);

		// Prior turns go to the model as real chat messages, not text in the system prompt.
		var historyMessages = BuildTurnMessages(npc);
		// Count of PRIOR turns (before this one is appended to history below) — a clean 0-based
		// turn ordinal: 0 for a fresh conversation, resets when the history is cleared. Do NOT use
		// historyMessages.Count here: that's a message count (2 per turn plus a trailing world note),
		// so it climbs 1,3,5,... and convert's arc grouping (which expects 0,1,2,3) never matches.
		var turnIndex = _conversationTracker.GetConversationHistory(npcId).Count;

		string response;
		_lastReplyEnded = false;
		if (_systemConfig.UseSelfCritique)
		{
			// Generate + validate silently (thinking dots only), then type out ONLY the
			// accepted response. A rejected draft is never shown — no double responses.
			var validated = await GenerateValidatedAsync(
				npc.Name, dynamicPersona, contextBlock, systemPrompt, userQuery, numKeep, historyMessages);
			response = StripEndMarker(validated, out var endedByToken);
			_lastReplyEnded = endedByToken;
			await TypeOutAsync(response);
			Console.WriteLine();
		}
		else
		{
			// No critique — stream live as the NPC "speaks". GenerateResponseAsync suppresses
			// and strips the <END> control token and sets _lastReplyEnded.
			response = await GenerateResponseAsync(systemPrompt, userQuery, numKeep, withPacing: true, historyMessages);
		}

		// Involuntary close — the NPC is too angry or disgusted to keep talking.
		if (!_lastReplyEnded && (npc.EmotionalState.Anger >= 0.85f || npc.EmotionalState.Disgust >= 0.85f))
			_lastReplyEnded = true;

		// ── Order detection & session history ──────────────────────────────────
		// Fast, no LLM — must be in place before the next turn's prompt is built.
		DetectAndInjectOrder(npcId, userQuery, _gameState.CurrentLocation);
		_conversationTracker.AddConversationTurn(npcId, userQuery, response);

		// Capture the turn for fine-tuning. systemPrompt + userQuery → response is exactly
		// the SFT pair; the state snapshot lets the set be filtered/balanced later. `turnIndex`
		// (captured above, before this turn joined the history) is the clean 0-based turn ordinal
		// that lets the curator/convert reassemble a multi-turn arc from consecutive rows.
		_trainingLogger?.BufferTurn(
			_primaryModelName, npcId, npc.Name, systemPrompt, userQuery, response,
			BuildStateSnapshot(npc), turnIndex);

		// ── Post-turn memory work (background) ─────────────────────────────────
		// Memory extraction, scar-tissue compression and the NPC-state save run while the
		// player reads and types, so the prompt returns immediately. This is flushed before
		// the next interaction with this NPC (FlushPendingMemoryWorkAsync), so it is always
		// single-flight — if the model can't keep up, the player simply waits at the next
		// turn as before; nothing queues up.
		var dayForMemory = _gameState.CurrentDay;
		_pendingPostTurn[npcId] = Task.Run(async () =>
		{
			if (npc.Tier == 1 && _systemConfig.UseMemoryCreation && !_systemConfig.CollectionMode)
				await CreateConversationMemoriesAsync(npc, npcId, userQuery, response, dayForMemory);

			if (npc.Tier == 1 && _systemConfig.UseScarTissueCompression)
				await _npcMemoryManager.CompressMemoriesIfNeededAsync(
					npcId, _scarTissueCompressor, dayForMemory, _systemConfig.LogMemory);

			if (_systemConfig.PersistsNpcState)
				await _npcRegistry.SaveAsync();
		});

		// ── Time advance ──────────────────────────────────────────────────────
		// Estimate elapsed time from combined word count of this exchange
		var minutes = GameStateManager.WordCountToMinutes(userQuery + " " + response);

		// A day-wrap triggers DecayMemories, which iterates THIS NPC's memory lists on this
		// thread. The post-turn background task (scheduled just above) mutates those same
		// lists, so a wrapping advance could race its enumeration → "collection modified".
		// Only on a wrapping turn do we finish that work first; non-wrapping turns (the
		// common case) keep it backgrounded and return immediately, as before.
		var willWrapDay = _gameState.CurrentHour * 60 + _gameState.CurrentMinute + minutes >= 24 * 60;
		if (willWrapDay)
			await FlushPendingMemoryWorkAsync(npcId);

		await _gameState.AdvanceTimeAsync(minutes,
			_systemConfig.PersistsGameState);

		MaybeShowTimestamp();

		return response;
	}

	// ── Silence handling ─────────────────────────────────────────────────────
	// Full pipeline with the last NPC response as retrieval anchor.
	// The NPC decides based on persona + state whether to fill the silence or not.

	public async Task<string> HandleSilenceAsync(string npcId, string? displayName = null)
	{
		var npc = _npcRegistry.GetNpc(npcId)
			?? throw new InvalidOperationException($"NPC '{npcId}' not found.");

		await FlushPendingMemoryWorkAsync(npcId);

		// Use the last thing the NPC said as the retrieval query — they'll continue
		// from wherever the conversation was, or speak from their current state
		var history = _conversationTracker.GetConversationHistory(npcId);
		var retrievalQuery = history.Count > 0
			? history[^1].NpcResponse
			: $"{npc.Name} at {_gameState.CurrentLocation}";

		var queryEmbedding = await _embeddingService.GetEmbeddingAsync(
			retrievalQuery.ToLowerInvariant());

		var topics = ClassifyTopics(retrievalQuery);
		var retrievedChunks = await RetrieveAsync(retrievalQuery, queryEmbedding, topics);

		var contextBlock = retrievedChunks.Count > 0
			? BuildContextBlock(retrievedChunks)
			: string.Empty;

		var dynamicPersona = PersonaBuilder.Build(npc, _playerState?.Name, BuildWorld(), playerAppearance: _playerState?.Appearance);
		var (systemPrompt, numKeep) = BuildSilenceSystemPrompt(
			npc, npcId, dynamicPersona, contextBlock);

		PrintNpcPrefix(displayName ?? npc.Name);
		var response = await GenerateResponseAsync(
			systemPrompt, "[silence]", numKeep, withPacing: true, BuildTurnMessages(npc));
		Console.WriteLine();

		// Low-fidelity memory — the NPC noticed the player went quiet
		_npcMemoryManager.AddMemory(npcId, new NpcMemory
		{
			Id = Guid.NewGuid().ToString("N")[..8],
			Content = "The traveller went quiet without responding",
			Fidelity = 0.30f,
			InitialFidelity = 0.30f,
			DecayWeight = 0.8f,
			TraumaTagged = false,
			Timestamp = $"day-{_gameState.CurrentDay}"
		}, isPlayerMemory: true);

		_conversationTracker.AddConversationTurn(npcId, "[silence]", response);

		var minutes = GameStateManager.WordCountToMinutes(response);
		await _gameState.AdvanceTimeAsync(minutes,
			_systemConfig.PersistsGameState);

		MaybeShowTimestamp();

		return response;
	}

	// ── A/B comparison ──────────────────────────────────────────────────────────
	// Runs the same prompt through two LLMs and prints both responses. The primary
	// model's reply is threaded into session history (so successive `compare` calls
	// build context the way real play does — otherwise every compare reads as a cold
	// first contact and models greet on a loop). No long-term memory, no time advance,
	// no critique — diagnostic only.

	public async Task CompareAsync(
		string npcId,
		string userQuery,
		IReadOnlyList<(string label, ILlmService llm)> others,
		string primaryLabel)
	{
		var npc = _npcRegistry.GetNpc(npcId);
		if (npc == null) return;

		await FlushPendingMemoryWorkAsync(npcId);

		var queryEmbedding = await _embeddingService.GetEmbeddingAsync(userQuery.ToLowerInvariant());
		var topics = ClassifyTopics(userQuery);
		var retrievedChunks = await RetrieveAsync(userQuery, queryEmbedding, topics);
		var contextBlock = BuildContextBlock(retrievedChunks);
		var dynamicPersona = PersonaBuilder.Build(npc, _playerState?.Name, BuildWorld(), playerAppearance: _playerState?.Appearance);
		var (systemPrompt, numKeep) = BuildSystemPrompt(npc, npcId, dynamicPersona, contextBlock, null);
		var history = BuildTurnMessages(npc);

		// Same prompt for everyone: [A] is the primary model, then [B], [C], … one per
		// comparison model, each generated once.
		var primaryRaw = await StreamLabelledAsync("A", primaryLabel, _llm, systemPrompt, userQuery, numKeep, history, ConsoleColor.Cyan);

		var palette = new[] { ConsoleColor.Yellow, ConsoleColor.Green, ConsoleColor.Magenta, ConsoleColor.Blue, ConsoleColor.Red };
		for (int i = 0; i < others.Count; i++)
		{
			var letter = ((char)('B' + i)).ToString();
			await StreamLabelledAsync(letter, others[i].label, others[i].llm, systemPrompt, userQuery, numKeep, history, palette[i % palette.Length]);
		}

		// Thread the primary model's reply into session history so the next compare sees it.
		var primaryClean = StripWrappingQuotes(StripEndMarker(primaryRaw, out _));
		if (!string.IsNullOrWhiteSpace(primaryClean))
			_conversationTracker.AddConversationTurn(npcId, userQuery, primaryClean);
	}

	private static async Task<string> StreamLabelledAsync(
		string letter, string label, ILlmService llm,
		string systemPrompt, string userQuery, int numKeep,
		IReadOnlyList<ChatMessage>? history, ConsoleColor colour)
	{
		Console.ForegroundColor = colour;
		Console.WriteLine($"\n[{letter}] {label}");
		Console.ResetColor();
		var sb = new StringBuilder();
		await foreach (var token in llm.GenerateStreamAsync(systemPrompt, userQuery, numKeep, history))
		{
			Console.Write(token);
			sb.Append(token);
		}
		Console.WriteLine();
		return sb.ToString();
	}

	// ── Name reveal ──────────────────────────────────────────────────────────────
	// Called when the player gives their name during the intro. Generates the NPC's
	// contextual reaction, stores a permanent memory, and logs the turn.

	public async Task HandleNameRevealAsync(string npcId, string playerName, string? displayName = null)
	{
		var npc = _npcRegistry.GetNpc(npcId);
		if (npc == null || string.IsNullOrWhiteSpace(playerName)) return;

		await FlushPendingMemoryWorkAsync(npcId);

		// Permanent high-fidelity memory — the NPC will always know the player's name
		_npcMemoryManager.AddMemory(npcId, new NpcMemory
		{
			Id = Guid.NewGuid().ToString("N")[..8],
			Content = $"The traveller's name is {playerName}",
			Fidelity = 0.95f,
			InitialFidelity = 0.95f,
			DecayWeight = 2.5f,
			TraumaTagged = false,
			Timestamp = $"day-{_gameState.CurrentDay}"
		}, isPlayerMemory: true);

		var dynamicPersona = PersonaBuilder.Build(npc, playerName, BuildWorld(), playerAppearance: _playerState?.Appearance);
		var firstName = npc.Name.Split(' ')[0];

		// What just passed between them is supplied as chat turns (BuildHistoryMessages), so the
		// reaction fits the moment rather than being a generic greeting.
		var stateBlock = PersonaBuilder.BuildCurrentState(npc);
		var stateSection = !string.IsNullOrEmpty(stateBlock) ? $"\n\n{stateBlock}" : "";

		var systemPrompt =
			$"""
			You are {npc.Name}. {dynamicPersona}{stateSection}

			The traveller has just told you their name is {playerName}.
			Warmly welcome them and introduce yourself by name in return — you are {firstName}, so actually say it (for example, I'm {firstName}). React to the moment as it really is, drawing on what just passed between you — do not ask things you already know. You only met them this morning, so don't invent a longer shared history than that.
			Keep it to 1-2 sentences. Do not open with a bare filler word. No asterisks, no parentheses, no quotation marks, no action descriptions.
			""";

		var playerTurn = $"My name is {playerName}.";

		PrintNpcPrefix(displayName ?? npc.Name);
		var response = await GenerateResponseAsync(
			systemPrompt, playerTurn, systemPrompt.Length / 4, withPacing: true, BuildTurnMessages(npc));
		Console.WriteLine();

		_conversationTracker.AddConversationTurn(npcId, playerTurn, response);

		var minutes = GameStateManager.WordCountToMinutes(playerTurn + " " + response);
		await _gameState.AdvanceTimeAsync(minutes,
			_systemConfig.PersistsGameState);
	}

	private (string SystemPrompt, int NumKeep) BuildSilenceSystemPrompt(
		NpcState npc,
		string npcId,
		string dynamicPersona,
		string contextBlock)
	{
		var stablePrefix =
			$"""
			You are {npc.Name}. {dynamicPersona}

			The traveller has said nothing — they are silent.
			Respond exactly as your character and current emotional state would in this moment.
			You might fill the silence with your own thought, acknowledge it briefly, or simply
			let it sit. Do not ask a question. Keep it to 1-2 sentences at most.
			Do not use asterisks, parentheses, or action descriptions of any kind.
			""";

		var numKeep = stablePrefix.Length / 4;

		// History is passed separately as chat turns (BuildHistoryMessages), not embedded here.
		var contextSection = !string.IsNullOrEmpty(contextBlock)
			? $"\n\nContext:\n{contextBlock}"
			: "";

		// Current state goes last for recency, as in the main prompt.
		var stateBlock = PersonaBuilder.BuildCurrentState(npc);
		var stateSection = !string.IsNullOrEmpty(stateBlock) ? $"\n\n{stateBlock}" : "";

		return ($"{stablePrefix}{contextSection}{stateSection}", numKeep);
	}

	// ── Classification ────────────────────────────────────────────────────────

	private async Task<QueryComplexity> ClassifyComplexityAsync(string userQuery, float[] queryEmbedding)
	{
		var complexity = await _complexityClassifier.ClassifyAsync(userQuery, queryEmbedding);
		if (_systemConfig.LogComplexity)
			Console.WriteLine($"[complexity: {complexity}]");
		return complexity;
	}

	private List<Topic> ClassifyTopics(string userQuery)
	{
		var topics = _topicClassifier.Classify(userQuery);
		if (_systemConfig.LogTopics)
			Console.WriteLine($"[topics: {string.Join(", ", topics)}]");
		return topics;
	}

	// ── Embedding ─────────────────────────────────────────────────────────────

	// ── Retrieval ─────────────────────────────────────────────────────────────
	// Hybrid search (vector + BM25) → RRF fusion → rerank → MMR diversify → compress

	private async Task<List<DocumentChunk>> RetrieveAsync(
		string userQuery,
		float[] queryEmbedding,
		List<Topic> topics)
	{
		// Topic filtering — top 3 topics cast wide enough net for multi-dimensional
		// queries while excluding clearly irrelevant chunks
		var topTopics = topics.Take(3).ToList();
		var vectorResults = _vectorLoreData.Search(queryEmbedding, _topNumChunk * 2, topTopics);

		// Relevance floor — cut weak chunks here, before RRF fusion discards the score. Fusion is
		// rank-based, so an off-topic chunk that's merely the best of a bad pool would otherwise
		// rank #1 and get injected. The floor is relative to this query's own score distribution
		// rather than an absolute cosine, because the embedding model packs every similarity into
		// a narrow band that shifts with the corpus. BM25 uses a different score scale entirely, so
		// the floor is vector-only; a strong lexical hit is relevant by construction.
		var floor = _systemConfig.RelevanceSigma > 0f
			? _vectorLoreData.LastScoreMean + _systemConfig.RelevanceSigma * _vectorLoreData.LastScoreStdDev
			: float.MinValue;

		var flooredResults = _systemConfig.RelevanceSigma > 0f
			? vectorResults.Where(r => r.Score >= floor).ToList()
			: vectorResults;

		// Never starve a genuine lore question. vectorResults is already ordered by score, so this
		// restores the best few when the floor was too aggressive for this query's distribution.
		var rescued = 0;
		if (_systemConfig.RelevanceMinKeep > 0 && flooredResults.Count < _systemConfig.RelevanceMinKeep)
		{
			var target = Math.Min(_systemConfig.RelevanceMinKeep, vectorResults.Count);
			rescued = target - flooredResults.Count;
			flooredResults = vectorResults.Take(target).ToList();
		}

		if (_systemConfig.LogRelevance && vectorResults.Count > 0)
			Console.WriteLine(
				$"[relevance: floor={floor:0.000} (mean={_vectorLoreData.LastScoreMean:0.000} " +
				$"+{_systemConfig.RelevanceSigma:0.0}σ×{_vectorLoreData.LastScoreStdDev:0.000}) " +
				$"top={vectorResults[0].Score:0.000} low={vectorResults[^1].Score:0.000} " +
				$"kept={flooredResults.Count}/{vectorResults.Count}" +
				(rescued > 0 ? $" (+{rescued} rescued by minkeep)" : "") + "]");

		var bm25Results = _bm25.Search(userQuery, _topNumChunk * 2, topics: topTopics);

		// Combine vector and BM25 results with RRF fusion
		var fusedChunks = RRFFusion.Fuse(flooredResults, bm25Results, _topNumChunk * 2);

		if (fusedChunks.Count == 0)
			return fusedChunks;

		var rerankedChunks = _systemConfig.UseReranker
			? await _reranker.RerankAsync(userQuery, fusedChunks)
			: fusedChunks;

		var diverseChunks = _systemConfig.UseMMR
			? _mmr.Select(queryEmbedding, rerankedChunks, _topNumChunk)
			: rerankedChunks.Take(_topNumChunk).ToList();

		return _systemConfig.UseChunkCompressor
			? _chunkCompressor.Compress(userQuery, diverseChunks)
			: diverseChunks;
	}

	// ── Prompt construction ───────────────────────────────────────────────────

	private static string BuildContextBlock(List<DocumentChunk> chunks)
	{
		return string.Join(
			"\n\n---\n\n",
			chunks.Select((c, i) => $"[Source {i + 1}: {c.SourceTxtFile}]\n{c.ChunkContent}")
		);
	}

	private (string SystemPrompt, int NumKeep) BuildSystemPrompt(
		NpcState npc,
		string npcId,
		string dynamicPersona,
		string contextBlock,
		string? claimConstraint = null)
	{
		// Stable prefix — static instructions + persona base + world/episodic memories.
		// Its length sets num_keep so the KV cache protects it from context truncation.
		// Rule of thumb: 1 token ≈ 4 characters

		// The dialogue/action rule flips with UseStageDirections: allow sparing *asterisk*
		// beats, or strict spoken-only.
		var dialogueRule = _systemConfig.UseStageDirections
			? "- MOSTLY SPOKEN DIALOGUE. You may add brief physical actions wrapped in *asterisks* (e.g. *glances up*, *doesn't look up*, *pauses*) — short and sparing, a beat only when it earns its place, never every line. Asterisks wrap ONLY a physical action your body performs. NEVER put spoken words, an aside, or an unspoken thought inside asterisks, and NEVER wrap a whole sentence in them. NEVER use asterisks to emphasise a word (write 'you', not '*you*'). If it can be heard aloud, it is plain text OUTSIDE the asterisks. The action must fit WHO and WHERE you are; do not borrow a beat that belongs to someone else's setting. Write these WITHOUT a subject pronoun — never 'I glance up' or 'you glance up'; 'you' means the traveller everywhere else in your speech, never yourself. No parentheses, no angle brackets, no quotation marks around your speech."
			: "- STRICTLY DIALOGUE ONLY. Output only the words you say aloud — never describe or narrate what you do. No asterisks, no parentheses, no angle brackets, no quotation marks, no stage directions, no action or emotion labels.";

		var stablePrefix = npc.Nonverbal
			? $"""
				You have no mouth. You cannot speak, whisper, or make any sound — not ever, under any circumstances.
				You communicate ONLY through your eyes: where your gaze moves, what it settles on, how your eyes widen, narrow, glisten, or close. Convey everything through these eye movements, written in plain prose — never spoken words, never dialogue, never sound. Do not use asterisks or parentheses.
				Do not invent facts, names, or events. Keep it to one or two sentences describing only your eyes and gaze.

				You are {npc.Name}. {dynamicPersona}
				"""
			: $"""
				[CRITICAL FORMATTING RULES]
				- SPEAK ONLY IN THE FIRST PERSON AS {npc.Name}.
				{dialogueRule}
				- Keep your response to 2-4 sentences — fewer when your mood or state calls for it.

				[WHAT YOU KNOW]
				- Speak freely as yourself: your own life, work, trade, opinions, the people and places you actually know, and ordinary common sense are all yours to draw on. You do not need a source to talk about your own world.
				- The Context passages below are background reference, NOT your personal knowledge. Draw on them only for things someone in your position would genuinely have reason to know. Do not recite facts — names, routes, distant events, lore — that a person like you, living where you live, would have no way of knowing.
				- When asked about something you wouldn't know, say so in character: deflect, admit ignorance, or wave it off. Never invent specific names, dates, numbers, or events to fill a gap.
				- If you have no recorded memory of this traveller, treat them as a stranger. Do not invent a shared past or pretend to recognise them.

				[VOICE]
				- Write ONLY your own spoken reply as {npc.Name}. Never write the traveller's words, actions, or thoughts; never add a "You:" turn or narrate what the traveller does. Stop the instant your own reply is finished.
				- Real, natural speech. Get to the point. No monologues, no formal essay-speak, no forced exposition.
				- Vary your opening words. Do not start successive replies with the same word or filler. Use dialect sparingly.
				- You are NOT a helpful assistant. Do not flatter, do not agree reflexively, do not offer help unprompted. Be curt, bored, wary, or unwilling when that is truer to who you are and how you feel right now.

				[ENDING]
				- Do not reflexively end with a question. A flat statement or a refusal is often better.
				- If you naturally want to dismiss the traveller, walk away, or end the interaction, finish your final spoken sentence, hit enter, and place the exact tag <END> on its own line. Never explain or mention this tag.
				- But if you ask the traveller a question, you are inviting an answer — do NOT end the conversation that turn. Only use <END> when you are genuinely finished and expect no reply.

				You are {npc.Name}. {dynamicPersona}
				""";

		var numKeep = stablePrefix.Length / 4;

		// Conversation history is NOT embedded here — it's passed separately as real chat
		// turns (see BuildHistoryMessages), so the model doesn't copy prior replies back as
		// if continuing text. Only persona/state/context/working-memory live in the system.
		var workingMemory = _workingMemoryManager.GetWorkingMemory(npcId);
		var workingMemoryBlock = workingMemory.Count > 0
			? "\n\nWhat you are aware of right now:\n" +
			  string.Join("\n", workingMemory.Select(m => $"- {m.Content}"))
			: "";

		var constraintBlock = !string.IsNullOrEmpty(claimConstraint)
			? $"\n\n[CONTEXT: {claimConstraint}]"
			: "";

		// Current emotional/physical/relationship state goes LAST — recency makes small
		// models actually act on it, instead of losing it in the middle of the prompt.
		var stateBlock = PersonaBuilder.BuildCurrentState(npc);
		var stateSection = !string.IsNullOrEmpty(stateBlock) ? $"\n\n{stateBlock}" : "";

		var systemPrompt =
			$"""
			{stablePrefix}
			{workingMemoryBlock}
			{constraintBlock}

			Context:
			{contextBlock}
			{stateSection}
			""";

		return (systemPrompt, numKeep);
	}

	// Prior conversation turns as structured chat messages, passed alongside the system prompt
	// instead of embedded as text — which is what stops weaker models echoing earlier replies
	// back verbatim. A trailing "right now" system note is appended LAST so the current hour
	// and mood are the freshest thing the model reads: moving history out of the system prompt
	// pushed the time line and state block back, costing them their recency. The full state
	// block still lives in the system prompt too, so it lands in the logged training input.
	private List<ChatMessage> BuildTurnMessages(NpcState npc)
	{
		var msgs = new List<ChatMessage>();
		foreach (var t in _conversationTracker.GetConversationHistory(npc.Id))
		{
			msgs.Add(new ChatMessage("user", t.PlayerMessage));
			msgs.Add(new ChatMessage("assistant", t.NpcResponse));
		}

		var w = BuildWorld();
		msgs.Add(new ChatMessage("system",
			$"[RIGHT NOW: it is {w.TimeLabel} ({w.Hour:D2}:{w.Minute:D2}) on {w.DayOfWeek}, {w.Season}; {w.Weather}. " +
			"Stay anchored to this hour, and let how you feel right now (described above) drive your tone and length.]"));
		return msgs;
	}

	// ── Response generation ───────────────────────────────────────────────────

	// Show the in-game time only when the clock has crossed into a new half-hour, instead of
	// after every single exchange — keeps a long conversation from being peppered with stamps.
	private int _lastShownTimeBucket = -1;
	private void MaybeShowTimestamp()
	{
		if (!_outputConfig.ShowTimeStamp) return;
		int bucket = (((_gameState.CurrentDay - 1) * 24 + _gameState.CurrentHour) * 60 + _gameState.CurrentMinute) / 30;
		if (bucket == _lastShownTimeBucket) return;
		_lastShownTimeBucket = bucket;
		Console.WriteLine($"[day {_gameState.CurrentDay} {_gameState.CurrentHour:D2}:{_gameState.CurrentMinute:D2} — {WorldContext.WeatherFromDay(_gameState.CurrentDay)}]");
	}

	// Set after each generation: did the NPC end the conversation (via the <END> control token,
	// or an anger/disgust threshold)? Read by the game loop right after QueryAsync.
	private bool _lastReplyEnded;
	public bool LastReplyEndedConversation => _lastReplyEnded;

	// Control tokens an NPC can use to end a conversation. The prompt instructs <END>;
	// [END] is a fallback the smaller models sometimes emit instead. Both the buffered and
	// the live-streaming paths recognise every marker here, so behaviour can't diverge.
	private static readonly string[] EndMarks = { "<END>", "[END]" };

	// Straight and curly quote characters a model might wrap a line in.
	private static readonly char[] QuoteChars = { '"', '“', '”', '\'', '‘', '’' };
	private static bool IsQuote(char c) => Array.IndexOf(QuoteChars, c) >= 0;

	// Drop a single matching pair of wrapping quotes around the whole line — models often
	// quote-wrap "spoken" dialogue despite the formatting rules. Only strips when BOTH ends
	// are quotes, so legitimate internal quotes are never touched.
	private static string StripWrappingQuotes(string text)
	{
		if (string.IsNullOrEmpty(text)) return text;
		var trimmed = text.Trim();
		if (trimmed.Length >= 2 && IsQuote(trimmed[0]) && IsQuote(trimmed[^1]))
			return trimmed[1..^1].Trim();
		return text;
	}

	// Remove stage directions an NPC shouldn't emit — *…*, (…), <…> spans (the <END> control
	// token is already stripped before this runs). A trailing unclosed opener drops the rest.
	private static string StripStageDirections(string text, bool keepAsterisks)
	{
		if (string.IsNullOrEmpty(text)) return text;
		var sb = new StringBuilder(text.Length);
		int i = 0;
		while (i < text.Length)
		{
			char c = text[i];
			char? close = c switch { '*' when !keepAsterisks => '*', '(' => ')', '<' => '>', _ => (char?)null };
			if (close != null)
			{
				int end = text.IndexOf(close.Value, i + 1);
				if (end < 0) break;       // unclosed trailing action — drop the remainder
				i = end + 1;              // skip the whole span
				continue;
			}
			sb.Append(c);
			i++;
		}
		var cleaned = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "[ \\t]{2,}", " ");
		return cleaned.Trim();
	}

	// Write text to the console, dimming any *asterisk* action spans so physical beats read
	// as distinct from speech. Used by the non-streaming path; the streaming path keeps the
	// same state across chunks via its local Emit.
	private static void WriteActionColored(string text, bool enabled)
	{
		if (!enabled) { Console.Write(text); return; }
		bool inAction = false;
		foreach (char ch in text)
		{
			if (ch == '*')
			{
				if (!inAction) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write('*'); inAction = true; }
				else { Console.Write('*'); Console.ResetColor(); inAction = false; }
			}
			else Console.Write(ch);
		}
		if (inAction) Console.ResetColor();
	}

	// The portion of accumulated streaming text safe to show now: closed stage-direction spans
	// removed, truncated before any still-open delimiter or partial end-token tail, and a single
	// leading wrapping quote dropped. Prefix-stable as more arrives, so the caller prints only
	// the delta against how much it has already shown.
	private static string RenderableSoFar(string s, bool keepAsterisks)
	{
		var sb = new StringBuilder(s.Length);
		int i = 0;
		while (i < s.Length)
		{
			char c = s[i];
			char? close = c switch { '*' when !keepAsterisks => '*', '(' => ')', '<' => '>', _ => (char?)null };
			if (close != null)
			{
				int end = s.IndexOf(close.Value, i + 1);
				if (end < 0) break;       // delimiter still open — hold here, it may close later
				i = end + 1;
				continue;
			}
			sb.Append(c);
			i++;
		}
		var cleaned = sb.ToString();

		// Hold back a partial end-token tail ('[', '[EN', …); '<' partials are already held above.
		cleaned = cleaned[..(cleaned.Length - TrailingEndPrefixLen(cleaned))];

		// Drop a single leading wrapping quote.
		int k = 0;
		while (k < cleaned.Length && char.IsWhiteSpace(cleaned[k])) k++;
		if (k < cleaned.Length && IsQuote(cleaned[k])) cleaned = cleaned.Remove(k, 1);

		return cleaned;
	}

	// Remove any end control token from a buffered response (non-streaming paths).
	private static string StripEndMarker(string text, out bool ended)
	{
		ended = false;
		if (string.IsNullOrEmpty(text)) return text;
		foreach (var mark in EndMarks)
		{
			int idx = text.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
			if (idx >= 0) { ended = true; text = text.Remove(idx, mark.Length); }
		}
		return text.TrimEnd();
	}

	// Earliest index of any end marker in s, or -1 if none is present.
	private static int IndexOfEndMark(string s)
	{
		int best = -1;
		foreach (var mark in EndMarks)
		{
			int idx = s.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
			if (idx >= 0 && (best < 0 || idx < best)) best = idx;
		}
		return best;
	}

	// Length of the longest suffix of s that is a proper prefix of mark — how much of a possible
	// end-token tail to hold back from the live stream until the next token disambiguates it.
	private static int TrailingPrefixLen(string s, string mark)
	{
		int max = Math.Min(mark.Length - 1, s.Length);
		for (int len = max; len > 0; len--)
			if (string.Compare(s, s.Length - len, mark, 0, len, StringComparison.OrdinalIgnoreCase) == 0)
				return len;
		return 0;
	}

	// Longest holdback across every end marker — so a partial tail of any marker is held.
	private static int TrailingEndPrefixLen(string s)
	{
		int max = 0;
		foreach (var mark in EndMarks)
			max = Math.Max(max, TrailingPrefixLen(s, mark));
		return max;
	}

	// Generate a fresh, in-character opening line for an NPC who speaks first — seeded by their
	// authored opening_line and coloured by mood, time and weather (via the dynamic persona),
	// so it reads differently each time rather than a canned greeting.
	public async Task<string?> GenerateOpenerAsync(string npcId, string? displayName = null)
	{
		var npc = _npcRegistry.GetNpc(npcId);
		if (npc == null) return null;

		var dynamicPersona = PersonaBuilder.Build(npc, _playerState?.Name, BuildWorld(), playerAppearance: _playerState?.Appearance);
		var stateBlock = PersonaBuilder.BuildCurrentState(npc);
		var stateSection = !string.IsNullOrEmpty(stateBlock) ? $"\n\n{stateBlock}" : "";
		var systemPrompt =
			$"You are {npc.Name}. {dynamicPersona}\n\n" +
			"The traveller has just come up to you, and you are the one to speak first. In one or two " +
			"sentences, open the conversation in character — a greeting, a question, a challenge, a wary " +
			"remark, whatever fits who you are and your mood right now; let the hour or weather colour it " +
			"only if it feels natural. Dialogue only: no asterisks, no quotation marks, no stage directions." +
			stateSection;

		// Present it exactly like any other NPC line: the name prefix (??? while the NPC is
		// still unknown) followed by the live "thinking" dots while the model works, then the
		// streamed opener — so an NPC speaking first looks the same as one replying.
		PrintNpcPrefix(displayName ?? npc.Name);
		var opener = await GenerateResponseAsync(systemPrompt, "(The traveller approaches.)", systemPrompt.Length / 4, withPacing: true);

		// Record the opener as the first turn so the NPC's next reply builds on it (and doesn't
		// greet again). The synthetic player side reads naturally in the history block.
		if (!string.IsNullOrWhiteSpace(opener))
			_conversationTracker.AddConversationTurn(npcId, "(approaches you)", opener);

		return opener;
	}

	private async Task<string> GenerateResponseAsync(
		string systemPrompt,
		string userQuery,
		int numKeep,
		bool withPacing,
		IReadOnlyList<ChatMessage>? history = null)
	{
		if (!_outputConfig.UseStreaming)
		{
			var generated = await _llm.GenerateAsync(systemPrompt, userQuery, numKeep, history);
			var cleanedNS = StripStageDirections(StripWrappingQuotes(StripEndMarker(generated, out var endedNS)), _systemConfig.UseStageDirections);
			_lastReplyEnded = endedNS;
			WriteActionColored(cleanedNS, _systemConfig.UseStageDirections);
			Console.WriteLine();
			return cleanedNS;
		}

		var fullResponse = new StringBuilder();
		int printed = 0;   // chars of the cleaned/renderable output already shown
		bool ended = false;
		bool inAction = false;  // tracks *…* spans across stream chunks so they render dim

		// Print a chunk, dimming text inside *asterisk* action spans when stage directions are on.
		void Emit(string chunk)
		{
			if (!_systemConfig.UseStageDirections) { Console.Write(chunk); return; }
			foreach (char ch in chunk)
			{
				if (ch == '*')
				{
					if (!inAction) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write('*'); inAction = true; }
					else { Console.Write('*'); Console.ResetColor(); inAction = false; }
				}
				else Console.Write(ch);
			}
		}

		// Animated "thinking" dots while we wait for the first token — the gap before
		// generation starts (model load / prompt eval) can be long, so this reassures
		// the player the NPC is working. Cleared the moment real output arrives.
		using var thinkingCts = new CancellationTokenSource();
		var thinking = ShowThinkingAsync(thinkingCts.Token);
		var firstToken = true;

		await foreach (var token in _llm.GenerateStreamAsync(systemPrompt, userQuery, numKeep, history))
		{
			if (firstToken)
			{
				thinkingCts.Cancel();
				try { await thinking; } catch { /* cancellation */ }
				firstToken = false;
			}

			fullResponse.Append(token);
			var s = fullResponse.ToString();

			// Stop at an end control token: show what precedes it (cleaned), then break.
			int markIdx = IndexOfEndMark(s);
			if (markIdx >= 0)
			{
				var pre = RenderableSoFar(s[..markIdx], _systemConfig.UseStageDirections);
				if (pre.Length > printed) Emit(pre[printed..]);
				printed = pre.Length;
				ended = true;
				break;
			}

			// Show the renderable prefix — stage directions removed, wrapping quote dropped,
			// partial end-token / unclosed action held back — pacing only on new output.
			var disp = RenderableSoFar(s, _systemConfig.UseStageDirections);
			if (disp.Length > printed)
			{
				Emit(disp[printed..]);
				printed = disp.Length;
				if (withPacing)
				{
					var delay = GetPacingDelay(fullResponse);
					if (delay > 0) await Task.Delay(delay);
				}
			}
		}

		// No tokens ever arrived — still tear down the indicator
		if (firstToken)
		{
			thinkingCts.Cancel();
			try { await thinking; } catch { /* cancellation */ }
		}

		// Flush whatever's left renderable when the stream ended without an end-token.
		if (!ended)
		{
			var disp = RenderableSoFar(fullResponse.ToString(), _systemConfig.UseStageDirections);
			if (disp.Length > printed) Emit(disp[printed..]);
		}

		if (inAction) Console.ResetColor();
		Console.WriteLine();

		_lastReplyEnded = ended;
		var full = fullResponse.ToString();
		int cut = IndexOfEndMark(full);
		var body = cut >= 0 ? full[..cut] : full;
		return StripStageDirections(StripWrappingQuotes(body), _systemConfig.UseStageDirections);
	}

	// Cycling "." → ".." → "..." after the NPC prefix until the first token lands.
	// On cancel (first token, or no output), the finally erases whatever dots remain so
	// the cursor sits cleanly after "Name: " ready for the real response.
	private static async Task ShowThinkingAsync(CancellationToken ct)
	{
		var shown = 0;

		void Erase()
		{
			if (shown <= 0) return;
			Console.Write(new string('\b', shown));
			Console.Write(new string(' ', shown));
			Console.Write(new string('\b', shown));
			shown = 0;
		}

		// Hide the blinking caret while the dots animate (guarded — throws if redirected)
		try { Console.CursorVisible = false; } catch { /* not a console */ }

		try
		{
			var n = 0;
			while (true)
			{
				Erase();
				n = n % 3 + 1;
				Console.ForegroundColor = ConsoleColor.DarkGray;
				Console.Write(new string('.', n));
				Console.ResetColor();
				shown = n;
				await Task.Delay(400, ct);
			}
		}
		catch (TaskCanceledException) { }
		finally
		{
			Erase();
			Console.ResetColor();
			try { Console.CursorVisible = true; } catch { /* not a console */ }
		}
	}

	// Punctuation-aware delays simulate natural speech rhythm during streaming.
	// Inspects the accumulated response tail rather than a single token, so an ellipsis
	// is detected even when its dots arrive as separate tokens.
	private int GetPacingDelay(StringBuilder response)
	{
		var delay = _outputConfig.StreamingTokenDelayMs;

		// Find the last non-whitespace character
		int i = response.Length - 1;
		while (i >= 0 && char.IsWhiteSpace(response[i])) i--;
		if (i < 0) return delay;

		var last = response[i];

		var isEllipsis = last == '…' ||
			(last == '.' && i >= 2 && response[i - 1] == '.' && response[i - 2] == '.');

		if (isEllipsis)
			delay = _outputConfig.StreamingPauseEllipsisMs;
		else if (last is '.' or '?' or '!')
			delay = _outputConfig.StreamingPauseLongMs;
		else if (last is ',' or ';' or ':')
			delay = _outputConfig.StreamingPauseShortMs;

		return delay;
	}

	private static void PrintNpcPrefix(string displayName)
	{
		Console.ForegroundColor = ConsoleColor.DarkYellow;
		Console.Write($"\n{displayName}: ");
		Console.ResetColor();
	}

	// Print any working memory flavour text that hasn't been shown yet
	private void PrintUnshownFlavourText(string npcId)
	{
		foreach (var wm in _workingMemoryManager.GetWorkingMemory(npcId)
			.Where(m => !string.IsNullOrEmpty(m.FlavourText) && !m.FlavourPrinted))
		{
			ConsoleEx.Dim($"\n{wm.FlavourText}\n");
			wm.FlavourPrinted = true;
		}
	}

	// Detect simple food/drink orders and add them as working memory so the NPC
	// remembers what was ordered even when the model fails to track it in context.
	private static readonly string[] OrderPrefixes =
		["i'll take", "i'll have", "give me", "can i get", "i want", "i'd like"];

	// Orders are only taken where food/drink is served. Without this gate, "I want to
	// know about the Hevraths" becomes an order for "To know about the Hevraths".
	private static readonly HashSet<string> OrderVenues = new(StringComparer.OrdinalIgnoreCase)
	{
		"sleeping_hound_bar", "sleeping_hound_kitchen", "worn_lintel", "bluebells_garden", "carvallen_market"
	};

	// Food/drink vocabulary (drawn from Ath_Food_And_Drink lore). The ordered text must
	// contain one of these whole words, so non-order phrasings are never captured.
	private static readonly string[] OrderKeywords =
	{
		"ale", "wine", "beer", "mead", "cider", "pint", "cup", "mug", "drink", "water", "milk", "tea",
		"porridge", "pottage", "stew", "soup", "broth", "bread", "rye", "flatbread", "loaf",
		"fish", "mackerel", "mutton", "goat", "meat", "game", "rabbit", "cheese", "bean", "beans",
		"mash", "pickle", "kelp", "compote", "preserve", "sauce", "berry", "meal", "food", "plate", "dish", "pot"
	};

	private void DetectAndInjectOrder(string npcId, string playerMessage, string locationId)
	{
		// Only taverns/kitchens take orders
		if (!OrderVenues.Contains(locationId)) return;

		var lower = playerMessage.ToLowerInvariant();
		foreach (var prefix in OrderPrefixes)
		{
			var idx = lower.IndexOf(prefix, StringComparison.Ordinal);
			if (idx < 0) continue;

			var after = playerMessage[(idx + prefix.Length)..].Trim().TrimEnd('.', '!', '?', ',');
			if (after.Length < 2 || after.Length > 60) continue;

			// Require an actual food/drink word, else this isn't really an order
			if (!OrderKeywords.Any(k => StringUtils.IsWholeWordMatch(after.ToLowerInvariant(), k)))
				continue;

			// Capitalise first letter for cleaner display
			var item = char.ToUpper(after[0]) + after[1..];
			var key = $"[order] {item}";

			// Avoid duplicate order entries
			var existing = _workingMemoryManager.GetWorkingMemory(npcId);
			if (existing.Any(m => m.Content.Equals(key, StringComparison.OrdinalIgnoreCase)))
				return;

			_workingMemoryManager.AddAuthoredWorkingMemory(
				npcId,
				$"The traveller has ordered: {item}",
				flavourText: string.Empty,
				isSignificant: false);
			return;
		}
	}

	// ── Self-RAG critique ─────────────────────────────────────────────────────
	// Post-generation quality gate. Generates and validates WITHOUT printing — only the
	// accepted response is shown (by the caller), so a rejected draft never reaches the
	// player. Thinking dots cover the wait; on failure it regenerates with the critique
	// reason appended as a constraint, up to MaxAttempts.

	private async Task<string> GenerateValidatedAsync(
		string npcName,
		string dynamicPersona,
		string contextBlock,
		string systemPrompt,
		string userQuery,
		int numKeep,
		IReadOnlyList<ChatMessage>? history = null)
	{
		const int MaxAttempts = 2;

		using var dotsCts = new CancellationTokenSource();
		var dots = ShowThinkingAsync(dotsCts.Token);

		var prompt = systemPrompt;
		var response = string.Empty;
		try
		{
			for (int attempt = 0; attempt < MaxAttempts; attempt++)
			{
				response = await GenerateSilentAsync(prompt, userQuery, numKeep, history);

				var critique = await _selfCritiqueService.CritiqueAsync(
					npcName, dynamicPersona, response, contextBlock);

				if (critique.Passed)
				{
					LogCritique($"[critique] passed: {critique.Reason}");
					break;
				}

				if (attempt + 1 >= MaxAttempts)
				{
					LogCritique($"[critique] failed: {critique.Reason} — max attempts reached, using last");
					break;
				}

				LogCritique($"[critique] failed: {critique.Reason} — regenerating (attempt {attempt + 1})");
				// B10 — append failure reason as a hard constraint so the model addresses it
				prompt = systemPrompt +
					$"\n\n[CONSTRAINT: Your previous response was rejected because: {critique.Reason}. Do not repeat this mistake.]";
			}
		}
		finally
		{
			dotsCts.Cancel();
			try { await dots; } catch { /* cancellation */ }
		}

		return response;
	}

	// Buffer a full generation without writing anything to the console.
	private async Task<string> GenerateSilentAsync(string systemPrompt, string userQuery, int numKeep,
		IReadOnlyList<ChatMessage>? history = null)
	{
		if (!_outputConfig.UseStreaming)
			return await _llm.GenerateAsync(systemPrompt, userQuery, numKeep, history);

		var sb = new StringBuilder();
		await foreach (var token in _llm.GenerateStreamAsync(systemPrompt, userQuery, numKeep, history))
			sb.Append(token);
		return sb.ToString();
	}

	// Replay an already-generated response with the same speech-pacing as live streaming,
	// so a validated (held) response still "types out" rather than appearing all at once.
	private async Task TypeOutAsync(string text)
	{
		if (!_outputConfig.UseStreaming)
		{
			Console.Write(text);
			return;
		}

		var sb = new StringBuilder();
		var words = text.Split(' ');
		for (int i = 0; i < words.Length; i++)
		{
			var token = i < words.Length - 1 ? words[i] + " " : words[i];
			Console.Write(token);
			sb.Append(token);

			var delay = GetPacingDelay(sb);
			if (delay > 0)
				await Task.Delay(delay);
		}
	}

	// ── Claim detection handler ───────────────────────────────────────────────

	private async Task HandleClaimDetectionAsync(
		NpcState npc,
		string npcId,
		ClaimDetectionResult result)
	{
		var r = npc.PlayerRelationship;
		var e = npc.EmotionalState;

		// Gullibility gate — credulous NPCs sometimes miss contradictions
		if (result.Type == "contradiction" &&
			Random.Shared.NextSingle() < r.Gullibility)
		{
			LogClaim("[claim] contradiction missed — gullibility gate passed");
			return;
		}

		LogClaim($"[claim] {result.Type} detected (severity {result.Severity:F2}): {result.Description}");

		switch (result.Type)
		{
			case "contradiction":
				// Raise suspicion; severity scaled by inverse gullibility
				e.Suspicion = Math.Clamp(e.Suspicion + result.Severity * (1f - r.Gullibility) * 0.15f, 0f, 1f);

				// Reclassify the conflicting memory → move to SuspectMemories
				if (result.ConflictingMemoryId != null)
				{
					var allMemories = npc.OrphanMemories.Concat(npc.WorldMemories).ToList();
					var conflicting = allMemories.FirstOrDefault(m => m.Id == result.ConflictingMemoryId);
					if (conflicting != null)
					{
						conflicting.Nature = Domain.Npc.MemoryNature.Claim;
						_npcMemoryManager.MoveToSuspect(npcId, conflicting);
						LogClaim($"[claim] memory '{conflicting.Content}' moved to SuspectMemories");
					}
				}

				_pendingConstraint = result.Severity > 0.6f
					? $"The traveller just said something that directly contradicts what you know. You are noticeably more guarded. You may challenge them, ask them to clarify, or simply be cooler in your response."
					: $"The traveller said something that doesn't quite match what you remember. You are quietly sceptical but haven't called it out yet.";
				break;

			case "accusation":
				// Anger and trust drift based on guilt — a guilty NPC gets rattled, not angry
				var guiltFactor = e.Guilt;
				e.Anger   = Math.Clamp(e.Anger   + result.Severity * (1f - guiltFactor) * 0.2f, 0f, 1f);
				r.TrustPlayer = Math.Clamp(r.TrustPlayer - result.Severity * 0.15f, 0f, 1f);

				var shouldConfront = e.Anger > 0.45f || r.TrustPlayer < 0.25f || result.Severity > 0.7f;
				_pendingConstraint = shouldConfront
					? $"The traveller has just accused you of something. You are defensive and want to address it directly — push back, deny it, or demand an explanation."
					: $"The traveller has implied something unflattering about you. You noticed it but chose not to make a scene — let a hint of displeasure colour your response without confronting them outright.";
				break;

			case "joke":
				// Mildly raise erratic behaviour — someone claiming to be Emperor is odd
				r.PlayerErraticBehaviour = Math.Clamp(r.PlayerErraticBehaviour + 0.05f, 0f, 1f);
				_pendingConstraint = $"The traveller said something clearly absurd or boastful. You may find it faintly amusing, dismiss it gently, or simply not take it seriously.";
				break;
		}

		await Task.CompletedTask; // reserved for future async ops (e.g. async stat writes)
	}

	private void LogClaim(string message)
	{
		if (!_systemConfig.LogClaimDetection) return;
		Console.ForegroundColor = ConsoleColor.DarkCyan;
		Console.WriteLine(message);
		Console.ResetColor();
	}

	private void LogCritique(string message)
	{
		if (!_systemConfig.LogCritique) return;

		ConsoleEx.Dim(message);
	}

	// ── Background post-turn work ──────────────────────────────────────────────
	// Awaits and clears any in-flight memory bookkeeping for this NPC. Called before
	// anything reads or mutates the NPC's memory on the main thread, keeping background
	// work single-flight and consistent, and bounding it to one task per NPC.
	public async Task FlushPendingMemoryWorkAsync(string npcId)
	{
		if (!_pendingPostTurn.TryGetValue(npcId, out var task)) return;
		_pendingPostTurn.Remove(npcId);
		try { await task; }
		catch (Exception ex)
		{
			// This background task does the NPC-state SaveAsync, so a swallowed failure here
			// means silently lost persistence. Always surface it — not gated on LogMemory,
			// unlike the routine memory-bookkeeping traces.
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			Console.WriteLine($"[memory] background work failed for {npcId}: {ex.Message}");
			Console.ResetColor();
		}
	}

	// ── Training-data capture ──────────────────────────────────────────────────

	// Non-zero state values for this NPC — compact metadata so the training set can be
	// filtered or balanced by state (e.g. over-sampling anger turns).
	private static Dictionary<string, float> BuildStateSnapshot(NpcState npc)
	{
		var e = npc.EmotionalState;
		var p = npc.PhysicalState;
		var r = npc.PlayerRelationship;
		var d = new Dictionary<string, float>();
		void Add(string k, float v) { if (v > 0.001f) d[k] = MathF.Round(v, 2); }

		Add("fear", e.Fear); Add("grief", e.Grief); Add("hope", e.Hope); Add("suspicion", e.Suspicion);
		Add("anger", e.Anger); Add("anxiety", e.Anxiety); Add("disgust", e.Disgust); Add("guilt", e.Guilt);
		Add("exhaustion", p.Exhaustion); Add("pain", p.Pain); Add("intoxication", p.Intoxication);
		Add("hunger", p.Hunger); Add("illness", p.Illness);
		Add("trust_player", r.TrustPlayer); Add("care_player", r.CarePlayer); Add("gullibility", r.Gullibility);
		Add("infatuation_player", r.InfatuationPlayer); Add("player_erratic_behaviour", r.PlayerErraticBehaviour);
		return d;
	}

	// Tag the most recent logged turn (good/edit/discard + optional texture/note). Returns
	// false if there's no turn awaiting a tag.
	public bool TagLastTurn(string tag, string? texture, string? note)
		=> _trainingLogger?.Tag(tag, texture, note) ?? false;

	// Write any buffered-but-untagged turn — called when a conversation ends.
	public void FlushTrainingLog() => _trainingLogger?.FlushUntagged();

	// ── Memory relevance ───────────────────────────────────────────────────────
	// Embeds player-derived memories (orphan/suspect/episodic) once each and caches the
	// vectors, so PersonaBuilder can rank them against the query. World memories are
	// authored, bounded and always injected, so they need no embedding. Cheap: on a fresh
	// game these lists are empty, and new memories are embedded once as they accrue.
	private async Task<IReadOnlyDictionary<string, float[]>> EnsureMemoryEmbeddingsAsync(NpcState npc)
	{
		foreach (var m in npc.OrphanMemories.Concat(npc.SuspectMemories))
		{
			if (string.IsNullOrEmpty(m.Id) || string.IsNullOrWhiteSpace(m.Content)) continue;
			if (_memoryEmbeddings.ContainsKey(m.Id)) continue;
			_memoryEmbeddings[m.Id] = await _embeddingService.GetEmbeddingAsync(m.Content, isDocument: true);
		}

		foreach (var e in npc.EpisodicMemories)
		{
			if (string.IsNullOrEmpty(e.Id) || string.IsNullOrWhiteSpace(e.Content)) continue;
			if (_memoryEmbeddings.ContainsKey(e.Id)) continue;
			_memoryEmbeddings[e.Id] = await _embeddingService.GetEmbeddingAsync(e.Content, isDocument: true);
		}

		if (_memoryEmbeddings.Count > MemoryEmbeddingPruneThreshold)
			PruneMemoryEmbeddings();

		return _memoryEmbeddings;
	}

	// Drop cached embeddings whose memory no longer exists on any NPC. Safe to walk every
	// NPC here — this runs between the previous turn's flushed background work and this turn's
	// (not yet scheduled), so no background task is mutating the memory lists.
	private void PruneMemoryEmbeddings()
	{
		var liveIds = new HashSet<string>();
		foreach (var n in _npcRegistry.GetAllNpcs())
		{
			foreach (var m in n.OrphanMemories.Concat(n.SuspectMemories))
				liveIds.Add(m.Id);
			foreach (var e in n.EpisodicMemories)
				liveIds.Add(e.Id);
		}

		foreach (var staleId in _memoryEmbeddings.Keys.Where(k => !liveIds.Contains(k)).ToList())
			_memoryEmbeddings.Remove(staleId);
	}

	// ── Memory creation ───────────────────────────────────────────────────────

	private async Task CreateConversationMemoriesAsync(
		NpcState npc,
		string npcId,
		string userQuery,
		string response,
		int currentDay)
	{
		var beliefBaseline = ComputeBeliefBaseline(npc);

		var memories = await _conversationMemoryCreator.TryCreateMemoriesAsync(
			userQuery, response, npc, beliefBaseline, currentDay);

		// Emotional weighting — memories formed during peak emotional states are more vivid
		var emotionalPeak = MathF.Max(
			MathF.Max(npc.EmotionalState.Fear, npc.EmotionalState.Grief),
			MathF.Max(npc.EmotionalState.Anger, npc.EmotionalState.Anxiety));

		// Same value the memories were created with — no need to recompute
		var credibility = beliefBaseline;

		foreach (var memory in memories)
		{
			if (emotionalPeak > 0.4f)
			{
				var boost = 1f + (emotionalPeak * 0.35f);
				memory.Fidelity = MathF.Min(memory.Fidelity * boost, 0.95f);
				memory.InitialFidelity = memory.Fidelity;
			}

			memory.Credibility = credibility;

			// AddMemory routes to OrphanMemories or SuspectMemories based on fidelity
			_npcMemoryManager.AddMemory(npcId, memory, isPlayerMemory: true);

			if (_systemConfig.LogMemory)
			{
				var credLog = _systemConfig.LogMemoryCredibility
					? $", credibility: {memory.Credibility:F2}"
					: string.Empty;
				Console.WriteLine($"[memory] stored: \"{memory.Content}\" (fidelity: {memory.Fidelity:F2}{credLog})");
			}
		}
	}

	// Approximates how believable the player currently is to this NPC.
	// See heuristics audit — candidate for LLM-judged credibility in Phase 4.
	private static float ComputeBeliefBaseline(NpcState npc)
	{
		var r = npc.PlayerRelationship;
		var e = npc.EmotionalState;

		// Weighted average — divisor is the sum of the term weights (1 + 1 + 1 + 0.4 + 0.6),
		// so a fully neutral NPC sits near 0.5 and only a trusting, credulous, smitten,
		// drunk NPC approaches 1.0. (Was /3f, which let ordinary NPCs peg at the clamp.)
		const float totalWeight = 1f + 1f + 1f + 0.4f + 0.6f;
		var beliefBaseline = (r.TrustPlayer +
							 (1f - e.Suspicion) +
							  r.Gullibility +
							  r.InfatuationPlayer * 0.4f +
							  npc.PhysicalState.Intoxication * 0.6f) / totalWeight;

		return Math.Clamp(beliefBaseline, 0f, 1f);
	}
}
