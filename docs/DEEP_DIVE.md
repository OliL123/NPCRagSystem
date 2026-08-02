# Code Deep Dive

Annotated walkthroughs of the intricate parts, with the actual code. Companion to
[ARCHITECTURE.md](ARCHITECTURE.md) (which has the overview). Read each next to its source file.

---

## 1. Hybrid retrieval — RRF fusion

`RAG/Retrieval/RRFFusion.cs`. Two retrievers (dense vector + BM25) return ranked lists.
They're fused by **rank**, not score:

```csharp
private const int K = 60;
// for each result list, by rank position:
var rrfScore = 1.0 / (rank + 1 + K);
// summed per chunk across both lists, then OrderByDescending(RrfScore).Take(N)
```

**Why this is clever:** vector scores (cosine ~0–1) and BM25 scores (unbounded) live on
*incomparable scales* — you can't just add them. RRF throws away the magnitudes and keeps only
*position*. A chunk at rank 0 contributes `1/61`, rank 1 contributes `1/62`, etc. The `+K`
(=60) flattens the curve so the gap between rank 1 and rank 5 isn't huge — it's *forgiving* to
lower ranks. A chunk that ranks decently in **both** retrievers sums two contributions and beats
a chunk that ranks #1 in only one. That's the whole point: agreement across two different
notions of relevance (semantic + lexical) wins.

## 1b. Hybrid retrieval — MMR (diversity)

`RAG/Retrieval/MMRSelector.cs`. After fusion you might have five chunks that all say the same
thing. MMR greedily picks for *relevance minus redundancy*:

```csharp
var relevance = CosineSimilarity(queryEmbedding, candidate.Embedding);
var maxSimilarityToSelected = selected.Count == 0 ? 0f
    : selected.Max(s => CosineSimilarity(candidate.Embedding, s.Embedding));
var mmrScore = _lambda * relevance - (1 - _lambda) * maxSimilarityToSelected;  // λ = 0.6
```

**The trick:** each pick is scored by how relevant it is *and* how *unlike* everything already
picked. `λ=0.6` → 60% "is it relevant", 40% "is it new". So the 2nd pick can be slightly less
relevant than a near-duplicate of the 1st, and still win because it adds *coverage*. It's a
greedy O(k·n) loop — fine at this scale (the comment flags precomputing the similarity matrix
if candidates ever exceed ~50).

---

## 2. HyDE — embed a hypothetical answer

`RAG/Retrieval/HyDEGenerator.cs`, fired only on *Complex* queries.

```csharp
var hypotheticalAnswer = await _llm.GenerateAsync(systemPrompt, query); // "write a passage that answers this"
return await _embeddingService.GetEmbeddingAsync(hypotheticalAnswer, isDocument: true);
```

**Why:** a short vague question ("what happened to the Carantis?") embeds far from the lore that
*answers* it, because questions and answers are phrased differently. So instead of embedding the
question, you have the LLM hallucinate a plausible *answer*, and embed **that**. A fake answer
lives in the same semantic neighbourhood as the real passages, so cosine search lands closer. Note
`isDocument: true` — it's embedded in *document* space (the embedder uses different prefixes for
queries vs documents), because a pseudo-passage is being compared against real passages.

---

## 3. Memory — the Ebbinghaus forgetting curve

`State/Managers/NpcMemoryManager.cs`. Decay is recomputed from *total elapsed days* each time (absolute,
not incremental — so it can't drift):

```csharp
var anchorBonus = hasEpisodicAnchor ? 1f + (anchorFidelity * EpisodicAnchorBonus) : 1f;
var stability  = memory.DecayWeight * 10.0 * anchorBonus;   // ×15 for episodic memories
var retention  = Math.Exp(-daysElapsed / stability);
memory.Fidelity = (float)(memory.InitialFidelity * retention);
```

**Breakdown:**
- `retention = exp(−t / stability)` is the literal Ebbinghaus curve — fast initial drop, long tail.
- `stability` controls how slowly it fades. It scales with `DecayWeight` (importance — a name has
  high weight and barely fades; idle small talk has low weight and drops fast). `DecayWeight ≤ 0`
  short-circuits to "never decays" (used for permanent facts).
- `anchorBonus` slows decay further if the memory is tied to a vivid **episodic** memory — facts
  attached to a big moment are remembered longer. That's why scar-tissue (below) sets `DecayWeight
  = 2.0`: the hazy impression should *linger*, not vanish.
- Below a fidelity threshold a memory stops being injected into the prompt. **Reinforcement**
  (merging/re-hearing) bumps `Fidelity` back up and resets `InitialFidelity` to the new anchor.

### 3b. Scar-tissue compression
`RAG/Pipeline/ScarTissueCompressor.cs`. When several memories have faded, rather than delete them
the LLM merges them into one *hazy* recollection ("I think… something about…"), capped at fidelity
0.4 and given `DecayWeight = 2.0` so it lingers. The NPC keeps a blurry impression instead of a
clean hole — much more human than `list.Remove()`.

### 3c. Emotional weighting
`RagPipeline.CreateConversationMemoriesAsync`. A memory formed during peak emotion is more vivid:
```csharp
var emotionalPeak = Max(Fear, Grief, Anger, Anxiety);
if (emotionalPeak > 0.4f) {
    var boost = 1f + (emotionalPeak * 0.35f);
    memory.Fidelity = Min(memory.Fidelity * boost, 0.95f);
}
```
Flashbulb memory: what you said while the NPC was terrified sticks harder than idle chat.

---

## 4. State deviation ranking

`PersonaBuilder.BuildCurrentState`. The state block doesn't dump every non-zero stat — emotions are
ranked by **deviation from the NPC's baseline**:

```csharp
void AddEmotion(string state, float current, float baseline) {
    var deviation = current - baseline;
    if (deviation < 0.15f) return;                  // at/below baseline = their normal, drop it
    var text = GetModifier(state, current);         // wording reflects actual intensity
    if (text != null) items.Add((text, deviation)); // salience = deviation
}
// physical/relationship have no baseline → AddAbsolute (salience = raw value)
var ranked = items.OrderByDescending(i => i.salience);   // strongest standout first
```

**The insight:** a character's *resting* emotional makeup is already in their persona prose. A wary
farmer's standing suspicion shouldn't crowd the prompt — but a *sudden spike* of fear should lead
it. So emotions are scored by how far they've moved *above* baseline, sub-baseline ones are dropped,
and the biggest standout goes first under "the first point matters most." (This is also why a
*polluted* baseline silently suppresses a state — if baseline anger got saved at 0.8, setting anger
to 0.8 gives deviation 0 → dropped. Hence `BaselineEmotionalState` is now persisted from the authored
value, not re-derived from runtime.)

---

## 5. Streaming control-token suppression

`RagPipeline.RenderableSoFar` + `TrailingEndPrefixLen`. The model streams token-by-token and may emit
`<END>` or `*beats*` mid-stream — you must never flash a half-token like `<EN`.

```csharp
private static string RenderableSoFar(string s, bool keepAsterisks) {
    // walk chars; at an opener (* ( <) jump to its closer and skip the span;
    // if a closer is missing, BREAK — hold here, it may close later
    ...
    // then hold back a partial end-token tail:
    cleaned = cleaned[..(cleaned.Length - TrailingEndPrefixLen(cleaned))];
    // and drop a single leading wrapping quote
}
```

```csharp
// longest suffix of s that is a prefix of "<END>"/"[END]" — how much tail to hold back
private static int TrailingPrefixLen(string s, string mark) {
    for (int len = Min(mark.Length-1, s.Length); len > 0; len--)
        if (s.EndsWith first `len` chars match mark's first `len`) return len;
    return 0;
}
```

**Why it's the trickiest code in the repo:** the caller prints only the *delta* past what it already
showed, so `RenderableSoFar` must be **prefix-stable** — text it returns now must never change as more
tokens arrive. It guarantees that by (a) skipping only *closed* delimiter spans, (b) stopping at any
*open* delimiter (the `*action` might still be streaming), and (c) holding back any suffix that could
be the start of `<END>`. So `"Aye. <EN"` shows just `"Aye. "` and waits; when `<END>` completes it's
caught and stripped; when `*wipes the bar*` closes it renders (or gets dimmed/removed per the toggle).

---

## 6. Single-flight background work

`RagPipeline`. Post-turn memory work runs async so the reply returns instantly:

```csharp
_pendingPostTurn[npcId] = Task.Run(async () => {
    if (npc.Tier == 1 && UseMemoryCreation) await CreateConversationMemoriesAsync(...);
    if (npc.Tier == 1 && UseScarTissueCompression) await CompressMemoriesIfNeededAsync(...);
    if (Persist) await _npcRegistry.SaveAsync();
});
```

```csharp
public async Task FlushPendingMemoryWorkAsync(string npcId) {
    if (!_pendingPostTurn.TryGetValue(npcId, out var task)) return;
    _pendingPostTurn.Remove(npcId);
    try { await task; } catch (Exception ex) { ... }
}
```

**The contract:** the dictionary holds at most one task per NPC. `Flush` is called *before* anything
reads/mutates that NPC's memory (top of the next `QueryAsync`, `EndConversation`, etc.), so: (1) the
last turn's writes have landed before this turn reads, and (2) there's never a backlog — if the model
can't keep up, the player just waits at the next turn. The background task mutates the memory *lists*;
without the flush, the next turn could enumerate them mid-write → "collection modified" crash.

---

## 7. Claim detection + the gullibility gate

`RagPipeline.HandleClaimDetectionAsync`. When the player contradicts known lore or accuses the NPC:

```csharp
if (result.Type == "contradiction" && Random.Shared.NextSingle() < r.Gullibility) {
    // gullibility gate passed — NPC simply doesn't notice the contradiction
    return;
}
e.Suspicion = Clamp(e.Suspicion + result.Severity * (1f - r.Gullibility) * 0.15f, 0, 1);
// reclassify the conflicting memory → SuspectMemories (sceptical framing)
_pendingConstraint = "The traveller just said something that contradicts what you know...";
```

**Two nice touches:** (1) the **gullibility gate** — a credulous NPC (`Random < Gullibility`) *misses*
the contradiction entirely, so naïve characters can be lied to; (2) suspicion rises *scaled by inverse
gullibility* (`1 − Gullibility`), so the same lie rattles a sharp NPC more than a trusting one. The
conflicting memory isn't deleted — it's moved to `SuspectMemories` and re-injected with "things you
find hard to believe" framing, so the NPC stays wary of it.

---

## 8. Player erratic-behaviour detection

`State/Managers/ConversationTracker.EvaluatePlayerBehaviour`. Keeps a rolling window of the player's recent
message *embeddings* and watches two failure modes:

```csharp
if (maxSimilarity >= RepetitionThreshold)        // 0.95 — saying the same thing over and over
    UpdateState(npcId, "player_erratic_behaviour", current + increment);
else if (avgSimilarity <= IncoherenceThreshold)  // 0.20 — every message unrelated to the last
    UpdateState(npcId, "player_erratic_behaviour", current + increment);
else if (window.Count >= 5)
    UpdateState(npcId, "player_erratic_behaviour", Max(0, current - 0.02f));  // slow recovery
```

**Why cosine over a window:** repetition = a new message *too similar* to a recent one (max sim ≥
0.95); incoherence = messages with *no thread* between them (weighted-avg sim ≤ 0.20). First few
exchanges weigh heavier (`isFirstImpression`) — a bad first impression sticks. Recent messages are
weighted more than old ones in the average. The result feeds `player_erratic_behaviour`, which makes
NPCs treat you as unstable. (TechSpecs notes a planned upgrade: also compare to the *lore* DB, so
talking about things that don't exist in the world — "AI", "Hong Kong" — reads as raving.)

---

## 9. Control vectors (Track B, repeng)

`ml/control-vectors/extract.py`. Not C# — the Python workshop. For each trait, contrastive prompt
pairs are built (same context, opposite trait) and fed to the model:

```python
def make_dataset(tokenizer, positive, negative, suffixes):
    for pos, neg in zip(positive, negative):
        for suffix in suffixes:
            dataset.append(DatasetEntry(
                positive=render(tokenizer, pos, suffix),   # "...someone who is furious..." + suffix
                negative=render(tokenizer, neg, suffix),   # "...someone who is calm..."    + suffix
            ))
cv = ControlVector.train(model, tokenizer, dataset)   # diffs hidden activations, PCA → one direction
cv.export_gguf("anger.gguf")
```

**The mechanism:** repeng runs the positive and negative prompts, records the model's *hidden-layer
activations* for each, and takes the average **difference** — that difference *is* the "anger
direction" in activation space. The many `suffixes` give varied contexts so the average isolates the
trait and cancels phrase-specific noise. At inference, llama.cpp adds `vector × strength` to the
residual stream, nudging tone without retraining. Bidirectional: `+0.8` → angrier, `−0.8` → calmer.
It's the live intensity dial; the fine-tune (Track A) supplies the voice.
