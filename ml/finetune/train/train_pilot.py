# ============================================================================
# Track A — PILOT QLoRA fine-tune of Hermes-3-Llama-3.1-8B   (Google Colab, T4)
# ============================================================================
# GOAL: prove the pipeline end-to-end (data -> train -> GGUF -> Ollama) and get
# a first "is this better than base hermes at state->tone AND voice?" signal on the
# ~300-example SFT set (single-turn rows + a few assembled multi-turn arcs). This is
# a DIAGNOSTIC run, not the ship model — but with ~300 rows we now hold out a ~10%
# eval split and WATCH EVAL LOSS, because overfit collapse (train loss -> ~0.015,
# token loops on off-distribution input) is the failure this set is big enough to avoid.
#
# KEPT IN SYNC with train_pilot_kaggle.py — identical training body (cells [4]-[7]);
# only the file-in ([3]) and file-out ([8]/[11]) cells differ for the platform.
#
# WHERE TO RUN: Colab, Runtime -> Change runtime type -> T4 GPU.
# Paste each "# %%" block into its own Colab cell.
#
# ----------------------------------------------------------------------------
# RUN ORDER (top to bottom; exactly ONE manual restart, marked below):
#
#   [1]  install (pinned)          <- then: Runtime > Restart session  (ONCE)
#   ---------------- RESTART HAPPENS HERE, AND ONLY HERE ----------------
#   [2]  post-restart sanity checks
#   [3]  upload train.jsonl                       (manual step: pick the file)
#   [4]  load base model in 4-bit
#   [5]  attach LoRA adapters
#   [6]  build training text
#   [7]  train  (~15-30 min on T4)
#   [8]  SAVE + DOWNLOAD lora_ckpt.zip            (manual step: keep the file!)
#   [9]  quick smoke test
#   [10] GGUF export  (SLOW: 10-30 min, long silent pauses — DO NOT interrupt)
#   [11] download the GGUF (Drive if auth works, browser download if not)
#
# Total manual interventions: the upload in [3], the single restart after [1],
# and grabbing the two downloads ([8] and [11]). Everything else is hands-off.
# ============================================================================

# %% [1] Install — PINNED versions. Run this cell, then RESTART (see banner it prints).
# Unsloth = 2x faster, ~half the VRAM, and does the GGUF export for us.
#
# HARD-WON PINNING RULES (each of these cost a broken session — do not "improve"):
#   * NEVER install unsloth from git-HEAD. HEAD auto-updated to 2026.7.2, which is
#     BROKEN: it cannot load the pre-quantized 4-bit Hermes repo against
#     transformers 5.5.0 ("OSError ... no file named model.safetensors").
#     2026.6.9 is the version that trained successfully — pin it.
#   * NEVER use --force-reinstall. It dragged numpy 2.0.2 -> 2.5.1 mid-session ->
#     "RuntimeError: numpy was upgraded mid-session ... C extensions cannot be
#     reloaded". One plain install, keep Colab's base numpy, then ONE restart.
#   * trl/peft/accelerate/bitsandbytes go in with --no-deps so pip can't drag
#     transformers or numpy up/down underneath unsloth. If TRL's API shifts
#     again, the known deltas are documented in cell [7]'s comments.
#   * Red pip "dependency resolver" warnings are NORMAL here — what matters is
#     the version printout below, not pip's grumbling.
import numpy as _np_before_install   # imported BEFORE pip runs, to detect numpy churn

!pip install -q "unsloth[colab-new]==2026.6.9"
!pip install -q --no-deps --upgrade trl peft accelerate bitsandbytes

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
    print("!! This is the numpy-churn failure mode. The restart below makes the")
    print("!! session consistent again, BUT if any import crashes after the")
    print("!! restart: Runtime > Disconnect and delete runtime, then rerun [1].")
    print("=" * 74)
else:
    print("numpy unchanged on disk — good.")

print()
print("#" * 74)
print("##  INSTALL DONE. NOW RESTART — this is the ONLY restart in the run:  ##")
print("##      Runtime  >  Restart session                                   ##")
print("##  Then continue from cell [2]. Do NOT rerun this cell afterwards.   ##")
print("#" * 74)

# %% [2] Post-restart sanity — catches a skipped restart / missing GPU before any slow work
# NOTE: PYTORCH_CUDA_ALLOC_CONF must be set before torch's FIRST import of the
# session (it curbs the fragmentation OOM later) — and this cell is where torch
# first gets imported after the restart, so it lives here.
import os
os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"
import torch, numpy
from importlib.metadata import version as _v

assert torch.cuda.is_available(), (
    "No GPU. Runtime > Change runtime type > T4 GPU, then rerun from cell [2] "
    "(the install in [1] survives a runtime-type change only if the VM is kept; "
    "if packages are gone, rerun [1] + restart first)."
)
assert numpy.__version__ == _v("numpy"), (
    f"numpy in RAM ({numpy.__version__}) != numpy on disk ({_v('numpy')}). "
    "You SKIPPED the restart after cell [1]. Runtime > Restart session, "
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

# %% [3] Data (skips the upload if train.jsonl survived a session restart)
import os
if os.path.exists("train.jsonl"):
    DATA_PATH = "train.jsonl"
else:
    from google.colab import files
    print("Upload train.jsonl (from ml/finetune/out/):")
    up = files.upload()
    DATA_PATH = next(iter(up))      # e.g. "train.jsonl"
print("using:", DATA_PATH)
with open(DATA_PATH) as f:
    n_lines = sum(1 for line in f if line.strip())
assert n_lines > 0, f"{DATA_PATH} is empty — wrong file uploaded?"
print(f"{n_lines} training lines found.")

# %% [4] Load base model in 4-bit
import os
os.environ.setdefault("PYTORCH_CUDA_ALLOC_CONF", "expandable_segments:True")  # already set in [2] before torch's first import; kept here defensively — curbs fragmentation OOM
# Unsloth's "fast downloading" (hf_transfer) STALLS AT 0% on flaky networks — the model
# download just never starts and the cell hangs forever. Force the plain, slower-but-reliable
# HF downloader. Must be set BEFORE the unsloth import. (Cost us a whole session, 2026-07.)
os.environ["HF_HUB_ENABLE_HF_TRANSFER"] = "0"
import torch

BF16_OK = torch.cuda.get_device_capability(0)[0] >= 8   # Ampere or newer
if not BF16_OK:
    # newer torch claims bf16 "support" on pre-Ampere via emulation; keep every
    # is_bf16_supported() consumer honest (must run before the unsloth import)
    torch.cuda.is_bf16_supported = lambda *a, **k: False

from unsloth import FastLanguageModel

MAX_SEQ = 4096                      # our system prompts (persona + memories + rules) are long
DTYPE   = torch.bfloat16 if BF16_OK else torch.float16
print(torch.cuda.get_device_name(0), "->", DTYPE)

model, tokenizer = FastLanguageModel.from_pretrained(
    model_name     = "NousResearch/Hermes-3-Llama-3.1-8B",
    max_seq_length = MAX_SEQ,
    load_in_4bit   = True,
    dtype          = DTYPE,
)

# THE ROOT-CAUSE FIX for the bf16 GradScaler crash on T4: with load_in_4bit Unsloth
# swaps in its prequantized checkpoint (unsloth/Hermes-3-...-bnb-4bit) whose config
# bakes in bnb_4bit_compute_dtype=bfloat16 + torch_dtype=bfloat16 — overriding the
# dtype we pass. Unsloth's training prep then derives its precision regime from
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

# %% [7] Train (~15-30 min on T4; loss prints every step, so silence here = problem)
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
        max_length                  = 3328,    # cap BELOW MAX_SEQ. Single-turn data max is ~3148 real
                                               # tokens, so those don't truncate. At 4096, TRL's chunked-CE
                                               # materialised an fp32 [seq x 128k-vocab] logits buffer and
                                               # OOM'd the T4. (Multi-turn arcs over this cap are flagged in
                                               # cell [6]; if you have them, use_liger_kernel + raise this.)
        activation_offloading       = True,    # offloads activations to CPU — the other half of the OOM fix
        packing                     = False,   # tiny set — keep examples distinct
        padding_free                = False,   # this trl build defaults it True -> conflicts w/ max_length
        per_device_train_batch_size = 1,       # T4 16GB: batch 2 OOMs on long prompts
        gradient_accumulation_steps = 8,       # effective batch still 8
        warmup_steps                = 5,
        # 2026-07-17 PILOT RESULT — the OLD settings here (3 epochs @ 2e-4) OVERFIT into
        # degeneracy: loss hit 0.015 (healthy SFT sits ~0.5-1.5) midway through epoch 2, and
        # the rest engraved the set in. The result reproduced training rows and collapsed into
        # token loops ("Anyone Anyone Anyone") on anything off-distribution. repeat_penalty does
        # not rescue that. Scale these WITH the dataset:
        #   ~110 rows  -> 1 epoch, 5e-5
        #   ~300+ rows -> 2 epochs, 1e-4   (current — matches the ~300-row pilot set)
        # Watch EVAL loss (printed every eval_steps): if train loss dives below ~0.1 while eval
        # loss stops improving, stop early — that's memorising, not training.
        num_train_epochs            = 2,
        learning_rate               = 1e-4,
        eval_strategy               = "steps", # print eval loss alongside train loss
        eval_steps                  = 5,
        # Match the load dtype chosen in cell [4]: bf16 on Ampere+, fp16 on the T4.
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
# THE FIX THAT HOLDS (T4 / fp16 trainer): trainable LoRA params MUST be float32 —
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
trainer.train()   # watch BOTH losses: train should fall then flatten; if train keeps diving
                  # (toward ~0.015) while EVAL loss flattens/climbs, that's overfit — stop early.

# %% [8] *** SAVE THE ADAPTERS IMMEDIATELY — the safety net. DO NOT SKIP. ***
# The LoRA adapters ARE the trained result and are tiny (~150MB). Save + download
# them BEFORE the slow/fragile GGUF export. If the export dies or the runtime
# restarts, you reload these instead of retraining — export is decoupled from
# training, and you only ever train once.
model.save_pretrained("lora_ckpt"); tokenizer.save_pretrained("lora_ckpt")
import os, shutil
shutil.make_archive("lora_ckpt", "zip", "lora_ckpt")
assert os.path.exists("lora_ckpt.zip") and os.path.getsize("lora_ckpt.zip") > 1_000_000, \
    "lora_ckpt.zip missing or suspiciously small — do not proceed to export until this is fixed."
print(f"lora_ckpt.zip: {os.path.getsize('lora_ckpt.zip')/1e6:.0f} MB — downloading now (seconds, not minutes). KEEP THIS FILE SAFE.")
from google.colab import files
try:
    files.download("lora_ckpt.zip")
except Exception as e:
    print("auto-download failed:", e)
    print("-> grab lora_ckpt.zip manually from the Files pane (folder icon, left sidebar).")
#
# TO RESUME EXPORT ON A FRESH RUNTIME (no retraining):
#   1. run cell [1], do the ONE restart, run cell [2]
#   2. upload lora_ckpt.zip via the Files pane, then in a new cell:
#        import os, zipfile, torch
#        os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"
#        zipfile.ZipFile("lora_ckpt.zip").extractall("lora_ckpt")
#        torch.cuda.is_bf16_supported = lambda *a, **k: False   # T4: must precede the unsloth import
#        from unsloth import FastLanguageModel
#        model, tokenizer = FastLanguageModel.from_pretrained(
#            "lora_ckpt", max_seq_length=4096, load_in_4bit=True, dtype=torch.float16)
#   3. run cells [10] and [11]. That's it — no upload of train.jsonl, no training.

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
# ##  THIS CELL IS SLOW: 10-30 MINUTES ON A T4, with LONG SILENT PAUSES     ##
# ##  (llama.cpp build, fp16 merge, quantize). NO OUTPUT IS NORMAL.         ##
# ##  DO NOT interrupt, DO NOT restart — interrupting here is exactly how   ##
# ##  a finished training run was lost. Walk away; come back to the print.  ##
# ############################################################################
import time
_t0 = time.time()
ct = tokenizer.chat_template
if isinstance(ct, dict):   # transformers 5.x dict-form template; unsloth save.py assumes str (.replace crash)
    tokenizer.chat_template = ct.get("default") or next(iter(ct.values()))
# maximum_memory_usage=0.5 (default 0.75) shards the ~16 GB fp16 merge so it doesn't
# RAM-OOM the box during quantize (bit us on a low-RAM runtime once).
model.save_pretrained_gguf("hermes-npc", tokenizer, quantization_method="q4_k_m", maximum_memory_usage=0.5)
# -> lands in hermes-npc_gguf/ (NOT hermes-npc/) with unsloth's own file name.
# Unsloth also drops its own Modelfile there — IGNORE it; ml/finetune/Modelfile has
# the game's sampling params + the multi-turn .Messages template.
import glob, os
cands = glob.glob("hermes-npc_gguf/**/*.gguf", recursive=True) or glob.glob("**/*.gguf", recursive=True)
print("gguf files:", cands)
assert cands, "No .gguf produced — the export step above failed; read its output."
gguf = next((f for f in cands if "q4_k_m" in f.lower()), cands[0])  # case-insensitive; unsloth may name it either case
print(f"using: {gguf}  ({os.path.getsize(gguf)/1e9:.2f} GB, export took {(time.time()-_t0)/60:.0f} min)")

# %% [11] Get the GGUF out (Drive if auth works; direct browser download if not)
# Drive is the robust path for a ~5GB file (resumable download from drive.google.com),
# but its auth popup has failed before. files.download() needs no auth but is a plain
# browser download — slow, and it must not be interrupted. Try Drive, fall back inline.
import os, shutil
FINAL = "/content/hermes-npc.Q4_K_M.gguf"   # name must match ml/finetune/Modelfile's FROM line
if not os.path.exists(FINAL):
    try:
        os.link(gguf, FINAL)                # hardlink: instant, no extra disk
    except OSError:
        shutil.copy(gguf, FINAL)
print(f"staged: {FINAL} ({os.path.getsize(FINAL)/1e9:.2f} GB)")

try:
    from google.colab import drive
    drive.mount("/content/drive")           # auth popup — approve it in the browser
    shutil.copy(FINAL, "/content/drive/MyDrive/hermes-npc.Q4_K_M.gguf")
    print("DONE — download hermes-npc.Q4_K_M.gguf from drive.google.com into ml/finetune/")
except Exception as e:
    print("Drive path failed:", e)
    print("Falling back to direct browser download (no auth; leave the tab open until it finishes)...")
    from google.colab import files
    files.download(FINAL)

# ============================================================================
# NEXT (locally, in Ollama):
#   1. Put hermes-npc.Q4_K_M.gguf in ml/finetune/ next to the existing Modelfile.
#   2. ollama create hermes-npc -f Modelfile
#   3. In the game's model picker choose hermes-npc, and use `compare` to put it
#      next to base hermes3:8b on the scenes base fails (anger->curt, exhaustion->terse,
#      Solem's silence). THAT is the moment of truth.
# ============================================================================
