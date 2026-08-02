# NPC model workshop (`ml/`)

This folder is the **ML workshop** — separate from the C# game. It produces two kinds of
small artifact the game eventually consumes:

- **Control vectors** (`control-vectors/out/*.gguf`) — one per emotional/physical state.
  Loaded by llama.cpp at inference; dialled by the matching `NpcState` stat to push the
  model's tone live (anger 0.8 → anger vector × 0.8). **Track B.**
- **A fine-tuned model** (`finetune/`) — QLoRA on hermes, exported to GGUF. Supplies voice
  + de-slop. **Track A.** (scaffolded later)

Nothing here touches the C# code. The heavy work runs in the **cloud**; only the small
outputs come back to run **locally** under llama.cpp.

---

## Track B — control vectors

### 0. Base model
Everything targets **hermes3:8b** (`NousResearch/Hermes-3-Llama-3.1-8B`). Prototype the
vectors on base hermes now; **rebuild them on the fine-tuned hermes later** — vectors are
weight-specific.

### 1. Extract (cloud GPU — one-time per model)
On a rented GPU box (RunPod / Vast / Colab) or any machine with enough VRAM:

```bash
cd control-vectors
pip install -r requirements.txt
python extract.py --model NousResearch/Hermes-3-Llama-3.1-8B
#   add --load-4bit to fit a smaller card
```

This reads `pairs/traits.json` × `pairs/suffixes.txt`, finds each trait's direction, and
writes `out/anger.gguf`, `out/fear.gguf`, … (one per trait). Takes minutes. The big model
only lives on the cloud box — you download just the tiny `out/*.gguf` files.

### 2. Run locally (llama.cpp)
Install llama.cpp and grab a hermes GGUF (e.g. a `Q4_K_M` from HuggingFace) into `../models/`.

```bash
# steer ANGRY: load the anger vector at strength 0.8
./llama-server -m ../models/hermes3-8b.gguf \
  --control-vector-scaled control-vectors/out/anger.gguf 0.8 \
  --port 11434
```

Positive strength pushes *toward* the trait, negative pushes away, 0 disables. Sweep the
strength on a neutral prompt and find where it's clearly angry but still coherent — that's
the per-trait ceiling. Record it.

### 3. Wire to game state (later, C-phase)
The game stops calling Ollama and calls llama.cpp's OpenAI-compatible endpoint instead.
Each request passes the live vector strengths derived from `NpcState` — e.g. `anger 0.8`,
`exhaustion 0.45` → those two vectors at those strengths, clamped to the ceilings from
step 2. Multiple vectors stack.

### When to re-run extract.py
- base model changes (after fine-tuning) — rebuild all
- you edit a trait's pairs — rebuild that trait
- you add a new trait

---

## Layout
```
control-vectors/
  pairs/traits.json     authored trait poles (positive[i] vs negative[i])
  pairs/suffixes.txt    varied dialogue fragments for context
  extract.py            repeng extraction -> out/*.gguf
  requirements.txt
  out/                  generated vectors (gitignored)
finetune/               Track A (scaffolded later)
models/                 big GGUFs (gitignored)
```

Traits in `traits.json` map 1:1 to `NpcState` stats: anger, fear, grief, suspicion,
anxiety, disgust, exhaustion. Add more pairs as needed — same shape.
