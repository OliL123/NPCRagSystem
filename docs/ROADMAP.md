# Roadmap & Status

Where the project is, what's parked, and what's designed but unbuilt. The engine and world are
built and playable today; everything below is enhancement.

---

## Built and working

- **The RAG dialogue engine** — hybrid retrieval (vector + BM25 → RRF → MMR → compression), HyDE,
  topic/complexity classification, semantic cache. See [ARCHITECTURE.md](ARCHITECTURE.md).
- **NPC cognition** — episodic memory with Ebbinghaus decay, reinforcement, emotional weighting,
  scar-tissue compression; emotional/physical/relationship state; state-deviation ranking.
- **Social systems** — claim detection with a gullibility gate, gossip propagation, player
  erratic-behaviour tracking.
- **World** — locations with NPC schedules, movement, knock/door handling, a player-state model,
  intro sequence, and an authored cast across Antitheis and Carvallen.
- **Dev tooling** — in-game `talk` / `debug` / `wm` / `tag` / `compare` commands, and a
  training-data logger.

## In progress

- **Track A — QLoRA fine-tune of `hermes3:8b`** (`ml/finetune/`). Teaches the behaviour no
  off-the-shelf model does reliably: *state → tone* (anger → curt, fear → rattled), terseness,
  de-slop, and body-language `*beats*`. Pipeline (collect → curate → convert → train) exists;
  data curation is the active work. **Currently parked in favour of shipping the engine** — the
  game runs fully on the stock model without it.
- **Track B — control vectors** (`ml/control-vectors/`, repeng). Live activation-steering to dial
  emotional intensity at inference. Experimental; weight-specific, so it gets rebuilt on the
  fine-tuned weights once Track A lands.

## Designed, not yet built (Phase 4/5)

- **World events system** — foreshadowing, organic and NPC-specific events; the world is currently
  static between sessions.
- **Privacy-gated disclosure** — a per-memory privacy score gates what an NPC will reveal by trust
  level (drop if far above trust; inject as a *guarded* memory if near). The substrate for secrets
  that leak only under the right pressure.
- **Secret-leaking gossip** — NPCs betraying *each other's* personal secrets under emotional
  indiscretion (today gossip only spreads traveller-info, not private facts).
- **Reflection layer** — a beliefs layer above episodic memory (importance scoring → reflections),
  so NPCs form durable opinions, not just recall events. Runs as background work.
- **Persona generator** — a light fine-tune on the authored personas to generate new NPCs in the
  house voice from a seed + trait dials.
- **Neurosymbolic consistency** — a relationship knowledge graph feeding a symbolic reasoner (ASP)
  to keep the social world logically coherent. The furthest-out item.

---

## Known limitations & refactors in flight

An honest snapshot. None of these block
running the game; they're the engineering backlog:

- **No automated tests yet** — the biggest safety gap. First targets are the pure functions
  (`VectorMath`, memory decay, streaming token suppression).
- **Two God-classes** — `GameLoop` and `RagPipeline` are ~1,400 lines each and want decomposing;
  console I/O should move behind an interface for testability.
- **Service composition is hand-rolled** in `Program.cs` — migrating to
  `Microsoft.Extensions.DependencyInjection` + `appsettings.json` is planned.
- **ML pipeline hardening** — dataset versioning/manifest, a pinned `requirements` lockfile, a
  scripted eval harness (base-vs-finetune metrics), and folding the two near-identical
  `train_pilot*.py` scripts into a shared core.
- **Persistence** — regional JSON saves rewrite whole files; a debounced, atomic (temp-then-rename)
  save coordinator is planned.

---

## A note on the training data

The hand-curated fine-tune corpus (battery collections and review tables under `ml/finetune/`) is
valuable and slow to reproduce. Live capture logs and player saves under `Data/Saves/` are
**gitignored** (they're runtime, not source) — if you rely on a curated log, back it up outside the
repo before starting a New Game, which reseeds saves from templates.
