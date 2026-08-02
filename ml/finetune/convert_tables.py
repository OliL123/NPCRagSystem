#!/usr/bin/env python3
"""Build a QLoRA SFT dataset from the markdown review tables + the raw training log.

Workflow (the table-based one, not the in-game `tag` one):
  1. Battery run -> Data/Saves/auto/training_log.jsonl  (system, user, response, npc, state)
  2. Archive it -> ml/finetune/logs/batchNN_log.jsonl (BEFORE the next New game wipes it)
  3. Claude pulls turns -> data_review_batchNN.md with blank Verdict / Your Rewrite
  4. You fill Verdict (good|edit|discard) + Your Rewrite
  5. This script joins each table -> its OWN archived log and writes out/train.jsonl

Multi-turn: each log row carries a `turn` index (0 = fresh conversation, then 1,2,...
for an arc collected without `reset`). Consecutive rows whose turn climbs 0,1,2,...
are assembled into ONE {messages:[system, u,a, u,a, ...]} example (loss on each
assistant turn) instead of being flattened to independent single turns. A discarded
or unjudged turn truncates the arc at that point. Legacy logs with no `turn` field
default to 0 -> every row is its own single-turn example, exactly as before.

Log resolution: `data_review_batchXX.md` pairs with `logs/batchXXX_log.jsonl` by name
(the "batchXX" part must match). NEVER falls back to the live/default log for a table
that doesn't have its own archive — a coincidentally-matching line count from a
DIFFERENT batch's log is silent corruption (system prompt for one turn, user/assistant
for another), not usable data. A table with no matching archived log is skipped
entirely and reported, not partially processed.

  good     -> assistant = the original logged response (it was already right)
  edit     -> assistant = your rewrite (the manufactured ideal reply)
  discard  -> skipped (kept aside for a later DPO pass)

Usage:
  py convert_tables.py                    # all data_review_*.md, each paired with its logs/*_log.jsonl
  py convert_tables.py --tables a.md b.md --out out/train.jsonl
"""
import argparse, json, re, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
LOGS_DIR = HERE / "logs"


def unescape(cell: str) -> str:
    """Undo the markdown-table escaping used in the Your Rewrite column.

    A markdown cell can't hold a real newline, so multi-paragraph rewrites are written with
    <br> / <br/> / <br /> — restore those to actual newlines here (they'd otherwise land in the
    training target verbatim).
    """
    cell = re.sub(r"\s*<br\s*/?>\s*", "\n", cell, flags=re.IGNORECASE)
    return (cell.replace("\\*", "*").replace("\\|", "|").replace("\\_", "_")).strip()


def load_log(path: Path):
    rows = []
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def parse_table(md_path: Path):
    """Yield dicts for each data row: idx, npc, prompt, hermes, verdict, rewrite."""
    for raw in md_path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line.startswith("|"):
            continue
        # Split on the pipe borders, dropping ONLY the empty cells from the leading/trailing '|'
        # — never an internal empty cell. `line.strip("|").split("|")` would swallow an empty
        # final "Your Rewrite" cell (every good/discard row), making the row read as <7 columns
        # and be silently dropped — losing that example from the dataset.
        parts = line.split("|")
        if parts and parts[0].strip() == "":
            parts = parts[1:]
        if parts and parts[-1].strip() == "":
            parts = parts[:-1]
        cells = [c.strip() for c in parts]
        if len(cells) < 7:
            continue
        idx = cells[0]
        if not idx.isdigit():          # header row or separator
            continue
        yield {
            "idx": int(idx),
            "npc": cells[1],
            "prompt": cells[3],
            "hermes": cells[4],
            "verdict": cells[5].lower().strip(),
            "rewrite": unescape(cells[6]),
        }


def norm(s: str) -> str:
    return re.sub(r"\s+", " ", s or "").strip().lower()


def build_runs(log):
    """Group 0-based log-line indices into conversation runs using the `turn` field.

    A run starts at turn 0 and continues while turn increments by exactly 1
    (0,1,2,...), which is how a multi-turn arc was collected (no `reset` between
    lines, so the model's history depth climbed each turn). A row with turn 0
    starts a fresh conversation. Legacy logs with no `turn` field default every
    line to 0, so each line becomes its own single-turn run — identical to the
    old flatten-everything behaviour.
    """
    runs, cur = [], []
    for i, entry in enumerate(log):
        t = entry.get("turn", 0) or 0
        if cur and t == len(cur):      # continues the current arc (next expected depth)
            cur.append(i)
        else:                          # t == 0, or an unexpected value -> start fresh
            if cur:
                runs.append(cur)
            cur = [i]
    if cur:
        runs.append(cur)
    return runs


def find_matching_log(table_path: Path) -> Path | None:
    """data_review_batch02b.md -> logs/batch02b_log.jsonl (match the batchXX token)."""
    m = re.search(r"batch([0-9a-zA-Z]+)", table_path.stem)
    if not m:
        return None
    candidate = LOGS_DIR / f"batch{m.group(1)}_log.jsonl"
    return candidate if candidate.exists() else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tables", nargs="*", type=Path,
                    default=sorted(HERE.glob("reviews/data_review_*.md")))
    ap.add_argument("--out", type=Path, default=HERE / "out" / "train.jsonl")
    args = ap.parse_args()

    if not args.tables:
        sys.exit("no review tables found (data_review_*.md)")

    args.out.parent.mkdir(parents=True, exist_ok=True)

    counts = {"good": 0, "edit": 0, "discard": 0, "blank": 0}
    warnings, skipped_tables, examples = [], [], []
    multiturn = 0   # how many emitted examples span more than one assistant turn

    for tbl in args.tables:
        log_path = find_matching_log(tbl)
        if log_path is None:
            skipped_tables.append(tbl.name)
            continue
        log = load_log(log_path)
        table_rows = {r["idx"]: r for r in parse_table(tbl)}

        # Warn about verdicts that can never be placed (idx past the end of the log).
        for idx, row in table_rows.items():
            if idx - 1 >= len(log) and row["verdict"] in ("good", "edit"):
                warnings.append(f"{tbl.name} row {idx}: no matching log line in {log_path.name}")

        # Walk the log as conversation runs; each run -> at most one training example
        # ({system, u, a} for a single turn, or {system, u, a, u, a, ...} for an arc).
        for run in build_runs(log):
            msgs, system = [], None
            for li in run:
                idx = li + 1
                row = table_rows.get(idx)
                if row is None:
                    break                       # no verdict for this turn -> truncate the arc here
                v = row["verdict"]
                if v not in ("good", "edit"):
                    counts["discard" if v == "discard" else "blank"] += 1
                    break                       # a discarded/unjudged turn ends the arc
                entry = log[li]
                # Hard validation: this must be THIS row's own turn, not a coincidence.
                if norm(entry.get("user", "")) != norm(row["prompt"]):
                    warnings.append(
                        f"{tbl.name} row {idx}: prompt mismatch against {log_path.name}, SKIPPED "
                        f"(table='{row['prompt'][:30]}' vs log='{entry.get('user','')[:30]}')")
                    break
                assistant = entry["response"] if v == "good" else row["rewrite"]
                if not assistant.strip():
                    warnings.append(f"{tbl.name} row {idx}: {v} but empty target, skipped")
                    break
                if system is None:              # one system message, from the arc's first turn
                    system = entry["system"]
                    msgs.append({"role": "system", "content": system})
                msgs.append({"role": "user", "content": entry["user"]})
                msgs.append({"role": "assistant", "content": assistant})
                counts[v] += 1
            if system is not None and len(msgs) >= 3:
                examples.append({"messages": msgs})
                if len(msgs) > 3:               # more than one user/assistant pair
                    multiturn += 1

    with open(args.out, "w", encoding="utf-8") as f:
        for ex in examples:
            f.write(json.dumps(ex, ensure_ascii=False) + "\n")

    used_tables = [t.name for t in args.tables if t.name not in skipped_tables]
    print(f"tables used: {', '.join(used_tables) or '(none)'}")
    if skipped_tables:
        print(f"tables SKIPPED (no archived log — original was overwritten, unrecoverable "
              f"without a re-collect): {', '.join(skipped_tables)}")
    print(f"verdicts -> good:{counts['good']} edit:{counts['edit']} "
          f"discard:{counts['discard']} blank/unjudged:{counts['blank']}")
    print(f"wrote {len(examples)} SFT examples ({multiturn} multi-turn) -> {args.out}")
    if warnings:
        print(f"\n{len(warnings)} warning(s) (each SKIPPED, not included in output):")
        for w in warnings:
            print("  -", w)


if __name__ == "__main__":
    main()
