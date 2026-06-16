"""
Offline fidelity analysis for the DreamCodeVR partial-rejection study log.

Reads ~/Desktop/dreamcodevr_study_log.csv, and for every row where both
pre_regenerate_code and code (post) are present, computes a real line-level
diff (not just exact-match boolean) using difflib, producing:

- percent_lines_changed: of the lines that existed in the PREVIOUS code,
  what fraction were not preserved unchanged in the new code (added,
  removed, or modified)
- percent_lines_added: new lines introduced that didn't exist before
- a coarse "kept_assumption_violation" flag: True if any non-trivial
  code line changed (ignoring blank lines and pure whitespace/comment-only
  changes), used as a less blunt proxy than the old exact-match boolean

This does NOT attempt true semantic/AST-level diffing - it's a line-based
diff, which is still far more informative than exact string equality and
is the documented method in the paper's methodology section if used as-is.

Run with: python analyze_fidelity.py
Requires: pandas not required, uses only stdlib (csv, difflib, re).
"""
import csv
import difflib
import re
from pathlib import Path

LOG_PATH = Path.home() / "Desktop" / "dreamcodevr_study_log.csv"
OUTPUT_PATH = Path.home() / "Desktop" / "dreamcodevr_fidelity_analysis.csv"


def normalize_line(line):
    """Strip whitespace and trailing comments for trivial-change filtering."""
    stripped = line.strip()
    stripped = re.sub(r"//.*$", "", stripped).strip()
    return stripped


def compute_line_diff(pre_code, post_code):
    """
    Returns a dict with diff statistics between two code strings.
    Uses difflib.SequenceMatcher on a line-by-line basis.
    """
    if not pre_code or not post_code:
        return None

    pre_lines = pre_code.replace("\r\n", "\n").split("\n")
    post_lines = post_code.replace("\r\n", "\n").split("\n")

    matcher = difflib.SequenceMatcher(a=pre_lines, b=post_lines, autojunk=False)

    unchanged = 0
    changed = 0
    added = 0
    removed = 0
    non_trivial_change = False

    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == "equal":
            unchanged += (i2 - i1)
        elif tag == "replace":
            changed += max(i2 - i1, j2 - j1)
            old_chunk = [normalize_line(l) for l in pre_lines[i1:i2]]
            new_chunk = [normalize_line(l) for l in post_lines[j1:j2]]
            if old_chunk != new_chunk:
                non_trivial_change = True
        elif tag == "delete":
            removed += (i2 - i1)
            non_trivial_change = True
        elif tag == "insert":
            added += (j2 - j1)
            non_trivial_change = True

    total_pre_lines = max(len(pre_lines), 1)
    percent_lines_changed = round(100.0 * (changed + removed) / total_pre_lines, 1)
    percent_lines_added = round(100.0 * added / total_pre_lines, 1)

    return {
        "percent_lines_changed": percent_lines_changed,
        "percent_lines_added": percent_lines_added,
        "lines_unchanged": unchanged,
        "lines_changed": changed,
        "lines_removed": removed,
        "lines_added": added,
        "kept_assumption_violation": non_trivial_change,
    }


def main():
    if not LOG_PATH.exists():
        print(f"No log file found at {LOG_PATH}")
        return

    rows_out = []
    with open(LOG_PATH, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for row in reader:
            pre_code = row.get("pre_regenerate_code", "")
            post_code = row.get("code", "")

            if not pre_code or not post_code:
                continue

            diff_stats = compute_line_diff(pre_code, post_code)
            if diff_stats is None:
                continue

            belief_answer = row.get("belief_answer", "")
            real_diff_correct = ""
            if belief_answer in ("yes", "no"):
                real_diff_correct = (
                    (belief_answer == "yes" and not diff_stats["kept_assumption_violation"])
                    or (belief_answer == "no" and diff_stats["kept_assumption_violation"])
                )

            rows_out.append({
                "timestamp": row.get("timestamp", ""),
                "instruction": row.get("instruction", ""),
                "decision": row.get("decision", ""),
                "disclosure_shown": row.get("disclosure_shown", ""),
                "belief_answer": belief_answer,
                "belief_correct_exact_match": row.get("belief_correct", ""),
                **diff_stats,
                "belief_correct_real_diff": real_diff_correct,
            })

    if not rows_out:
        print("No regenerate trials with both pre and post code found in the log yet.")
        return

    fieldnames = list(rows_out[0].keys())
    with open(OUTPUT_PATH, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows_out)

    print(f"Analyzed {len(rows_out)} regenerate trials.")
    print(f"Output written to {OUTPUT_PATH}")

    violations = sum(1 for r in rows_out if r["kept_assumption_violation"])
    print(f"\nKept-assumption violations (real diff): {violations}/{len(rows_out)} "
          f"({100.0 * violations / len(rows_out):.1f}%)")

    pct_changed_values = [r["percent_lines_changed"] for r in rows_out]
    if pct_changed_values:
        sorted_vals = sorted(pct_changed_values)
        median = sorted_vals[len(sorted_vals) // 2]
        print(f"Median % of previous-code lines changed/removed per regenerate: {median}%")

    mismatches = sum(
        1 for r in rows_out
        if r["belief_correct_exact_match"] != "" and
        r["belief_correct_real_diff"] != "" and
        str(r["belief_correct_exact_match"]).lower() != str(r["belief_correct_real_diff"]).lower()
    )
    print(f"\nRows where exact-match belief_correct disagreed with real-diff belief_correct: {mismatches}")


if __name__ == "__main__":
    main()
