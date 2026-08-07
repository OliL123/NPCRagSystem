# ============================================================================
# Track A — PILOT QLoRA fine-tune of Hermes-3-Llama-3.1-8B   (bf16: Colab Pro L4/A100)
# ============================================================================
# WHY THIS FILE EXISTS (2026-08):
#   The T4 scripts (train_pilot.py / _kaggle.py) produced a CORRUPT adapter:
#   train loss nose-dived to ~0.015 and generation was garbage even on the
#   TRAINED questions. We proved it wasn't the data, masking, LR, or liger:
#     - masking VERIFIED correct (loss graded only on the ~65 reply tokens),
#     - base model (no adapter) on the SAME path generated PERFECT in-voice NPCs.
#   So the model, data, prompt and inference path are all fine. The ONLY broken
#   step is fp16 TRAINING of this checkpoint. Hermes' 4-bit repo is baked for
#   bf16 (bnb_4bit_compute_dtype=bfloat16); the T4 has no bf16, so the T4 scripts
#   are a pile of hacks forcing bf16 -> fp16, and the fp16 backward/optimizer path
#   corrupts the LoRA. On a bf16 GPU that entire class of problem does not exist.
#
# WHAT'S DIFFERENT FROM THE T4 SCRIPT (all HACKS REMOVED, nothing added):
#   - no liger-kernel (bf16 CE fits 4608 on a 24GB L4 with room to spare)
#   - no is_bf16_supported() override, no de-bf16 surgery on the 4-bit layers
#   - no fp32 trainable-param upcast, no GradScaler dance (bf16 needs none)
#   - eval is turned back ON (the L4 has the memory) — WATCH EVAL LOSS
#   - masking (train_on_responses_only) KEPT — it was the one real fix from the T4 saga
#
# WHERE TO RUN: Colab Pro, Runtime -> Change runtime type -> L4 GPU (or A100).
#   L4 = 24GB, Ada (compute 8.9), native bf16. A100 works too (overkill). A T4 or
#   P100 will FAIL cell [2]'s bf16 assert — use train_pilot.py for those.
# Paste each "# %%" block into its own Colab cell.
#
# RUN ORDER (top to bottom; exactly ONE manual restart, after [1]):
#   [1] install  -> Runtime > Restart session (ONCE)  |  [2] sanity  |  [3] data
#   [4] load 4-bit  |  [5] LoRA  |  [6] build text + eval split  |  [7] train
#   [8] save+download adapters  |  [9] smoke test  |  [10] GGUF export  |  [11] download GGUF
# ============================================================================

# %% [1] Install — PINNED versions. Run this cell, then RESTART (see banner it prints).
# The pins are hard-won (each cost a broken session) — keep them:
#   * unsloth pinned to 2026.6.9 — git-HEAD (2026.7.2) can't load the 4-bit Hermes repo.
#   * NEVER --force-reinstall (drags numpy mid-session -> "numpy upgraded" crash).
#   * trl/peft/accelerate/bitsandbytes with --no-deps so pip can't move transformers/numpy.
#   * Red pip "dependency resolver" warnings are NORMAL — trust the version printout below.
# NOTE vs the T4 script: NO liger-kernel install — bf16 doesn't need it.
import numpy as _np_before_install   # imported BEFORE pip, to detect numpy churn

!pip install -q "unsloth[colab-new]==2026.6.9"
!pip install -q --no-deps --upgrade trl peft accelerate bitsandbytes

from importlib.metadata import version as _v
for _pkg in ("unsloth", "trl", "peft", "transformers", "numpy", "torch"):
    try:
        print(f"  {_pkg:14s} {_v(_pkg)}")
    except Exception as _e:
        print(f"  {_pkg:14s} NOT FOUND ({_e})")
assert _v("unsloth") == "2026.6.9", (
    f"unsloth on disk is {_v('unsloth')}, not 2026.6.9 — the pin failed. "
    "Do NOT proceed: rerun this cell and read pip's output.")
if _v("numpy") != _np_before_install.__version__:
    print("=" * 74)
    print(f"!! WARNING: the install changed numpy on disk "
          f"({_np_before_install.__version__} -> {_v('numpy')}).")
    print("!! The restart below fixes the session; if an import still crashes after")
    print("!! it, Runtime > Disconnect and delete runtime, then rerun [1].")
    print("=" * 74)
else:
    print("numpy unchanged on disk — good.")
print()
print("#" * 74)
print("##  INSTALL DONE. NOW RESTART — the ONLY restart in the run:           ##")
print("##      Runtime  >  Restart session                                   ##")
print("##  Then continue from cell [2]. Do NOT rerun this cell afterwards.   ##")
print("#" * 74)

# %% [2] Post-restart sanity — catches a skipped restart, a wrong GPU, or a missing bf16 GPU
import os
os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"  # before torch's first import
import torch, numpy
from importlib.metadata import version as _v

assert torch.cuda.is_available(), (
    "No GPU. Runtime > Change runtime type > L4 GPU, then rerun from cell [2].")
# THE key check for this script: native bf16 (compute capability >= 8.0).
_cap = torch.cuda.get_device_capability(0)
assert _cap[0] >= 8, (
    f"{torch.cuda.get_device_name(0)} (compute {_cap[0]}.{_cap[1]}) has NO native bf16. "
    "This bf16 script needs an L4/A100 (Runtime > Change runtime type). "
    "A T4/P100 will corrupt the adapter here — use train_pilot.py for those instead.")
assert numpy.__version__ == _v("numpy"), (
    f"numpy in RAM ({numpy.__version__}) != on disk ({_v('numpy')}). You SKIPPED the "
    "restart after cell [1]. Runtime > Restart session, then run from THIS cell.")
assert _v("unsloth") == "2026.6.9", (
    f"unsloth is {_v('unsloth')}, expected 2026.6.9 — rerun cell [1] and restart once.")
print("GPU:", torch.cuda.get_device_name(0), f"(compute {_cap[0]}.{_cap[1]}, bf16 OK)")
print("torch", torch.__version__, "| numpy", numpy.__version__,
      "| unsloth", _v("unsloth"), "| trl", _v("trl"), "| transformers", _v("transformers"))
print("sanity OK — run straight down from here, no more restarts.")

# %% [3] Data (skips the upload if train.jsonl survived a session restart)
import os
if os.path.exists("train.jsonl"):
    DATA_PATH = "train.jsonl"
else:
    from google.colab import files
    print("Upload train.jsonl (from ml/finetune/out/):")
    up = files.upload()
    DATA_PATH = next(iter(up))
print("using:", DATA_PATH)
with open(DATA_PATH) as f:
    n_lines = sum(1 for line in f if line.strip())
assert n_lines > 0, f"{DATA_PATH} is empty — wrong file uploaded?"
print(f"{n_lines} training lines found.")

# %% [4] Load base model in 4-bit — NATIVE bf16, no dtype surgery
import os
os.environ.setdefault("PYTORCH_CUDA_ALLOC_CONF", "expandable_segments:True")
# hf_transfer STALLS AT 0% on flaky networks — force the plain reliable downloader.
os.environ["HF_HUB_ENABLE_HF_TRANSFER"] = "0"
import torch
from unsloth import FastLanguageModel

MAX_SEQ = 4608                       # bf16 CE fits this on a 24GB L4 -> nothing truncates
DTYPE   = torch.bfloat16             # native here; this is the checkpoint's baked dtype, so
                                     # load_in_4bit "just works" — NONE of the T4 de-bf16 hacks.
print(torch.cuda.get_device_name(0), "->", DTYPE)

model, tokenizer = FastLanguageModel.from_pretrained(
    model_name     = "NousResearch/Hermes-3-Llama-3.1-8B",
    max_seq_length = MAX_SEQ,
    load_in_4bit   = True,
    dtype          = DTYPE,
)

# %% [5] Attach LoRA adapters (the QLoRA part) — unchanged from the T4 script
model = FastLanguageModel.get_peft_model(
    model,
    r              = 16,
    lora_alpha     = 16,
    lora_dropout   = 0.05,
    target_modules = ["q_proj","k_proj","v_proj","o_proj",
                      "gate_proj","up_proj","down_proj"],
    bias           = "none",
    use_gradient_checkpointing = "unsloth",
    random_state   = 3407,
)

# %% [6] Build the training text (Hermes-3 = ChatML) + held-out eval split
from datasets import load_dataset
ds = load_dataset("json", data_files=DATA_PATH, split="train")

def to_text(ex):
    return {"text": tokenizer.apply_chat_template(ex["messages"], tokenize=False)}

ds = ds.map(to_text, remove_columns=ds.column_names)
assert len(ds) > 0, "Dataset empty — is train.jsonl one {'messages':[...]} object per line?"

# token-length audit — at MAX_SEQ=4608 nothing should truncate, but print the spread so
# an over-long arc is visible, not silent (truncation from the end chops an assistant target).
_MAXLEN = 4608   # keep in sync with cell [7]'s max_length
_lens = sorted(len(tokenizer(t)["input_ids"]) for t in ds["text"])
print(f"token lengths: min {_lens[0]}, median {_lens[len(_lens)//2]}, "
      f"p95 {_lens[int(len(_lens)*0.95)]}, max {_lens[-1]}")
_over = sum(1 for n in _lens if n > _MAXLEN)
if _over:
    print(f"!! {_over} example(s) exceed max_length={_MAXLEN} (longest {_lens[-1]}) and WILL "
          f"truncate. Raise MAX_SEQ+max_length, or split those rows.")

# held-out ~10% eval split — THE anti-collapse instrument. Watch EVAL loss, not train loss.
_split = ds.train_test_split(test_size=0.1, seed=3407)
ds_train, ds_eval = _split["train"], _split["test"]
print(f"{len(ds_train)} train / {len(ds_eval)} eval examples. Sample:\n",
      ds_train[0]["text"][:400], "...\n")

# %% [7] Train — bf16, eval ON, response-only masking. NO fp16/fp32/liger hacks.
from trl import SFTTrainer, SFTConfig

trainer = SFTTrainer(
    model            = model,
    processing_class = tokenizer,
    train_dataset    = ds_train,
    eval_dataset     = ds_eval,
    args = SFTConfig(
        dataset_text_field          = "text",
        max_length                  = 4608,    # bf16 fits it on the L4 -> no truncation
        packing                     = False,   # tiny set — keep examples distinct
        padding_free                = False,   # this trl build defaults it True -> conflicts w/ max_length
        per_device_train_batch_size = 1,
        gradient_accumulation_steps = 8,       # effective batch 8
        per_device_eval_batch_size  = 1,       # keep eval memory tiny
        warmup_steps                = 5,
        # 309 rows, effective batch 8 -> ~35 steps/epoch. Standard for this size:
        num_train_epochs            = 2,       # WATCH eval loss; if it climbs after epoch 1, drop to 1.
        learning_rate               = 1e-4,    # standard SFT LR. On bf16 with masking this should ride
                                               # ~0.5-1.0, NOT nuke to 0.015 like the broken fp16 path did.
        eval_strategy               = "steps", # <-- the instrument. Compare eval_loss to train loss:
        eval_steps                  = 5,       #     both falling = learning; train falls / eval flat-or-up = overfit.
        bf16 = True,                           # native bf16 all the way through — the whole point of this file.
        fp16 = False,
        logging_steps     = 1,
        optim             = "adamw_8bit",
        weight_decay      = 0.01,
        lr_scheduler_type = "cosine",
        seed              = 3407,
        output_dir        = "outputs",
        report_to         = "none",
    ),
)

# --- RESPONSE-ONLY LOSS MASKING (the one real fix carried over from the T4 saga) ---
# Without it, SFTTrainer grades the ~3.3k-token system prompt too; the model memorises
# that boilerplate, loss craters, and generation regurgitates "[CRITICAL FORMATTING RULES]".
# Masks everything up to the assistant turn so loss lands ONLY on the reply. Hermes = ChatML;
# the assert fails loud if the tokenizer uses a different template.
from unsloth.chat_templates import train_on_responses_only
_INSTR = "<|im_start|>user\n"
_RESP  = "<|im_start|>assistant\n"
assert _RESP in ds_train[0]["text"], (
    "ChatML assistant header not found — tokenizer is using a DIFFERENT template. "
    "Print ds_train[0]['text'] and set _INSTR/_RESP to match before training.")
trainer = train_on_responses_only(trainer, instruction_part=_INSTR, response_part=_RESP)
print("response-only masking ON — loss computed on assistant turns only.")

# Sanity: on bf16 there is NO manual dtype casting. Trainable params stay in the framework's
# dtype and it just works. Print them once so a regression is visible.
from collections import Counter
print("trainable dtypes:", Counter(str(p.dtype) for p in model.parameters() if p.requires_grad))
trainer.train()   # WATCH: train AND eval loss should both settle ~0.5-1.0. A dive to ~0.015 =
                  # the old collapse; if that happens on bf16, stop and tell me — it'd be new.

# %% [8] *** SAVE THE ADAPTERS IMMEDIATELY — the safety net. DO NOT SKIP. ***
model.save_pretrained("lora_ckpt"); tokenizer.save_pretrained("lora_ckpt")
import os, shutil
shutil.make_archive("lora_ckpt", "zip", "lora_ckpt")
assert os.path.exists("lora_ckpt.zip") and os.path.getsize("lora_ckpt.zip") > 1_000_000, \
    "lora_ckpt.zip missing or too small — fix before export."
print(f"lora_ckpt.zip: {os.path.getsize('lora_ckpt.zip')/1e6:.0f} MB — downloading. KEEP THIS FILE SAFE.")
from google.colab import files
try:
    files.download("lora_ckpt.zip")
except Exception as e:
    print("auto-download failed:", e, "\n-> grab lora_ckpt.zip from the Files pane (folder icon).")
#
# TO RESUME EXPORT ON A FRESH L4 (no retraining):
#   1. run cell [1], do the ONE restart, run cell [2]
#   2. upload lora_ckpt.zip via the Files pane, then in a new cell:
#        import os, zipfile, torch
#        os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"
#        zipfile.ZipFile("lora_ckpt.zip").extractall("lora_ckpt")
#        from unsloth import FastLanguageModel
#        model, tokenizer = FastLanguageModel.from_pretrained(
#            "lora_ckpt", max_seq_length=4608, load_in_4bit=True, dtype=torch.bfloat16)
#   3. run cells [10] and [11].

# %% [9] Eyeball BEFORE export — voice adherence AND a memorisation check.
# Probe each persona TWICE: the TRAINED question (should reproduce closely) and a NOVEL one
# it never saw (should stay in-voice but answer freshly). Loops or verbatim parroting on the
# novel question = memorising, not learning.
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
trained_qs = {usr_of(r).strip().lower() for r in rows}
NOVEL = ["So what's your story?", "Anything strange happen round here lately?",
         "What do you make of this weather?", "Busy today?"]
novel_q = next((q for q in NOVEL if q.lower() not in trained_qs), NOVEL[0])

for r in (rows[0], rows[len(rows)//2], rows[-1]):
    s = sys_of(r)
    print("=" * 72)
    print("PERSONA:", s[:80].replace("\n", " "), "...")
    print("  TRAINED Q:", usr_of(r))
    print("     ->", ask(s, usr_of(r)))
    print("  NOVEL   Q:", novel_q, "   (never seen in training)")
    print("     ->", ask(s, novel_q))

# %% [10] Export to GGUF for Ollama / llama.cpp
# ############################################################################
# ##  SLOW: 10-30 MIN with LONG SILENT PAUSES (llama.cpp build, merge,       ##
# ##  quantize). NO OUTPUT IS NORMAL. DO NOT interrupt — walk away.          ##
# ############################################################################
import time
_t0 = time.time()
ct = tokenizer.chat_template
if isinstance(ct, dict):   # transformers 5.x dict-form template; unsloth save.py assumes str
    tokenizer.chat_template = ct.get("default") or next(iter(ct.values()))
model.save_pretrained_gguf("hermes-npc", tokenizer, quantization_method="q4_k_m",
                           maximum_memory_usage=0.5)
import glob, os
cands = glob.glob("hermes-npc_gguf/**/*.gguf", recursive=True) or glob.glob("**/*.gguf", recursive=True)
print("gguf files:", cands)
assert cands, "No .gguf produced — the export above failed; read its output."
gguf = next((f for f in cands if "q4_k_m" in f.lower()), cands[0])
print(f"using: {gguf}  ({os.path.getsize(gguf)/1e9:.2f} GB, export took {(time.time()-_t0)/60:.0f} min)")

# %% [11] Get the GGUF out (Drive if auth works; direct browser download if not)
import os, shutil
FINAL = "/content/hermes-npc.Q4_K_M.gguf"   # name must match ml/finetune/Modelfile's FROM line
if not os.path.exists(FINAL):
    try:
        os.link(gguf, FINAL)
    except OSError:
        shutil.copy(gguf, FINAL)
print(f"staged: {FINAL} ({os.path.getsize(FINAL)/1e9:.2f} GB)")
try:
    from google.colab import drive
    drive.mount("/content/drive")
    shutil.copy(FINAL, "/content/drive/MyDrive/hermes-npc.Q4_K_M.gguf")
    print("DONE — download hermes-npc.Q4_K_M.gguf from drive.google.com into ml/finetune/")
except Exception as e:
    print("Drive path failed:", e, "\nFalling back to direct browser download (leave the tab open)...")
    from google.colab import files
    files.download(FINAL)

# ============================================================================
# NEXT (locally, in Ollama):
#   1. Put hermes-npc.Q4_K_M.gguf in ml/finetune/ next to the existing Modelfile.
#   2. ollama create hermes-npc -f Modelfile
#   3. In the game's model picker choose hermes-npc, and `compare` it against base
#      hermes3:8b on the scenes base fails (anger->curt, exhaustion->terse, Solem's
#      silence). THAT is the moment of truth.
# ============================================================================
