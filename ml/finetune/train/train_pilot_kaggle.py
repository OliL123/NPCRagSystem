# ============================================================================
# Track A — PILOT QLoRA fine-tune, KAGGLE variant of train_pilot.py
# ============================================================================
# Same training as train_pilot.py (ALL the Colab-era fixes included, same cell
# numbers, identical training body so the two files don't drift); only the
# file-in / file-out cells differ. Use when Colab won't hand out a GPU —
# Kaggle gives ~30 GPU-hours/week, quota separate from Colab.
#
# ONE-TIME SETUP on kaggle.com (needs a phone-verified account for GPU+internet):
#   1. Create -> New Notebook.
#   2. Right panel -> Session options: Accelerator = "GPU T4 x2", Internet = ON.
#   3. Right panel -> Input -> "+ Add Input" -> "Upload" -> New Dataset:
#      name it  npc-train  and give it ml/finetune/out/train.jsonl.
#      It mounts read-only at /kaggle/input/npc-train/train.jsonl.
# Paste each "# %%" block into its own cell (Kaggle: Ctrl+Shift+Minus splits).
#
# ----------------------------------------------------------------------------
# RUN ORDER (top to bottom; exactly ONE manual restart, marked below):
#
#   [1]  install (pinned)     <- then: Run > Restart session  (ONCE)
#   ---------------- RESTART HAPPENS HERE, AND ONLY HERE ----------------
#   [2]  post-restart sanity checks
#   [3]  locate train.jsonl (attached dataset — no upload prompt)
#   [4]  load base model in 4-bit
#   [5]  attach LoRA adapters
#   [6]  build training text
#   [7]  train
#   [8]  SAVE lora_ckpt.zip to /kaggle/working   (safety net on disk)
#   [8.5] PUSH ADAPTERS TO HF                    (permanent backup — set your HF_TOKEN)
#   [9]  quick smoke test
#   [10] GGUF export  (SLOW: 10-30 min, long silent pauses — DO NOT interrupt)
#   [11] stage the GGUF in /kaggle/working
#   [11.5] PUSH GGUF TO HF  ->  download home with 'hf download' (NOT Kaggle's stalling button)
#
# Kaggle restart note: the attached dataset stays mounted and /kaggle/working
# survives a session restart (not a session shutdown).
# ============================================================================

# %% [1] Install — PINNED versions. Run this cell, then RESTART (see banner it prints).
# HARD-WON PINNING RULES (same as Colab — each cost a broken session):
#   * NEVER install unsloth from git-HEAD. HEAD auto-updated to 2026.7.2, which is
#     BROKEN: it cannot load the pre-quantized 4-bit Hermes repo against
#     transformers 5.5.0 ("OSError ... no file named model.safetensors").
#     2026.6.9 is the version that trained successfully — pin it.
#   * NEVER use --force-reinstall. On Colab it dragged numpy 2.0.2 -> 2.5.1
#     mid-session -> "RuntimeError: numpy was upgraded mid-session". One plain
#     install, then ONE restart.
#   * trl/peft/accelerate/bitsandbytes go in with --no-deps so pip can't drag
#     transformers or numpy up/down underneath unsloth.
#   * KAGGLE CAVEAT: Kaggle's base image pins its own torch/transformers/numpy
#     and they differ from Colab's (and change with Kaggle's environment
#     releases). The unsloth==2026.6.9 pin is the anchor; if pip's resolver has
#     to move numpy here it's MORE likely than on Colab — the warning below
#     flags it. If imports crash after the restart, use Session options ->
#     "Factory reset" (or a fresh notebook) and rerun [1]. Do NOT start adding
#     --force-reinstall to fight it.
#   * Red pip "dependency resolver" warnings are NORMAL — trust the version
#     printout below, not pip's grumbling.
import numpy as _np_before_install   # imported BEFORE pip runs, to detect numpy churn

!pip install -q "unsloth[colab-new]==2026.6.9"
!pip install -q --no-deps --upgrade trl peft accelerate bitsandbytes
!pip install -q --no-deps liger-kernel   # fused CE: computes loss in chunks, never builds the fp32 [seq x 128k] logits buffer, so 4608 fits the T4

# --- verify what actually landed on disk (metadata reads disk, not RAM) ---
from importlib.metadata import version as _v
for _pkg in ("unsloth", "trl", "peft", "transformers", "numpy", "torch"):
    try:
        print(f"  {_pkg:14s} {_v(_pkg)}")
    except Exception as _e:
        print(f"  {_pkg:14s} NOT FOUND ({_e})")
assert _v("unsloth") == "2026.6.9", (
    f"unsloth on disk is {_v('unsloth')}, not 2026.6.9 — the pin failed. "
    "Do NOT proceed: rerun this cell and read pip's output."
)
if _v("numpy") != _np_before_install.__version__:
    print("=" * 74)
    print(f"!! WARNING: the install changed numpy on disk "
          f"({_np_before_install.__version__} -> {_v('numpy')}).")
    print("!! The restart below makes the session consistent again, BUT if any")
    print("!! import crashes after the restart: Factory reset the session")
    print("!! (Session options) and rerun [1].")
    print("=" * 74)
else:
    print("numpy unchanged on disk — good.")

print()
print("#" * 74)
print("##  INSTALL DONE. NOW RESTART — this is the ONLY restart in the run:  ##")
print("##      Run  >  Restart session   (circular-arrow button also works)  ##")
print("##  Then continue from cell [2]. Do NOT rerun this cell afterwards.   ##")
print("#" * 74)

# %% [2] Post-restart sanity — catches a skipped restart / missing GPU before any slow work
# NOTE: PYTORCH_CUDA_ALLOC_CONF must be set before torch's FIRST import of the
# session — and this cell is where torch first gets imported after the restart.
# CUDA_VISIBLE_DEVICES too: T4 x2 session, but Unsloth free is single-GPU.
import os
os.environ["CUDA_VISIBLE_DEVICES"] = "0"
os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"
import torch, numpy
from importlib.metadata import version as _v

assert torch.cuda.is_available(), (
    "No GPU. Session options -> Accelerator = 'GPU T4 x2' (and Internet = ON), "
    "then rerun [1] + the one restart if the fresh session lost the installs."
)
assert numpy.__version__ == _v("numpy"), (
    f"numpy in RAM ({numpy.__version__}) != numpy on disk ({_v('numpy')}). "
    "You SKIPPED the restart after cell [1]. Run > Restart session, "
    "then run again from THIS cell. (Restart once — not more.)"
)
assert _v("unsloth") == "2026.6.9", (
    f"unsloth is {_v('unsloth')}, expected the pinned 2026.6.9 — rerun cell [1] "
    "and restart once."
)
print("GPU:", torch.cuda.get_device_name(0))
print("torch", torch.__version__, "| numpy", numpy.__version__,
      "| unsloth", _v("unsloth"), "| trl", _v("trl"),
      "| transformers", _v("transformers"))
print("sanity OK — run straight down from here, no more restarts.")

# %% [3] Data — comes from the attached Kaggle dataset, no upload prompt
import glob
matches = glob.glob("/kaggle/input/**/train.jsonl", recursive=True)
assert matches, "No train.jsonl found — attach the 'npc-train' dataset (+ Add Input)."
DATA_PATH = matches[0]
print("using:", DATA_PATH)
with open(DATA_PATH) as f:
    n_lines = sum(1 for line in f if line.strip())
assert n_lines > 0, f"{DATA_PATH} is empty — wrong file in the dataset?"
print(f"{n_lines} training lines found.")

# %% [4] Load base model in 4-bit
import os
os.environ.setdefault("CUDA_VISIBLE_DEVICES", "0")                              # already set in [2]; kept defensively
os.environ.setdefault("PYTORCH_CUDA_ALLOC_CONF", "expandable_segments:True")    # already set in [2] before torch's first import; curbs fragmentation OOM
# Unsloth's "fast downloading" (hf_transfer) STALLS AT 0% on flaky networks — the model
# download never starts and the cell hangs forever. Force the plain, reliable HF downloader.
# Must be set BEFORE the unsloth import. (Cost us a whole session, 2026-07.)
os.environ["HF_HUB_ENABLE_HF_TRANSFER"] = "0"
import torch

BF16_OK = torch.cuda.get_device_capability(0)[0] >= 8   # T4 (7.5) and P100 (6.0) -> False
if not BF16_OK:
    # newer torch claims bf16 "support" on pre-Ampere via emulation; keep every
    # is_bf16_supported() consumer honest (must run before the unsloth import)
    torch.cuda.is_bf16_supported = lambda *a, **k: False

from unsloth import FastLanguageModel

MAX_SEQ = 4608                      # liger ON -> room for the ~3.6k-token max example; nothing truncates
DTYPE   = torch.bfloat16 if BF16_OK else torch.float16
print(torch.cuda.get_device_name(0), "->", DTYPE)

model, tokenizer = FastLanguageModel.from_pretrained(
    model_name     = "NousResearch/Hermes-3-Llama-3.1-8B",
    max_seq_length = MAX_SEQ,
    load_in_4bit   = True,
    dtype          = DTYPE,
)

# THE ROOT-CAUSE FIX for the bf16 GradScaler crash on pre-Ampere: with load_in_4bit
# Unsloth swaps in its prequantized checkpoint (unsloth/Hermes-3-...-bnb-4bit) whose
# config bakes in bnb_4bit_compute_dtype=bfloat16 + torch_dtype=bfloat16 — overriding
# the dtype we pass. Unsloth's training prep then derives its precision regime from
# model.config INSIDE the training path, so bf16 grads reappear no matter what we do
# to params beforehand. Scrub bf16 from the loaded model BEFORE get_peft_model, so
# the LoRA layers and every quantized matmul are created/computed in DTYPE.
if not BF16_OK:
    from bitsandbytes.nn import Linear4bit
    model.config.torch_dtype = DTYPE
    if hasattr(model.config, "dtype"):          # transformers 5.x name
        model.config.dtype = DTYPE
    n_c = n_q = 0
    for m in model.modules():
        if isinstance(m, Linear4bit):
            if m.compute_dtype == torch.bfloat16:
                m.compute_dtype = DTYPE; n_c += 1
            qs = getattr(m.weight, "quant_state", None)
            if qs is not None and getattr(qs, "dtype", None) == torch.bfloat16:
                qs.dtype = DTYPE; n_q += 1
    print(f"de-bf16'd: {n_c} compute_dtype, {n_q} quant_state; config -> {model.config.torch_dtype}")

# %% [5] Attach LoRA adapters (this is the QLoRA part)
model = FastLanguageModel.get_peft_model(
    model,
    r              = 16,
    lora_alpha     = 16,
    lora_dropout   = 0.05,          # a little dropout since the set is tiny
    target_modules = ["q_proj","k_proj","v_proj","o_proj",
                      "gate_proj","up_proj","down_proj"],
    bias           = "none",
    use_gradient_checkpointing = "unsloth",
    random_state   = 3407,
)
# (No manual dtype fixups here: an upcast-to-fp32 pass was tried and did NOT survive —
# Unsloth recasts trainable params again inside trainer.train(). The is_bf16_supported
# override in cell [4] is the fix that holds, because it runs before Unsloth imports.)

# %% [6] Build the training text (Hermes-3 = ChatML; the tokenizer knows it)
from datasets import load_dataset

ds = load_dataset("json", data_files=DATA_PATH, split="train")

def to_text(ex):
    # ex["messages"] = [system, user, assistant] -> one ChatML string
    return {"text": tokenizer.apply_chat_template(ex["messages"], tokenize=False)}

ds = ds.map(to_text, remove_columns=ds.column_names)
assert len(ds) > 0, "Dataset came out empty — is train.jsonl one {'messages': [...]} object per line?"

# --- token-length audit. Multi-turn arcs are long: the system prompt alone is ~3.3-3.6k
# tokens (persona + memories + lore + rules), and an arc stacks several user/assistant
# turns on top. Anything over max_length (cell [7]) is TRUNCATED FROM THE END, which
# silently chops an assistant target — the worst kind of corruption. Print the spread so
# an over-long arc is visible, not silent.
_MAXLEN = 3328   # keep in sync with cell [7]'s max_length
_lens = sorted(len(tokenizer(t)["input_ids"]) for t in ds["text"])
print(f"token lengths: min {_lens[0]}, median {_lens[len(_lens)//2]}, "
      f"p95 {_lens[int(len(_lens)*0.95)]}, max {_lens[-1]}")
_over = sum(1 for n in _lens if n > _MAXLEN)
if _over:
    print(f"!! {_over} example(s) exceed max_length={_MAXLEN} and WILL be truncated "
          f"(longest {_lens[-1]}). If these are multi-turn arcs you'll lose late turns.\n"
          f"!! Fix options: set use_liger_kernel=True in cell [7] (fused CE never builds the "
          f"fp32 logits buffer, so you can raise MAX_SEQ + max_length without OOM), or drop/"
          f"split the over-long rows before training.")

# --- held-out eval split (~10%): the anti-collapse instrument. Watch EVAL loss, not train
# loss. Train loss sliding toward ~0.015 while eval loss flattens or climbs = memorising.
_split = ds.train_test_split(test_size=0.1, seed=3407)
ds_train, ds_eval = _split["train"], _split["test"]
print(f"{len(ds_train)} train / {len(ds_eval)} eval examples. Sample:\n", ds_train[0]["text"][:400], "...\n")

# %% [7] Train (loss prints every step, so silence here = problem)
# NOTE: modern TRL puts dataset_text_field / max_length / packing INTO SFTConfig, and takes
# `processing_class=` instead of the old `tokenizer=` kwarg.
# TRL >= 0.20 renamed SFTConfig's `max_seq_length` to `max_length` (the old name now raises
# TypeError: unexpected keyword argument). On a pre-0.20 build, rename it back.
from trl import SFTTrainer, SFTConfig

trainer = SFTTrainer(
    model            = model,
    processing_class = tokenizer,   # new TRL name for the old `tokenizer=`; Unsloth needs it
    train_dataset    = ds_train,
    eval_dataset     = ds_eval,     # the held-out ~10% from cell [6] — watch its loss
    args = SFTConfig(
        dataset_text_field          = "text",
        max_length                  = 3328,    # liger OFF -> the standard fp32-logits CE is back, so cap at 3328 to fit
                                               # the T4. The longest ~6 arcs truncate their last turn (acceptable to test).
        use_liger_kernel            = False,   # 2026-08 TEST: liger was ON in EVERY degenerate run this session. It is a
                                               # fused fp16 CE kernel; a bogus near-zero loss + garbage grads from it would
                                               # explain the 0.01-loss-on-unseen-data AND the collapse. OFF to isolate it.
        activation_offloading       = True,    # offloads activations to CPU — the other half of the mem fix.
        packing                     = False,   # tiny set — keep examples distinct
        padding_free                = False,   # this trl build defaults it True -> conflicts w/ max_length
        per_device_train_batch_size = 1,       # 16GB card: batch 2 OOMs on long prompts
        gradient_accumulation_steps = 8,       # effective batch still 8
        warmup_steps                = 5,
        # 2026-07-17 PILOT RESULT — the OLD settings here (3 epochs @ 2e-4) OVERFIT into
        # degeneracy: loss hit 0.015 (healthy SFT sits ~0.5-1.5) by step 15, i.e. midway
        # through epoch 2, and the remaining ~27 steps engraved the set into the weights.
        # The result reproduced training rows perfectly and collapsed into token loops
        # ("Anyone Anyone Anyone") on anything off-distribution. repeat_penalty does not
        # rescue that — it only changes which word loops. Scale these WITH the dataset:
        #   ~110 rows  -> 1 epoch, 5e-5
        #   ~300+ rows -> 2 epochs, 1e-4   (current — matches the ~300-row pilot set)
        # Watch EVAL loss (printed every eval_steps): if train loss dives below ~0.1 while
        # eval loss stops improving, stop early — that's memorising, not training.
        num_train_epochs            = 1,       # 2ep/1e-4 collapsed fast on the 309-set (like the old 3ep/2e-4)
        learning_rate               = 5e-5,    # back to 5e-5: 1e-5 ALSO cooked, so LR is ruled out. Testing liger OFF at
                                               # a normal LR -- if THIS run is coherent, liger's fused fp16 CE was the poison.
        eval_strategy               = "no",    # eval OOMs on the T4 at 4608: liger's fused CE is TRAIN-only,
                                               # eval falls back to upcasting the full fp32 [seq x 128k] logits.
                                               # Overfit check here = train loss + the cell [9] smoke test.
        # eval_steps                = 5,       # (re-enable with eval_strategy="steps" on a bigger GPU)
        # Match the load dtype chosen in cell [4]: bf16 on Ampere+, fp16 on T4/P100.
        # (The fp32 adapter upcast below makes either scaler path safe.)
        fp16 = not BF16_OK,
        bf16 = BF16_OK,
        logging_steps     = 1,
        optim             = "adamw_8bit",
        weight_decay      = 0.01,
        lr_scheduler_type = "cosine",
        seed              = 3407,
        output_dir        = "outputs",
        report_to         = "none",
    ),
)
# --- RESPONSE-ONLY LOSS MASKING (the fix for the 2026-08 collapse) ---
# WITHOUT this, SFTTrainer grades the model on the ENTIRE formatted string,
# including the ~3.3k-token system prompt that is near-identical every row. The
# model just memorises that boilerplate: train loss craters to ~0.015 and
# generation degenerates into token loops that regurgitate the system block
# ("[CRITICAL FORMATTING RULES]..."). Standard SFT masks everything up to the
# assistant turn so loss lands ONLY on the reply we want it to learn. Hermes-3 is
# ChatML; the assert fails loud if the tokenizer applies a different template
# (wrong markers would blank the whole sequence -> loss ~0, learns nothing).
# 2026-08 REGRESSION TEST: the 2026-07-06 run that WORKED on this T4 (loss 3.0->1.6,
# coherent curt-anger probe) had NO masking. Masking was added this session and is present
# in every collapsing run. So masking is the prime suspect. USE_MASKING=False reproduces the
# July-style full-sequence training. If THIS run is coherent -> masking (as applied on this
# unsloth/trl build) was the poison, not the hardware. Flip back to True only if it helps.
USE_MASKING = False
if USE_MASKING:
    from unsloth.chat_templates import train_on_responses_only
    _INSTR = "<|im_start|>user\n"
    _RESP  = "<|im_start|>assistant\n"
    assert _RESP in ds_train[0]["text"], (
        "ChatML assistant header not found in the formatted text -- the tokenizer is "
        "applying a DIFFERENT template. Print ds_train[0]['text'] and set _INSTR/_RESP "
        "to match before training.")
    trainer = train_on_responses_only(trainer, instruction_part=_INSTR, response_part=_RESP)
    print("response-only masking ON -- loss is computed on assistant turns only.")
else:
    print("response-only masking OFF -- full-sequence training (reproduces the July run).")

# THE FIX THAT HOLDS (pre-Ampere / fp16 trainer): trainable LoRA params MUST be float32 —
# the fp16 GradScaler hard-rejects fp16 grads (ValueError) and has no bf16 kernel
# (NotImplementedError); fp32 master weights under fp16 autocast is standard QLoRA.
# Must sit AFTER SFTTrainer construction, right before .train(): casts here survive
# into training (verified 2026-07-06), earlier ones were undone by Unsloth's prep.
from collections import Counter
if not BF16_OK:
    n = 0
    for _, p in model.named_parameters():
        if p.requires_grad and p.dtype != torch.float32:
            p.data = p.data.to(torch.float32); n += 1
    print(f"cast {n} trainable params to float32")
print("trainable dtypes:", Counter(str(p.dtype) for p in model.parameters() if p.requires_grad))
trainer.train()   # eval is off (OOMs on T4 at 4608), so watch TRAIN loss: it should fall then flatten
                  # (earlier 78-example run: ~3.0 -> ~1.6 over 30 steps). If it dives toward ~0.015 that's
                  # memorising — stop early. The cell [9] smoke test (trained-Q vs novel-Q) is the real check.

# %% [8] *** SAVE THE ADAPTERS IMMEDIATELY — the safety net. DO NOT SKIP. ***
# The LoRA adapters ARE the trained result and are tiny (~150MB). Save + download
# them BEFORE the slow/fragile GGUF export. If the export dies or the session
# restarts, you reload these instead of retraining — export is decoupled from
# training, and you only ever train once.
model.save_pretrained("lora_ckpt"); tokenizer.save_pretrained("lora_ckpt")
import os, shutil
shutil.make_archive("/kaggle/working/lora_ckpt", "zip", "lora_ckpt")
_zip = "/kaggle/working/lora_ckpt.zip"
assert os.path.exists(_zip) and os.path.getsize(_zip) > 1_000_000, \
    "lora_ckpt.zip missing or suspiciously small — do not proceed to export until this is fixed."
print(f"{_zip}: {os.path.getsize(_zip)/1e6:.0f} MB")
print("DOWNLOAD IT NOW: right panel -> Output -> /kaggle/working -> lora_ckpt.zip. KEEP IT SAFE.")
#
# TO RESUME EXPORT ON A FRESH SESSION (no retraining):
#   1. add lora_ckpt.zip as a Kaggle dataset (+ Add Input -> Upload), or re-upload it
#   2. run cell [1], do the ONE restart, run cell [2], then in a new cell:
#        import os, glob, zipfile, torch
#        os.environ["CUDA_VISIBLE_DEVICES"] = "0"
#        os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"
#        zipfile.ZipFile(glob.glob("/kaggle/input/**/lora_ckpt.zip", recursive=True)[0]).extractall("lora_ckpt")
#        torch.cuda.is_bf16_supported = lambda *a, **k: False   # T4/P100: must precede the unsloth import
#        from unsloth import FastLanguageModel
#        model, tokenizer = FastLanguageModel.from_pretrained(
#            "lora_ckpt", max_seq_length=4096, load_in_4bit=True, dtype=torch.float16)
#   3. run cells [10] and [11]. No dataset attach for train.jsonl, no training.

# %% [8.5] Push adapters to Hugging Face — permanent, offsite backup (~160 MB, fast + resumable).
# Do this BEFORE the slow export: if the session dies mid-export you reload from HF, no retrain.
# Fill HF_TOKEN below (Kaggle Add-ons -> Secrets is cleaner; hardcoding is fine for a private run).
from huggingface_hub import HfApi
HF_TOKEN  = "hf_PUT_YOUR_TOKEN_HERE"                  # <-- your token from hf.co/settings/tokens (write)
LORA_REPO = "DarkSparktheVoid/hermes-npc-lora"
_api = HfApi()
_api.create_repo(LORA_REPO, token=HF_TOKEN, repo_type="model", exist_ok=True)
_api.upload_folder(folder_path="lora_ckpt", repo_id=LORA_REPO, token=HF_TOKEN)
print(f"adapters pushed -> hf.co/{LORA_REPO}")
print(f"  reload later with: FastLanguageModel.from_pretrained('{LORA_REPO}', token=HF_TOKEN, load_in_4bit=True)")

# %% [9] Eyeball BEFORE export — voice adherence AND a memorisation check.
# The fine-tune must GENERALISE the voice, not memorise rows. So probe each real persona
# TWICE: the exact question it was TRAINED on (should reproduce closely — fine) and a NOVEL
# question it never saw (should stay in-voice but answer freshly). If the novel answer
# degenerates into loops, or just parrots the trained answer, that's memorising not learning.
import json
FastLanguageModel.for_inference(model)

def ask(system, user, n=90):
    msgs = [{"role":"system","content":system}, {"role":"user","content":user}]
    ids = tokenizer.apply_chat_template(msgs, tokenize=True, add_generation_prompt=True,
                                        return_tensors="pt").to("cuda")
    out = model.generate(input_ids=ids, max_new_tokens=n, temperature=0.85, top_p=0.9)
    return tokenizer.decode(out[0][ids.shape[1]:], skip_special_tokens=True).strip()

rows   = [json.loads(l) for l in open(DATA_PATH) if l.strip()]
sys_of = lambda r: next(m["content"] for m in r["messages"] if m["role"] == "system")
usr_of = lambda r: next(m["content"] for m in r["messages"] if m["role"] == "user")

# pick a question guaranteed NOT to be a verbatim training row (true off-distribution probe)
trained_qs = {usr_of(r).strip().lower() for r in rows}
NOVEL = ["So what's your story?", "Anything strange happen round here lately?",
         "What do you make of this weather?", "Busy today?"]
novel_q = next((q for q in NOVEL if q.lower() not in trained_qs), NOVEL[0])

for r in (rows[0], rows[len(rows)//2], rows[-1]):      # spread across the file / characters
    s = sys_of(r)
    print("=" * 72)
    print("PERSONA:", s[:80].replace("\n", " "), "...")
    print("  TRAINED Q:", usr_of(r))
    print("     ->", ask(s, usr_of(r)))
    print("  NOVEL   Q:", novel_q, "   (never seen in training)")
    print("     ->", ask(s, novel_q))

# %% [10] Export to GGUF for Ollama / llama.cpp
# ############################################################################
# ##  THIS CELL IS SLOW: 10-30 MINUTES, with LONG SILENT PAUSES             ##
# ##  (llama.cpp build, fp16 merge, quantize). NO OUTPUT IS NORMAL.         ##
# ##  DO NOT interrupt, DO NOT restart — interrupting here is exactly how   ##
# ##  a finished training run was lost. Walk away; come back to the print.  ##
# ############################################################################
import time
_t0 = time.time()
ct = tokenizer.chat_template
if isinstance(ct, dict):   # transformers 5.x dict-form template; unsloth save.py assumes str (.replace crash)
    tokenizer.chat_template = ct.get("default") or next(iter(ct.values()))
# Export to /tmp, NOT /kaggle/working: the fp16 merge is ~16 GB and /kaggle/working caps at 20 GB
# (it filled mid-export and the run died). /tmp is on the ~1 TB overlay. maximum_memory_usage=0.5
# (default 0.75) shards the merge so it doesn't also RAM-OOM the ~29 GB box.
model.save_pretrained_gguf("/tmp/hermes-npc", tokenizer, quantization_method="q4_k_m", maximum_memory_usage=0.25)  # 0.5 OOM'd the free-box RAM on the fp16 merge -> shard harder
# -> lands in /tmp/hermes-npc_gguf/ with unsloth's own file name.
# Unsloth also drops its own Modelfile there — IGNORE it; ml/finetune/Modelfile has
# the game's sampling params + the multi-turn .Messages template.
import glob, os
cands = glob.glob("/tmp/hermes-npc_gguf/**/*.gguf", recursive=True) or glob.glob("/tmp/**/*.gguf", recursive=True)
print("gguf files:", cands)
assert cands, "No .gguf produced — the export step above failed; read its output."
gguf = next((f for f in cands if "q4_k_m" in f.lower()), cands[0])  # case-insensitive; unsloth may name it either case
print(f"using: {gguf}  ({os.path.getsize(gguf)/1e9:.2f} GB, export took {(time.time()-_t0)/60:.0f} min)")

# %% [11] Stage the GGUF in /kaggle/working — downloadable from the Output pane, no auth
import os, shutil
FINAL = "/kaggle/working/hermes-npc.Q4_K_M.gguf"   # name must match ml/finetune/Modelfile's FROM line
if not os.path.exists(FINAL):
    shutil.move(gguf, FINAL)
print(f"READY: {FINAL} ({os.path.getsize(FINAL)/1e9:.2f} GB)")

# %% [11.5] Push the GGUF to Hugging Face — the fix for the download nightmare. Pull it home from
# HF's fast, resumable CDN (one command) instead of Kaggle's stalling Output button, and it survives
# the session dying. This is the step that turned last time's multi-hour fight into a non-event.
from huggingface_hub import HfApi
HF_TOKEN  = "hf_PUT_YOUR_TOKEN_HERE"                  # same token as cell [8.5]
GGUF_REPO = "DarkSparktheVoid/hermes-npc-gguf"
_api = HfApi()
_api.create_repo(GGUF_REPO, token=HF_TOKEN, repo_type="model", exist_ok=True)
_api.upload_file(path_or_fileobj=FINAL, path_in_repo="hermes-npc.Q4_K_M.gguf",
                 repo_id=GGUF_REPO, token=HF_TOKEN)
print(f"GGUF pushed -> hf.co/{GGUF_REPO}  (Kaggle can die now — it's safe.)")
print(f"LOCAL DOWNLOAD:  hf download {GGUF_REPO} hermes-npc.Q4_K_M.gguf --local-dir .")

# ============================================================================
# NEXT (locally): put hermes-npc.Q4_K_M.gguf in ml/finetune/, then from that folder:
#   ollama create hermes-npc -f Modelfile
# and in-game `compare` vs hermes3:8b on anger->curt / exhaustion->terse / Solem.
# ============================================================================
