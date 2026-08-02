# The ML pipeline — full overview

Written 2026-07-17, the day the Track A pilot completed end-to-end. This is the map:
what the systems are, how a change to any of them reaches an NPC's mouth, what every
downloaded artifact is for, and the configuration that was paid for in broken sessions.

---

## 1. Three systems, one prompt

The single biggest source of confusion is that **three separate systems** feed the same
generation call. They are not alternatives to each other; they do different jobs.

| System | Answers | Lives in | Changed by |
|---|---|---|---|
| **RAG** | *What does this NPC know?* | C# (`RAG/`) | editing `Data/Lore/*.txt`, retrieval config |
| **Track A — fine-tune** | *How does this NPC speak?* | model weights | retraining on `train.jsonl` |
| **Track B — control vectors** | *How does state bend the voice, live?* | `ml/control-vectors/` | re-extracting vectors |

Every dialogue turn assembles one prompt:

```
[ persona_base + speech_quirk ]     <- authored JSON       (who they are)
[ VOICE: accent register ]          <- accents.json        (how they sound)
[ world/episodic memories ]         <- NpcState            (what they remember)
[ Context: retrieved chunks ]       <- RAG                 (what they know)
[ CURRENT STATE: "You are furious" ]<- PersonaBuilder      (how they feel)
        |
        v
   the model  <- Track A decides the *style* of what comes out
        |
        v  (future) llama.cpp with control vectors scaled by NpcState  <- Track B
```

**RAG is knowledge. Track A is voice. Track B is live tone.** A retrieval bug and a
voice problem look identical from the outside and are fixed in completely different places.

---

## 2. Track A — the fine-tune pipeline (this is what we ran)

```
 (1) play the game, tag turns          `tag good|edit|discard [texture] [| note]`
        |                               -> Data/training/training_log.jsonl
        v
 (2) review + rewrite by hand          ml/finetune/data_review_batchNN.md
        |                               (curation is where the quality actually comes from)
        v
 (3) convert                            python convert_tables.py
        |                               joins review tables against logs/batchNN_log.jsonl
        v                               -> ml/finetune/out/train.jsonl   (110 examples)
 (4) QLoRA train (cloud GPU)            ml/finetune/train_pilot_kaggle.py
        |                               Kaggle T4 x2, ~20 min
        v                               -> LoRA adapters (~160 MB)
 (5) save + publish adapters            -> lora_ckpt.zip  AND  HF: <user>/hermes-npc-lora
        |
        v
 (6) merge + quantize                   save_pretrained_gguf(maximum_memory_usage=0.5)
        |                               adapters + base -> fp16 (~16 GB) -> GGUF -> Q4_K_M
        v                               -> hermes-npc.Q4_K_M.gguf (4.9 GB)
 (7) publish + fetch                    HF: <user>/hermes-npc-gguf  -> browser/CLI download
        |
        v
 (8) register + test                    ollama create hermes-npc -f Modelfile
                                        in game: compare <msg>
```

Each stage is cheap except (4) and (6). Steps (1)–(3) are the part that actually
determines quality, and they are entirely authoring, not machine learning.

---

## 3. Concepts, in plain terms

**Base model** — `NousResearch/Hermes-3-Llama-3.1-8B`. 8 billion parameters. Built on
Llama 3.1 but trained by NousResearch with the **ChatML** prompt format. That distinction
cost us a whole debugging round (see §5).

**LoRA / QLoRA** — full fine-tuning updates all 8B weights (needs ~80 GB VRAM, impossible
on a T4). LoRA freezes the base and trains small "adapter" matrices injected beside the
attention layers — ~160 MB instead of 16 GB. **QLoRA** = the same, but the frozen base is
held in 4-bit to fit in ~5.5 GB VRAM. This is why a free T4 can fine-tune an 8B model.

**Adapter vs merged vs quantized**
- *Adapter* (160 MB) — just the diff. Useless alone; needs the base.
- *Merged* (~16 GB fp16) — adapter mathematically folded into base weights. One standalone model.
- *Quantized GGUF* (4.9 GB) — the merged model with weights compressed to ~4 bits.

**Q4_K_M** — a llama.cpp quantization: ~4 bits per weight, "K-quant, Medium". Shrinks 16 GB
to 4.9 GB for a small quality loss. The `_M` mixes precision — more bits for layers that
matter. This is the standard "run it locally" format.

**safetensors vs GGUF** — safetensors is HuggingFace's training format (fp16, PyTorch).
GGUF is llama.cpp/Ollama's inference format. Training needs one, running locally needs the
other. Step (6) is the bridge, and it's expensive because it must materialize the full fp16
model in RAM before quantizing.

**Modelfile** — Ollama's recipe: which GGUF, which chat template, which stop tokens, default
sampling. **The chat template must match what the model was trained on** or output degenerates.

**Chat template** — the markup wrapping each turn. Hermes-3 uses ChatML:
`<|im_start|>user … <|im_end|>`. Stop tokens tell Ollama when the model is done. Wrong stop
token = generation never terminates.

---

## 4. Hard-won configuration

Every row cost at least one broken session. **Do not change these without a reason.**

| Setting | Value | Why |
|---|---|---|
| unsloth version | `==2026.6.9` | HEAD (2026.7.2) can't load the 4-bit Hermes repo vs transformers 5.5.0 |
| `--force-reinstall` | **never** | dragged numpy 2.0.2→2.5.1 mid-session on Colab |
| install order | install → **restart** → import | changing packages under a live interpreter poisons `sys.modules` |
| bf16 | `is_bf16_supported = lambda: False` | T4 has no bf16; must set **before** importing unsloth |
| `HF_HUB_ENABLE_HF_TRANSFER` | `0` | hf_transfer stalls downloads at 0% |
| `activation_offloading` | `True` | TRL chunked-CE materializes fp32 [seq × 128k vocab] logits → GPU OOM |
| `max_length` | `3328` | data max is 3,148 real tokens; caps the logits buffer without truncating |
| GGUF export path | `/tmp/...` | `/kaggle/working` caps at 20 GB; `/tmp` has ~1 TB |
| `maximum_memory_usage` | `0.5` | default 0.75 → **system RAM** OOM during the fp16 merge |
| chat template | **ChatML** | Unsloth's generated Modelfile guesses Llama-3 headers — wrong (see §5) |
| artifact transport | **HF, immediately** | `/kaggle/working` survives a *restart*, NOT a *shutdown* |

### The transport rule
Kaggle sessions are volatile. **Push every artifact to HuggingFace the moment it exists**,
before attempting any download. HF is the permanent hub; Kaggle is disposable compute.

---

## 5. The chat template trap

Unsloth's `save_pretrained_gguf` writes a Modelfile whose template it **infers from the
architecture** (Llama-3.1) rather than reading the tokenizer's actual template. Hermes-3 is
*built on* Llama-3.1 but trained with **ChatML**. The generated Modelfile therefore:

- wrapped prompts in `<|start_header_id|>…` — a format the model has never seen, and
- set `PARAMETER stop <|eot_id|>` — a token this model **cannot emit** (it emits `<|im_end|>`)

Result: nothing ever stopped generation → `again again again…` forever.

**Fix:** take the base model's template verbatim and swap only `FROM`:
```powershell
ollama show --modelfile hermes3:8b     # copy its TEMPLATE + stop params
```
Bonus: identical templates make `compare` a fair test — differences are then the fine-tune,
not templating artifacts.

---

## 6. Pilot results (2026-07-17)

**The pipeline works end-to-end.** Every stage validated. The model is *not* usable, for one
reason: **overfitting**.

| Config used | Value | Verdict |
|---|---|---|
| examples | 110 | too few |
| `num_train_epochs` | 3 | **too many** — memorized by step 15 (mid epoch 2), then ran 27 more |
| `learning_rate` | 2e-4 | **too high** for a set this small |
| final loss | **0.015** | pathological. Healthy SFT lands ~0.5–1.5 |

Confirmed by **prompt distance**, not guesswork:

| Probe | Distance from training | Result |
|---|---|---|
| "Your ale tastes like piss" | exact training row | ✅ perfect in-character Corin |
| "This ale is the worst I've tasted" | paraphrase, same NPC/topic | ✅ terse, in-character |
| "You look like you've had a rough week" | off-distribution | ❌ `Anyone Anyone Anyone…` |

`repeat_penalty` does not rescue it — it just picks a different word to loop on. That means
damaged weights, not a sampling problem.

**Real signal did appear**: *"Worst you've had or worst we have here?"* — one terse line
where base hermes3 gives four sentences of warm hospitality. The voice transfer is real.

### Next run
```python
num_train_epochs = 1        # was 3
learning_rate    = 5e-5     # was 2e-4
```
**More data is the actual fix.** 110 examples is far too few to teach voice; 300–500 is where
this stops being fragile. The mid-city cast is the natural next batch.

---

## 7. What's on disk, and why

| Path | Size | Purpose | Track |
|---|---|---|---|
| `~/.ollama/models` | 4.6 GB | `hermes3:8b` + `nomic-embed-text` — **the game runs on these** | RAG + play |
| `~/models/hermes-npc.Q4_K_M.gguf` | 4.9 GB | the fine-tuned model | A |
| `~/models/lora_ckpt/` | 160 MB | trained adapters (also on HF) | A |
| `ml/finetune/` | ~0 GB | **the entire Track A pipeline** — scripts + `train.jsonl` | A |
| `ml/control-vectors/` | 5 GB | extraction venv (~3 GB is torch) | **B** |
| `ml/models/Hermes-3…Q4_K_M.gguf` | 4.58 GB | base GGUF for llama.cpp — README §2 puts it here | **B** |
| `ml/llama.cpp/` | 1.1 GB | Track B serves via llama.cpp, not Ollama | **B** |
| `~/.cache/huggingface/hub/…Hermes-3` | 14.97 GB | fp16 base — `extract.py` needs full precision | **B** |

**Nothing here is junk.** The ~25 GB that looks redundant is Track B infrastructure. Note the
fp16 cache is a *cache* — deletable and re-downloadable, at the cost of a 15 GB re-fetch.

**Permanent, offsite:**
- `hf.co/DarkSparktheVoid/hermes-npc-lora` — adapters
- `hf.co/DarkSparktheVoid/hermes-npc-gguf` — the model

---

## 8. Known gaps (from the blind review + the pilot)

- **No lockfile.** The pins in §4 are comments, not enforced. This won't reproduce in six
  months. `requirements.lock` is the single highest-value fix.
- **Two near-identical trainers.** `train_pilot.py` / `train_pilot_kaggle.py` are ~90% the
  same; every fix today had to be applied twice.
- **No dataset manifest.** Nothing ties a GGUF to the data and config that produced it.
- **Eyeball-only evaluation.** No held-out set, no metric, no regression guard. A frozen
  `eval.jsonl` plus scripted base-vs-finetune metrics would have caught the overfit in
  seconds instead of a debugging round.
- **HF push is manual.** It should be a cell in the notebook, not a rescue step.
