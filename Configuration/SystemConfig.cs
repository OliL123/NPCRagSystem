namespace NPCRAGSystem.Configuration;

public class SystemConfig
{
	// ── Ingestion ─────────────────────────────────────────────────────────────
	public bool StripMarkdown { get; init; } = true;

	// ── Pipeline Stages ───────────────────────────────────────────────────────
	// TODO: HyDE should only fire on Complex once IterRAG is built
	public bool UseHyDE { get; init; } = true;
	public bool UseReranker { get; init; } = false;
	public bool UseMMR { get; init; } = true;
	public bool UseChunkCompressor { get; init; } = true;

	// Relevance floor — drops vector-search chunks before RRF fusion runs. RRF is rank-based and
	// discards the absolute score, so without a floor the best-of-a-bad-lot chunk still ranks #1
	// and gets injected even when nothing in the corpus is on-topic. An empty result is fine: the
	// NPC keeps its persona + memories, and no lore beats wrong lore.
	//
	// The floor is adaptive: keep chunks scoring at least RelevanceSigma standard deviations above
	// the mean similarity of the pool that query was scored against. An ABSOLUTE cosine cutoff was
	// tried first and does not work here — nomic-embed-text with task prefixes compresses scores
	// into a narrow band (measured: on-topic 0.68, off-topic 0.60, unrelated chatter 0.50), and the
	// topic pre-filter tightens it further, so any fixed number is fitted to this exact corpus and
	// embedding model and rots the moment either changes. "Standout for this query" travels.
	//
	// Caveat: this assumes relevant means outlier. A query that genuinely matches many chunks will
	// see real ones cut. The principled fix is a calibrated cross-encoder reranker (see UseReranker
	// — currently an LLM call per chunk, hence off). Set to 0 to disable the floor entirely.
	public float RelevanceSigma { get; init; } = 1.5f;

	// Floor safety net — always keep at least this many top-scoring chunks, even if the sigma
	// floor would cut them. The z-score measures standout WITHIN the query's own pool, which is
	// not the same as "this answer needs grounding": a query whose pool is uniformly relevant
	// has no outliers and gets over-cut. Observed live — "tell me about the Collegium and the
	// belief in gods" kept only 2/10 and the model filled the gap by inventing Greek gods.
	// Starving a real lore question is worse than passing a few weak chunks. 0 disables.
	public int RelevanceMinKeep { get; init; } = 3;
	// Off by default — it adds a second full generation per reply, which is slow on a
	// large primary model. Opt in at startup via the model picker. (settable, not init,
	// so the picker can flip it.)
	public bool UseSelfCritique { get; set; } = false;

	// ── NPC State ─────────────────────────────────────────────────────────────
	public bool UsePlayerBehaviourEvaluation { get; init; } = true;
	public bool UseMemoryCreation { get; init; } = true;
	public bool UseScarTissueCompression { get; init; } = true;
	// Detects contradictions/accusations in what the player says and feeds NPC suspicion,
	// anger and memory reclassification — a behaviour toggle, not a log switch.
	public bool UseClaimDetection { get; init; } = true;

	// ── Conversation ──────────────────────────────────────────────────────────
	public int ConversationHistoryWindow { get; init; } = 6;

	// ── Logging ───────────────────────────────────────────────────────────────
	public bool LogComplexity { get; init; } = false;
	public bool LogTopics { get; init; } = false;
	public bool LogMemory { get; init; } = false;
	public bool LogMemoryCredibility { get; init; } = false;
	public bool LogGossip { get; init; } = false;
	public bool LogCritique { get; init; } = false;
	public bool LogClaimDetection { get; init; } = false;
	// Prints the vector-search score spread and how many chunks the RelevanceFloor cut each
	// query — the calibration lever for setting RelevanceFloor against real play.
	public bool LogRelevance { get; init; } = false;

	// ── Persistence ───────────────────────────────────────────────────────────
	// Master switch — false disables all persistence for clean testing sessions.
	// Fresh starts are handled by the save-slot "new game" choice, not reset flags:
	// see SaveSlot and the startup prompt in Program.cs.
	public bool EnablePersistence { get; init; } = true;
	public bool PersistNpcState { get; init; } = true;
	public bool PersistGameState { get; init; } = true;

	// Convenience: the master switch ANDed with the per-kind switch — the form used at every
	// persistence call site.
	public bool PersistsNpcState  => EnablePersistence && PersistNpcState;
	public bool PersistsGameState => EnablePersistence && PersistGameState;

	// ── Debug ─────────────────────────────────────────────────────────────────
	// Skip the intro sequence regardless of PlayerState.HasCompletedIntro
	public bool SkipIntro { get; set; } = false;

	// Developer mode. Off for normal players: the dev commands (wm / debug / advance /
	// compare) are hidden from the banner and inert (typed as normal dialogue instead).
	// Enabled via the "--dev" launch arg or NPCRAG_DEV=1 — so one build serves both
	// players and the people the dev shares testing builds with. (settable, set by Program.)
	public bool DevMode { get; set; } = false;

	// ── Model Selection ───────────────────────────────────────────────────────
	// Show Spectre.Console model picker at startup instead of using FALLBACK_MODEL
	public bool EnableModelPicker { get; init; } = true;
	// Allow A/B comparison mode to be offered in the model picker prompt
	public bool EnableModelComparison { get; init; } = true;

	// ── Presentation ──────────────────────────────────────────────────────────
	// When on, NPCs may emit brief physical beats wrapped in *asterisks* (rendered dim),
	// e.g. *wipes the bar*. When off, output is strictly spoken dialogue and any stray
	// *…*/(…)/<…> action text is stripped. Default on.
	public bool UseStageDirections { get; set; } = true;

	// ── Sampling ──────────────────────────────────────────────────────────────
	// Applied to dialogue generation only — JSON/utility calls stay on Ollama defaults
	// so structured output isn't destabilised. RepeatLastN is the lever for cross-turn
	// greeting loops: Ollama's default 64-token window is too short to notice a repeated
	// "Mornin', son" a turn later, so we widen it.
	public float Temperature { get; init; } = 0.85f;
	public float TopP { get; init; } = 0.9f;
	public float RepeatPenalty { get; init; } = 1.15f;
	public int RepeatLastN { get; init; } = 320;

	// ── Training-data capture ─────────────────────────────────────────────────
	// When on, every dialogue turn (state + persona + context → reply) is logged to
	// JSONL for fine-tuning. Tag turns in-game: 'tag good|edit|discard [texture] [| note]'.
	public bool LogTrainingData { get; set; } = true;

	// Collection mode — for capturing clean state→tone training data. When on, all the
	// state/memory SIDE-EFFECTS are suppressed so each turn is isolated: no player-behaviour
	// evaluation (so a rude line can't bump the state you set), no claim detection, no memory
	// creation, and no end-of-conversation episodic consolidation. The NPC still has its
	// authored persona + memories + the state you set via 'debug'; it just doesn't accumulate
	// anything turn-to-turn. Toggle in-game with 'collect on|off'. Default off (normal play).
	public bool CollectionMode { get; set; } = false;
}