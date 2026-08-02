---
name: npc-training-data
description: >-
  Run the Track-A fine-tune data pipeline for the Ath NPC game — turn
  battery-collected NPC dialogue into a curated training set. Covers archiving
  the collection log, building the review table, auto-drafting the in-voice
  rewrite for every response, assigning verdicts (good/edit/discard), the
  state/disclosure sanity checks that stop the model learning hallucinations,
  running convert_tables.py, and validating the output. Use this whenever
  preparing NPC fine-tune data, curating a collection batch, rewriting/editing
  collected NPC responses in bulk, filling a data_review table, or building
  train.jsonl. Pairs with npc-writing (which supplies the voice for each rewrite).
---

# Curating Track-A training data

This is the **process** for turning collected NPC dialogue into a fine-tune dataset. The *voice* of each rewrite comes from the **npc-writing** skill — read that too; this skill is the pipeline around it.

The whole point is a clean set of `(system prompt, user prompt, ideal in-voice assistant reply)` triples. The base model (Hermes) writes serviceable-but-wrong-voiced replies; we rewrite them into the house voice so the fine-tune learns *that*.

Everything lives in `ml/finetune/`.

## The pipeline

**1. Collect (in-game).** A battery (a pasteable block of `talk <npc>` / `reset <npc>` / `debug <npc> <axis> <value>` / prompt / `leave` turns, with `collect on` at the top) drives NPC conversations and logs each turn to the training log written by `TrainingDataLogger` (see `Program.cs` — currently `Data/training/training_log.jsonl` under the exe's base dir). Each line has `system` (persona + state + world), `user` (the prompt), `response` (Hermes's reply), `npc`, and state. `debug <npc> <axis> <value>` is how you set the per-turn emotional/physical state; `reset` isolates the turn; the in-game `tag <good|edit|discard>` can mark a turn's verdict inline. Battery scripts live as `ml/finetune/battery_batchNN.md`.

**2. Archive the log — immediately, before anything else.** Copy `training_log.jsonl` to `ml/finetune/logs/batchNN_log.jsonl` **before the next New game wipes it.** This is non-negotiable: `convert_tables.py` joins each review table to its *own* archived log by the `batchNN` token in the filename, and it will NEVER fall back to the live log (a coincidental line-count match from a different batch is silent corruption). No archive = the batch is unrecoverable without re-collecting.

**3. Build the review table** `ml/finetune/data_review_batchNN.md`. Exactly seven columns, and the row index must line up with the archived log (table row `i` ↔ log line `i`):

```
| # | NPC | Context (time · loc · state) | Prompt | Hermes reply | Verdict | Your Rewrite |
```

The converter reads columns 0,1,3,4,5,6 (Context in col 2 is for human reference only).

**4. Auto-draft the rewrite for every row.** This is the bulk of the work and where you do a first pass so the human only reviews. For each row, write the ideal in-voice reply using **npc-reply-craft** (sorts the reply by input type — engine probe, hostile, probe-private, knowledge-boundary, nonsense, meta, action, etc. — and gives the response policy + craft for each) together with **npc-writing** (the character's voice) — apply the dials, the anti-tics (A-but-B, echo-opener, action-sandwich, flourishes…), and match the emotional state in the row's context. Then a **consolidation pass**: sweep the whole batch for any tic that slipped in, and check no two NPCs have drifted into the same register.

**5. Assign verdicts.**
- **`edit`** — the row trains on *Your Rewrite*. This is the overwhelming default (~95%+), because the whole reason for the project is that Hermes's writing isn't the target voice.
- **`good`** — the row trains on Hermes's *logged response* unchanged. Rare — only when the collected reply is already exactly right. If you rewrote it, it's an `edit`, not a `good` (the converter ignores rewrites on `good` rows).
- **`discard`** — dropped (kept aside for a later DPO pass). Use for a confused reply caused by a broken/nonsensical prompt.

**6. The state & disclosure checks — this is where you stop training hallucinations.** A rewrite must not lean on anything the NPC doesn't actually have in its context:
- **Location** — "here"/"out there" must match where the NPC actually is.
- **Time** — no "breakfast" reply at 3pm.
- **Relationship/knowledge** — a stranger shouldn't reveal a guarded secret, presume shared history, or confirm familiarity the NPC doesn't have. Presuming *secret* info is a real error (guarded NPCs guard); presuming *public* info is minor. Source-attribution ("people say", "I heard") legitimizes a stranger knowing something.
- Match disclosure to trust: guarded characters give little to strangers; glimpse a wound, don't confess it.

**7. Convert.** From `ml/finetune/`: `py convert_tables.py` → `out/train.jsonl`. It joins each `data_review_batchNN.md` to `logs/batchNN_log.jsonl`, validates that each row's Prompt matches the logged `user` (mismatches are SKIPPED with a warning, never silently mis-joined), and emits one `{"messages":[system,user,assistant]}` per kept row.

**8. Validate the output.** Read the converter's summary: check the `good/edit/discard` counts look right, and that there are **0 prompt-mismatch warnings** (each warning = a dropped row). Any warning means a table row's Prompt drifted from its log line — fix the table, don't ignore it.

## Review-table escaping (so edits don't break the converter)

- Cells can't hold real newlines — write multi-paragraph rewrites with `<br>` (the converter restores them).
- Escape literal `*`, `|`, `_` inside a rewrite as `\*`, `\|`, `\_` (Markdown table + the converter's unescape).
- Verdict must be exactly `good`, `edit`, or `discard` (case-insensitive).
- Never let a rewrite cell go empty on an `edit` row (empty target = skipped with a warning).

## Retrain

Feed `out/train.jsonl` to `ml/finetune/train_pilot_kaggle.py` (Kaggle, Unsloth QLoRA on Hermes-3-8B). Scale the hyperparameters to the set size and **watch the loss**: healthy SFT settles ~0.5–1.5; if it dives below ~0.1 you're memorising, not training (the degenerate failure mode is token loops on off-distribution prompts). The tighter/more-stylised the targets, the lower the learning rate needed — start gentle (~1 epoch / 2e-5–5e-5 for a few hundred rows) and let the loss curve guide you.
