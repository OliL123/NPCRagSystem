# Track A — fine-tune hermes (`ml/finetune/`)

Teaches hermes the behaviour no off-the-shelf model does: state → tone (anger → curt,
fear → rattled), terseness, de-slop, and your body-language `*beats*`. The control vectors
(Track B) are the live intensity dial; this is the voice + the state→behaviour mapping.

> **Status: parked.** The game runs fully on the stock Ollama model; this track is an
> enhancement, not a dependency. Tooling is kept working but data curation is on hold.

Layout: `reviews/` (curation tables), `batteries/` (collection prompts), `logs/` (archived
training logs), `train/` (the cloud QLoRA notebooks), `out/` (generated dataset + weights,
gitignored). `convert_tables.py` builds the dataset; `Modelfile` deploys the result to Ollama.

## The loop

1. **Collect** — play in-game, tag turns: `tag good | edit | discard [texture] [| note]`.
   Read `training_log.txt` (the human-readable companion) to decide; the machine data is in
   `training_log.jsonl`. Over-weight the gaps: anger, fear, high exhaustion, interjections.

2. **Curate** — open `Data/training/training_log.jsonl` (or the archived `logs/batchNN_log.jsonl`):
   - `good` → leave as-is (the reply is the target).
   - `edit` → **rewrite its `response`** into the ideal reply — terse for anger, the beat you
     wanted, the slop removed. This is where you manufacture the behaviour.
   - `discard` → skip for SFT (save for DPO later).
   Consistency beats volume: 300 examples where angry NPCs are *always* curt teach the rule
   cleanly; noisy data teaches "maybe."

3. **Convert** — chat-format dataset for the trainer:
   ```
   python convert_tables.py     # joins reviews/*.md + logs/ -> out/train.jsonl
   ```
   Joins each `reviews/data_review_batchNN.md` table to its archived `logs/batchNN_log.jsonl`
   and writes the SFT dataset. See the script's docstring for the authoritative flow. Aim for a
   few hundred+ examples.

4. **Train (QLoRA, cloud GPU)** — Unsloth, on RunPod/Colab/Vast. Outline:
   ```python
   from unsloth import FastLanguageModel
   from trl import SFTTrainer, SFTConfig
   from datasets import load_dataset

   model, tok = FastLanguageModel.from_pretrained(
       "NousResearch/Hermes-3-Llama-3.1-8B", max_seq_length=4096, load_in_4bit=True)
   model = FastLanguageModel.get_peft_model(model, r=16, lora_alpha=16, lora_dropout=0,
       target_modules=["q_proj","k_proj","v_proj","o_proj","gate_proj","up_proj","down_proj"])

   ds = load_dataset("json", data_files="out/train.jsonl", split="train")
   ds = ds.map(lambda e: {"text": tok.apply_chat_template(e["messages"], tokenize=False)})

   SFTTrainer(model=model, tokenizer=tok, train_dataset=ds,
       args=SFTConfig(per_device_train_batch_size=2, gradient_accumulation_steps=4,
           warmup_steps=5, num_train_epochs=2, learning_rate=2e-4, logging_steps=5,
           optim="adamw_8bit", output_dir="out/lora")).train()

   # Export for Ollama/llama.cpp
   model.save_pretrained_gguf("out/hermes-npc", tok, quantization_method="q4_k_m")
   ```
   Small dataset → keep epochs low (1–3) so it doesn't overfit. Watch the loss curve.

5. **Deploy** — drop `hermes-npc.Q4_K_M.gguf` where llama.cpp/Ollama can load it; re-run your
   bake-off scenes. Does it honour anger/fear/exhaustion now? That's the whole test.

## Later: DPO (de-slop)
Once SFT is solid but some slop survives, pair each `discard` (rejected) with the `good`/`edit`
reply for a similar prompt (chosen) and run a DPO pass on top of the SFT LoRA. That's the back
half of the original plan — kills the "helpful assistant" residue prompting couldn't.

## Files
```
convert_tables.py   reviews/*.md + logs/*_log.jsonl -> out/train.jsonl (chat format)
reviews/            curation tables (data_review_batchNN.md)
batteries/          collection prompt sets (battery_batchNN.md)
logs/               archived per-batch training logs
train/              cloud QLoRA notebooks (train_pilot.py, train_pilot_kaggle.py)
Modelfile           ollama create recipe to deploy the fine-tuned GGUF
out/                train.jsonl + the LoRA/GGUF from training (gitignored)
```
Base model and the control vectors (Track B) are weight-specific — **rebuild the vectors on
the fine-tuned weights** after training (see ../control-vectors/README.md).
