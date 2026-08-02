"""
Build control vectors for the NPC model, one per emotional/physical state.

This is a ONE-TIME build step (per model). It reads the contrastive trait poles in
pairs/traits.json, crosses them with pairs/suffixes.txt, runs repeng to find the
internal "direction" for each trait, and exports a small .gguf vector per trait into
out/. Those .gguf files are what llama.cpp loads at inference (see README).

Run this on a GPU box (cloud is easiest — it briefly needs the full model). Then copy
out/*.gguf down to the machine that runs llama.cpp. Re-run only when the base model
changes (e.g. after fine-tuning), when you edit the pairs, or when you add a trait.

    pip install -r requirements.txt
    python extract.py --model NousResearch/Hermes-3-Llama-3.1-8B
"""

import argparse
import json
import os

import numpy as np
import torch
from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig

# repeng 0.4.0 still references np.float_, which NumPy 2.0 removed. We run NumPy 2 (the only
# build with a Python 3.13 wheel), so restore the removed aliases before importing repeng.
if not hasattr(np, "float_"):
    np.float_ = np.float64
if not hasattr(np, "int_"):
    np.int_ = np.int64

from repeng import ControlVector, ControlModel, DatasetEntry

HERE = os.path.dirname(os.path.abspath(__file__))
PAIRS = os.path.join(HERE, "pairs")
OUT = os.path.join(HERE, "out")


def load_pairs():
    with open(os.path.join(PAIRS, "traits.json"), encoding="utf-8") as f:
        traits = {k: v for k, v in json.load(f).items() if not k.startswith("_")}
    with open(os.path.join(PAIRS, "suffixes.txt"), encoding="utf-8") as f:
        suffixes = [line.strip() for line in f if line.strip()]
    return traits, suffixes


def make_dataset(tokenizer, positive, negative, suffixes):
    """One DatasetEntry per (pole-pair x suffix). positive/negative differ only in the
    trait, so the difference in activations isolates that trait's direction."""
    dataset = []
    for pos, neg in zip(positive, negative):
        for suffix in suffixes:
            dataset.append(DatasetEntry(
                positive=render(tokenizer, pos, suffix),
                negative=render(tokenizer, neg, suffix),
            ))
    return dataset


def render(tokenizer, persona, suffix):
    """Persona as a system instruction + a generic user turn, with `suffix` seeded as the
    start of the assistant's reply. Uses the model's own chat template so it works for
    Hermes/Llama/whatever you point --model at."""
    messages = [
        {"role": "system", "content": f"You are someone who is {persona}."},
        {"role": "user", "content": "Say something to the traveller."},
    ]
    prefix = tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
    return prefix + suffix


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="NousResearch/Hermes-3-Llama-3.1-8B",
                    help="HF model id or local path of the base you'll run in llama.cpp.")
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--load-4bit", action="store_true",
                    help="Load in 4-bit (bitsandbytes) to fit a smaller GPU.")
    # Upper-middle layers carry the most steerable signal on an 8B (32 layers).
    ap.add_argument("--layers", default="-5:-18:-1",
                    help="start:stop:step for the control layers (Python slice semantics).")
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    traits, suffixes = load_pairs()

    print(f"Loading {args.model} ...")
    tokenizer = AutoTokenizer.from_pretrained(args.model)
    tokenizer.pad_token_id = tokenizer.eos_token_id
    kwargs = dict(dtype=torch.float16, device_map="auto")
    if args.load_4bit:
        kwargs["quantization_config"] = BitsAndBytesConfig(
            load_in_4bit=True,
            bnb_4bit_quant_type="nf4",
            bnb_4bit_compute_dtype=torch.float16,
            bnb_4bit_use_double_quant=True,
        )
    model = AutoModelForCausalLM.from_pretrained(args.model, **kwargs)

    a, b, c = (int(x) for x in args.layers.split(":"))
    control_layers = list(range(a, b, c))
    model = ControlModel(model, control_layers)

    for trait, poles in traits.items():
        print(f"\n=== {trait} ===")
        dataset = make_dataset(tokenizer, poles["positive"], poles["negative"], suffixes)
        print(f"  {len(dataset)} contrastive prompts")
        cv = ControlVector.train(model, tokenizer, dataset)
        path = os.path.join(args.out, f"{trait}.gguf")
        cv.export_gguf(path)
        print(f"  -> {path}")

    print(f"\nDone. {len(traits)} vectors in {args.out}. Copy them to your llama.cpp box.")


if __name__ == "__main__":
    main()
