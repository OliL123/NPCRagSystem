# NPC RAG System — Architecture

> Overview + request flow + technique catalog. For line-by-line walkthroughs of the intricate
> parts see [DEEP_DIVE.md](DEEP_DIVE.md); for where the project is going see [ROADMAP.md](ROADMAP.md).

A C# .NET 8 console app powering NPC dialogue for a text RPG on the world of **Ath** — the
city of **Antitheis** and the frontier town of **Carvallen**. The player talks to NPCs in
natural language; NPCs reply in-character via a RAG pipeline (world lore + NPC memory +
emotional state) running on a **locally-served LLM**.

Goal: NPCs that feel like real people, using relationships, persistent memory, emotional state and
distinct voices. We do not want generic chatbots.

---

## Model / inference direction (current)

- **Runtime model: `hermes3:8b`** via **Ollama** (native `/api/chat`). Chat model is chosen at
  startup by `ModelPicker`; `nomic-embed-text` does embeddings.
- **Where it's heading (parked, not required to run):** the base models don't do reliable
  *state → tone* (anger → curt etc.) — it's the RLHF helpfulness prior, not size. Two tracks are
  scoped to close that gap, both landing on **llama.cpp** as the runtime:
  - **Track A — QLoRA fine-tune of hermes** (voice, terseness, state-mapping, de-slop, body
    language). Tooling in `ml/finetune/`. *In progress; data curation ongoing.*
  - **Track B — control vectors** (repeng) to dial state intensity live. Tooling in
    `ml/control-vectors/`. *Experimental.*
- The game runs fully on the stock Ollama model **today** — the ML track is an enhancement, not a
  dependency. See [ROADMAP.md](ROADMAP.md) for status.

---

## Request flow (`RagPipeline.QueryAsync`)

One player line → one NPC reply. In order:

1. **Flush** the previous turn's background memory work (single-flight per NPC).
2. **Embed** the player line (`nomic-embed-text`).
3. **Classify** complexity (Simple/Moderate/Complex) + topics.
4. **Working memory** — track which world entities the player keeps mentioning.
5. **HyDE** — on Complex queries, retrieve via a hypothetical-answer embedding.
6. **Player-behaviour eval** — repetition/incoherence → nudges `player_erratic_behaviour`.
7. **Retrieve** — hybrid BM25 + vector → RRF fusion → rerank (off by default) → MMR diversify →
   chunk-compress. (`RetrieveAsync`)
8. **Claim detection** — player contradicting known facts / accusing the NPC → injects a
   one-turn constraint and shifts suspicion/anger.
9. **Build the system prompt** (`BuildSystemPrompt` + `PersonaBuilder`):
   persona base → world moment → memories (world/orphan/episodic/suspect) → working memory →
   claim constraint → retrieved Context → **current-state block** (last, for recency).
10. **Build chat messages** (`BuildTurnMessages`): prior turns as real `user`/`assistant`
    messages (NOT text in the system prompt — that caused verbatim echoing), then a trailing
    `[RIGHT NOW: …]` system note (time/weather/mood) so recency isn't lost.
11. **Generate** — stream via Ollama; strip wrapping quotes; strip OR keep `*beats*` per
    `UseStageDirections`; suppress the `<END>` control token.
12. **Record** — order detection, add the turn to history, **log the turn** for training.
13. **Background** (while the player reads/types): extract conversation memories, scar-tissue
    compress, save NPC state.
14. **Advance time** by the exchange's word count.

Other entry points on `RagPipeline`: `GenerateOpenerAsync` (NPC speaks first),
`HandleSilenceAsync` (player says nothing), `HandleNameRevealAsync` (intro), `CompareAsync`
(dev A/B), `TagLastTurn` / `FlushTrainingLog` (training capture).

---

## Where things live (code map)

| Area | Files | Notes |
|------|-------|-------|
| Entry / DI wiring | `Program.cs` | builds every service, constructs `RagPipeline` + `GameLoop` |
| Config | `Configuration/` — `SystemConfig`, `OutputConfig`, `JsonDefaults` | namespace `NPCRAGSystem.Configuration` |
| Game REPL | `Game/GameLoop.cs`, `Game/ModelPicker.cs` | location loop, conversation loop, dev commands |
| Pipeline | `RAG/Pipeline/RagPipeline.cs` | the orchestrator (above) |
| Prompt build | `RAG/Pipeline/PersonaBuilder.cs` | persona + `BuildCurrentState` (state block) |
| Memory creation | `RAG/Pipeline/{ConversationMemoryCreator,EpisodicMemoryCreator,MemoryConsolidator,ScarTissueCompressor}.cs` | |
| Other LLM stages | `RAG/Pipeline/{SelfCritiqueService,ClaimDetector,GossipService}.cs` | |
| Retrieval | `RAG/Retrieval/` (fusion, MMR, HyDE, compression, `BM25Index`, `InMemoryLoreData`, `VectorMath`) | hybrid search |
| LLM/embeddings | `Services/{OllamaLlmService,OllamaEmbeddingService}.cs` | native `/api/chat`; sampling params |
| Training capture | `Services/TrainingDataLogger.cs` | writes `training_log.jsonl` + `.txt` |
| State — managers | `State/Managers/` — `ConversationTracker`, `WorkingMemoryManager`, `NpcMemoryManager`, `GameStateManager`, `PlayerStateManager` | logic over live state |
| State — repositories | `State/Repositories/` — `NpcRegistry`, `LocationRegistry`, `EntityRegistry`, `SaveSlot`, `PendingGossipStore` | load/save, schedules, history |
| Domain types | `Domain/`, `Domain/Npc/` | `NpcState`, `EmotionalState`, etc. |
| Authored content | `Data/World/` (npcs, locations, accents, entities), `Data/Classifier/`, `Data/Lore/*.txt`, `Data/SaveTemplate/` | templates → seeded into `Data/Saves/auto/` |
| ML workshop | `ml/control-vectors/`, `ml/finetune/` | Python; separate from the C# app |

**NPC state** (`Domain/Npc/NpcState.cs`): `EmotionalState` (fear/grief/hope/suspicion/
anger/anxiety/disgust/guilt), `PhysicalState` (exhaustion/pain/intoxication/hunger/illness),
`PlayerRelationship` (trust/care/gullibility/infatuation/erratic), plus `BaselineEmotionalState`
(authored "normal", persisted, anchors the deviation ranking) and the memory lists
(world/orphan/suspect/episodic).

---

## Dev commands (DevMode)

At the location `>` prompt and/or the conversation `You:` prompt:
- `talk <npc>` — jump into a conversation with any NPC by id/name, ignoring location/schedule (`talk` alone lists ids).
- `debug <npc_id> <attr> <val>` — set any stat (0–1). Stats: fear, grief, hope, suspicion, anger, anxiety, disgust, guilt, exhaustion, pain, intoxication, hunger, illness, trust_player, care_player, gullibility, infatuation_player, player_erratic_behaviour.
- `wm <note> [| <flavour>] [| !]` — inject authored working memory (e.g. a hidden goal).
- `tag <good|edit|discard> [texture] [| note]` — tag the last reply for training.
- `forget` — clear the conversation thread + working memory (clean slate for the next test).
- `compare <msg>` — run the line through the primary + comparison models side by side.
- `advance <Nh | Nd>`, `stats`, `time`, `move`, `leave`, `quit`.

---

## Key config (`Config/SystemConfig.cs`)

- `UseStageDirections` (default **on**) — allow `*physical beats*` (dim) vs strict spoken-only.
- `UseSelfCritique` (off) — second-pass quality gate; opt-in at the picker.
- Sampling: `Temperature 0.85`, `TopP 0.9`, `RepeatPenalty 1.15`, `RepeatLastN 320` (dialogue
  only; JSON/utility calls stay deterministic).
- `LogTrainingData` (on) — write every turn to the training log.
- `UseReranker` (off), `ConversationHistoryWindow` (6), `DevMode`, persistence flags.

---

## Diagnosing common issues

- **NPC ignores its emotional state** → the state IS in the prompt (check `training_log.jsonl`'s
  `system` field for `[HOW YOU FEEL RIGHT NOW …]`). If it's there, the *model* is ignoring it —
  that's the Track A/B gap, not a bug. If it's missing, check `PersonaBuilder.BuildCurrentState`
  (it drops states at/below `BaselineEmotionalState`, so a polluted baseline can suppress them —
  start a New game for clean baselines).
- **NPC repeats/echoes earlier replies** → conversation history. It's now passed as chat
  messages (`BuildTurnMessages`) and cleared on `leave`/`forget` (`ConversationTracker`).
- **NPC recites lore it shouldn't know** → retrieval (`RetrieveAsync`) + the `[WHAT YOU KNOW]`
  prompt rule in `BuildSystemPrompt`.
- **Quotes / stray actions in output** → `StripWrappingQuotes` / `StripStageDirections` +
  `UseStageDirections`.
- **Hang / long pause before a reply** → model load or a reasoning model's hidden pass
  (`OllamaLlmService.IsThinkingModel` sends `think:false` for qwen3/qwq/deepseek-r1/magistral).

---

## Techniques & algorithms (catalog)

| Technique | Where | One-liner |
|-----------|-------|-----------|
| Dense vector retrieval | `RAG/Retrieval/InMemoryLoreData`, `VectorMath` | cosine similarity over `nomic-embed-text` embeddings |
| BM25 lexical retrieval | `RAG/Retrieval/BM25Index` | classic keyword ranking (TF saturation + IDF + length norm) |
| **Hybrid search** | `RagPipeline.RetrieveAsync` | runs vector + BM25, then fuses |
| RRF fusion | `RAG/Retrieval/RRFFusion` | rank-based fusion of the two result lists |
| MMR | `RAG/Retrieval/MMRSelector` | relevance-vs-diversity reranking |
| HyDE | `RAG/Retrieval/HyDEGenerator` | hypothetical-answer embedding for hard queries |
| Topic filtering | `RAG/Classification/TopicClassifier` | restrict retrieval to relevant topic tags |
| Complexity classification | `RAG/Classification/ComplexityClassifier` | Simple/Moderate/Complex via cosine to labelled examples |
| Chunk compression | `RAG/Retrieval/ChunkCompressor` | trims chunks to query-relevant spans |
| Cross-encoder rerank (off) | `Reranker`, `LlmRerankerService` | optional LLM-judge rerank |
| Ebbinghaus memory decay | `State/Managers/NpcMemoryManager` | exponential forgetting curve over in-game days |
| Memory reinforcement | `NpcMemoryManager` | re-encountered/merged memories regain fidelity |
| Scar-tissue compression | `ScarTissueCompressor` | merge faded memories into a hazy summary |
| Episodic consolidation | `EpisodicMemoryCreator`, `MemoryConsolidator` | sessions → long-term episodic records |
| Emotional weighting | `RagPipeline.CreateConversationMemoriesAsync` | memories formed at peak emotion are more vivid |
| Belief/credibility baseline | `ComputeBeliefBaseline` | how believable the player currently is to this NPC |
| Claim detection | `ClaimDetector` | contradictions/accusations → suspicion/anger + memory reclassification |
| Player-behaviour eval | `ConversationTracker.EvaluatePlayerBehaviour` | repetition/incoherence → `player_erratic_behaviour` |
| State deviation ranking | `PersonaBuilder.BuildCurrentState` | surface what changed from baseline, strongest first |
| Streaming token suppression | `RagPipeline.RenderableSoFar` | hide `<END>` / stage-directions mid-stream, prefix-stable |
| Single-flight background work | `_pendingPostTurn` + `FlushPendingMemoryWorkAsync` | ≤1 async memory task per NPC |
| Gossip propagation | `GossipService` | spread session facts to nearby NPCs |
| `num_keep` KV-cache pinning | `OllamaLlmService` | protect the stable prompt prefix from truncation |
| Control vectors (repeng) | `ml/control-vectors` | activation steering by emotional state — Track B |
| QLoRA fine-tune | `ml/finetune` | state→tone, voice, de-slop — Track A |

---

## Deep dives (the intricate parts)

### Hybrid retrieval — RRF + MMR
Two retrievers run over the lore chunks: **dense** (cosine of the query embedding vs each chunk embedding) and **BM25** (lexical). They disagree usefully — vector catches paraphrase, BM25 catches exact terms/names. They're fused by **Reciprocal Rank Fusion** (`RRFFusion`): each list contributes `1 / (rank + 1 + k)` per chunk (`k = 60`), summed across both lists. RRF uses *rank*, not raw score, so the two incomparable score scales never need normalising — a chunk ranked high in *either* retriever floats up, and one ranked high in *both* wins.

The fused top-N then goes through **MMR** (`MMRSelector`, Maximal Marginal Relevance) to kill redundancy. Greedily, each pick maximises `λ·relevance − (1−λ)·maxSimilarityToAlreadyPicked` with `λ = 0.6`. So it's mostly relevance but penalises a chunk that's too similar to something already chosen — you get coverage of *different* facts instead of five paraphrases of one. Finally `ChunkCompressor` trims each surviving chunk to the spans that actually match the query.

### HyDE (hypothetical document embeddings)
On *Complex* queries only, instead of embedding the player's (often short/vague) question, `HyDEGenerator` has the LLM write a *hypothetical answer* and embeds **that**. A made-up answer lives in the same semantic neighbourhood as the real lore, so it retrieves better than the bare question. Cheap trick, real gain — gated to Complex so it doesn't tax every turn.

### Memory: the Ebbinghaus forgetting curve
Each memory has `Fidelity` (current confidence) and `InitialFidelity` (anchor). Decay is **absolute**, recomputed from total elapsed days, not step-by-step (`NpcMemoryManager`):
```
stability  = DecayWeight × 10 × anchorBonus      (×15 for episodic)
retention  = exp(−daysElapsed / stability)
Fidelity   = InitialFidelity × retention
```
That's the literal Ebbinghaus curve. `DecayWeight` = importance (a name is sticky, small talk fades); `anchorBonus` slows decay for a memory tied to a vivid **episodic** anchor (so a fact attached to a big moment is remembered longer). Memories below threshold stop being injected into the prompt; **reinforcement** (re-hearing/merging a fact) bumps fidelity back up. **Scar-tissue compression** merges a cluster of faded memories into one hazy summary rather than deleting them — the NPC keeps a blurry impression, not a clean gap. **Emotional weighting**: a memory formed while the NPC was at peak fear/anger/etc. gets a fidelity boost — emotionally charged moments stick.

### State deviation ranking
`BuildCurrentState` doesn't dump all non-zero stats — it ranks **emotions by deviation from `BaselineEmotionalState`** (the NPC's authored "normal"), drops anything at/below baseline (that's just their nature, already in the persona), and leads with the biggest standout under "the first point matters most". So a wary farmer's baseline suspicion doesn't clutter the prompt, but a spike of fear leads it. (Physical/relationship stats have no baseline, so they rank by absolute value.) This is why a polluted baseline can silently suppress a state — see Diagnosing.

### Streaming control-token suppression
The hard part of streaming: the model emits `<END>` or `*beats*` token-by-token, and you must never flash a half-token like `<EN`. `RenderableSoFar(s)` returns the **prefix-stable** portion safe to show *right now*: it removes closed `*…*`/`(…)`/`<…>` spans, **holds back** at any still-open delimiter (it may close later), and holds back a partial end-token tail (`TrailingEndPrefixLen` finds the longest suffix of the buffer that's a prefix of `<END>`/`[END]`). The caller prints only the *delta* past what it already showed. "Prefix-stable" = earlier output never changes as more arrives, so the delta-printing is always correct.

### Single-flight background work
Covered in the flush diagram: post-turn memory work runs as a `Task` in `_pendingPostTurn[npcId]`; `FlushPendingMemoryWorkAsync` awaits + removes it before the next turn touches that NPC's memory. Guarantees ≤1 in-flight task per NPC (no backlog) and no read/write race on the memory lists.

### Claim detection + the gullibility gate
When the player contradicts known lore or accuses the NPC, `ClaimDetector` fires: it raises suspicion/anger, **reclassifies** the conflicting memory (moves it to `SuspectMemories` with sceptical framing), and injects a one-turn constraint into the prompt. A **gullibility gate** means a credulous NPC sometimes *misses* the contradiction entirely (`Random < Gullibility`) — so naïve characters can be lied to.

### Control vectors (Track B, repeng)
Not C# — the Python workshop. For each trait, contrastive prompt pairs (angry vs calm) are run through the model and the **difference in hidden activations** is averaged into a direction vector. At inference llama.cpp adds `vector × strength` to the residual stream, pushing tone without retraining. Bidirectional: `+` = toward the trait, `−` = away. They're the live intensity dial; the fine-tune is the voice.

---

## Intentionally incomplete

- Intro exists (`IntroSequenceAsync`) but `SkipIntro = true` during dev.
- No events system yet (world is static between sessions) — Phase 4/5.
- Reranker disabled pending tuning.
- Several Inner Ward NPCs lightly tested.
- Body-language is the interim `*asterisk*` convention; the structured/grammar channel is
  deferred to the llama.cpp deploy.

## Running it

Needs Ollama (`ollama serve`) with a chat model + `nomic-embed-text`. First run embeds all lore
and caches to `Data/Cache/embedding_cache.json`.
```
dotnet run            # add --dev for dev commands
```
Edits to `Data/World` / `Data/Lore` need a rebuild + **New game** (saves seed from templates).
