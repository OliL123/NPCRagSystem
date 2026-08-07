# ============================================================================
# Control-vector RE-EXTRACTION on the FINE-TUNED hermes-npc  (Colab/Kaggle GPU)
# ============================================================================
# WHY: the base-hermes vectors transfer to the fine-tune for the loud traits
# (anger/suspicion/grief/disgust) but NOT the subtle ones (guilt/exhaustion/hope) -
# the LoRA moved their activation geometry. This rebuilds all 9 vectors from the
# FINE-TUNE's OWN activations, which should recover the stragglers.
#
# WHERE: a box with plenty of SYSTEM RAM for the merge (the fp16 8B is ~16 GB).
#   - Kaggle (T4 x2, ~29 GB RAM)  <- recommended, the merge fits in RAM.
#   - Colab Pro L4 (24 GB VRAM)   <- also fine.
#   - Colab FREE (~13 GB RAM) will OOM on the CPU merge -> use one of the above.
# Paste each "# %%" block into its own cell.
#
# PREREQS:
#   1. The LoRA adapter on HF (cell [8.5] of the trainer pushed it to
#      DarkSparktheVoid/hermes-npc-lora). If you never ran [8.5], upload lora_ckpt.zip
#      instead (see cell [2] fallback).
#   2. control-vectors.zip = your ml/control-vectors folder (needs extract.py + pairs/;
#      you can exclude out/ and .venv). Upload it in cell [3].
# ============================================================================

# %% [1] Install — ADD only what's missing. Do NOT reinstall transformers or touch numpy:
# Kaggle's base image ships a CONSISTENT set (torch + numpy2 + scipy + sklearn built together),
# and disturbing numpy breaks their compiled ABI ("multiarray failed to import").
# repeng --no-deps so its numpy<2 pin can't downgrade anything; extract.py's np.float_ shim
# lets repeng run on numpy 2 fine.
# *** If this kernel was already numpy-churned, FACTORY RESET first (Session options ->
#     Factory reset), THEN run this cell on the clean image. A plain restart is NOT enough. ***
!pip install -q --no-deps repeng || pip install -q --no-deps "git+https://github.com/vgel/repeng.git"
!pip install -q gguf peft bitsandbytes accelerate
# Kaggle ships torchao 0.10, but the peft above wants >=0.16 and PEFT RAISES on the old version
# during LoRA load. We never use torchao (fp16 merge + bitsandbytes 4-bit), so remove it entirely.
!pip uninstall -q -y torchao
import numpy, torch, transformers
print("numpy", numpy.__version__, "| torch", torch.__version__,
      "| transformers", transformers.__version__, "| GPU:", torch.cuda.get_device_name(0))

# %% [2] Merge base + LoRA adapter -> a full fp16 model on disk ("merged_ft")
# Merge on the GPUs (device_map="auto" spreads the 16 GB fp16 model across BOTH T4s = 32 GB
# VRAM), NOT on CPU RAM -> avoids the ~29 GB RAM OOM. Takes a few minutes.
import torch, shutil, os, gc
from transformers import AutoModelForCausalLM, AutoTokenizer
from peft import PeftModel

BASE     = "NousResearch/Hermes-3-Llama-3.1-8B"
LORA     = "DarkSparktheVoid/hermes-npc-lora"   # from trainer cell [8.5]
HF_TOKEN = "hf_PUT_YOUR_TOKEN_HERE"             # needed only if the LoRA repo is PRIVATE

# --- FALLBACK if you never pushed the adapter to HF: upload lora_ckpt.zip and use it ---
# import zipfile; zipfile.ZipFile("lora_ckpt.zip").extractall("lora_ckpt"); LORA = "lora_ckpt"; HF_TOKEN = None

tok  = AutoTokenizer.from_pretrained(BASE)
tok.pad_token_id = tok.eos_token_id
base = AutoModelForCausalLM.from_pretrained(
    BASE, torch_dtype=torch.float16, device_map="auto", low_cpu_mem_usage=True)
peft = PeftModel.from_pretrained(base, LORA, token=HF_TOKEN)
merged = peft.merge_and_unload()                 # full fp16 weights, across the 2 GPUs
os.makedirs("merged_ft", exist_ok=True)
merged.save_pretrained("merged_ft", safe_serialization=True)
tok.save_pretrained("merged_ft")
del base, peft, merged
gc.collect(); torch.cuda.empty_cache()
print("merged -> ./merged_ft")

# %% [3] Upload control-vectors.zip (your ml/control-vectors: extract.py + pairs/), then unzip
import zipfile, glob, os
# Colab: from google.colab import files; files.upload()   # pick control-vectors.zip
# Kaggle: add it as a dataset input, or use the Upload panel, then adjust the path below.
_zip = (glob.glob("control-vectors.zip") or glob.glob("/kaggle/input/**/control-vectors.zip", recursive=True))
assert _zip, "Upload control-vectors.zip first (Files pane / dataset)."
zipfile.ZipFile(_zip[0]).extractall("cv")
# find extract.py wherever it landed inside the zip
_ex = glob.glob("cv/**/extract.py", recursive=True)[0]
CV_DIR = os.path.dirname(_ex)
print("extract.py at:", _ex, "| pairs:", os.listdir(os.path.join(CV_DIR, "pairs")))

# %% [4] Run the extraction on the MERGED fine-tune (4-bit fits the T4)
# extract.py loads --model in 4-bit with --load-4bit, wraps it in repeng's ControlModel,
# and writes one <trait>.gguf per trait into its out/ folder. Same script that built the
# base vectors - only the --model differs (the merged fine-tune instead of base hermes).
import subprocess, sys
r = subprocess.run(
    [sys.executable, _ex, "--model", os.path.abspath("merged_ft"), "--load-4bit"],
    cwd=CV_DIR)
assert r.returncode == 0, "extract.py failed - read the output above."
import glob
vecs = glob.glob(os.path.join(CV_DIR, "out", "*.gguf"))
print(f"{len(vecs)} vectors:", [os.path.basename(v) for v in vecs])

# %% [5] Get the new vectors home: push to HF (small, ~a few MB each), or zip + download
from huggingface_hub import HfApi
VEC_REPO = "DarkSparktheVoid/hermes-npc-vectors"
_api = HfApi()
_api.create_repo(VEC_REPO, token=HF_TOKEN, repo_type="model", exist_ok=True)
_api.upload_folder(folder_path=os.path.join(CV_DIR, "out"), repo_id=VEC_REPO, token=HF_TOKEN)
print(f"vectors pushed -> hf.co/{VEC_REPO}")
print("LOCAL: hf download " + VEC_REPO + " --local-dir ml/control-vectors/out --include '*.gguf'")
# (or just zip cv/.../out and grab it from the Files pane)

# ============================================================================
# NEXT (locally): the new out/*.gguf REPLACE the base-extracted ones in
# ml/control-vectors/out/. Then re-run the sweep to find fine-tune strengths for the
# recovered traits:  powershell -ExecutionPolicy Bypass -File .\test-vectors-recal.ps1
# (guilt / exhaustion / hope are the ones to re-check; keep anger/suspicion/grief/disgust).
# ============================================================================
