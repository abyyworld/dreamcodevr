# DreamCodeVR - Generation Server

Python backend for the DreamCodeVR error-recovery prototype. Generates Unity C# behavior scripts from natural-language instructions using OpenAI (gpt-4o), with structured JSON output. Supports both full generation and granular, assumption-scoped regeneration, plus offline analysis of how faithfully regenerations actually preserve the code tied to assumptions the user asked to keep.

## Files

* `generation_service.py` - Core functions:
  * `generate_behavior(scene_description, instruction)` - calls OpenAI with a strict JSON schema, returns `{code, target_object, assumptions[]}`.
  * `regenerate_behavior(scene_description, instruction, previous_code, rejected_assumptions, kept_assumptions)` - correction pass. Given the previous code and which assumptions were rejected vs kept, asks the model to fix only the rejected parts and preserve the rest. Returns the same `{code, target_object, assumptions[]}` shape.
* `generation_server.py` - Flask wrapper. Run this to serve Unity. Listens on `http://127.0.0.1:5005`, two endpoints:
  * `POST /generate` - fresh generation from an instruction.
  * `POST /regenerate` - correction pass, takes `previous_code`, `rejected_assumptions`, `kept_assumptions` in addition to `scene` and `instruction`.
* `benchmark_runner.py` - Runs 12 fixed benchmark instructions offline, saves results to `results/benchmark_run_<timestamp>.json`.
* `analyze_fidelity.py` - Offline analysis script. Reads the Unity-side study log CSV (`~/Desktop/dreamcodevr_study_log.csv`) and, for every regenerate trial, computes a real line-level diff (via Python's `difflib`) between the pre- and post-regenerate code, rather than relying on exact string match. Outputs `~/Desktop/dreamcodevr_fidelity_analysis.csv` with percent of lines changed/added/removed, a `kept_assumption_violation` flag, and a recomputed `belief_correct_real_diff` field that checks the user's stated trust ("will the AI keep what I marked as correct?") against what actually happened in the code. Run with `python analyze_fidelity.py` from `~/dreamcodevr` after collecting trial data. No extra dependencies beyond the standard library.
* `results/` - Offline benchmark outputs.

## The regenerate / partial-rejection flow

On the Unity side, each generated assumption is shown with its own checkbox instead of one blanket accept/reject. The user can:
- Accept everything as-is, or
- Uncheck specific assumptions they believe are wrong and press "Regenerate Unchecked," which calls `/regenerate` with only the unchecked items as `rejected_assumptions` and the rest as `kept_assumptions`.

Before each regenerate call, the user is asked a belief-elicitation question ("do you expect the assumptions you kept to still hold after this?", yes/no), logged alongside a randomized disclosure condition (whether the user was warned upfront that regeneration may affect unrelated parts of the code). This lets `analyze_fidelity.py` compare what the user predicted against what the model actually did to the code.

## Setup

```
cd ~/dreamcodevr
python -m venv venv
source venv/bin/activate
pip install flask openai
export OPENAI_API_KEY=your_key_here
```

Important: never put `# comments` on the same line as `export` in zsh - it breaks the shell. Put the export on its own line, comment above it.

## Running

```
cd ~/dreamcodevr
python generation_server.py
```

Must be run from `~/dreamcodevr` so it can import `generation_service`. Restart this process after any edit to `generation_service.py` - Flask does not auto-reload.

## Known issues / gotchas

* GPT-4o sometimes omits `using System.Collections;` for coroutine-based code (IEnumerator). System prompt now explicitly requires this, but not 100% guaranteed.
* "this ball" / "that thing" style instructions may not map to the semantically obvious object (e.g. "this ball" -> Cube_01 instead of Sphere_01) if the LLM has no notion of what the user is currently looking at/holding. Explicit object names (e.g. "Sphere_01") work more reliably.
* **Regeneration does not reliably honor `kept_assumptions`.** Even with an explicit, structured system prompt instructing the model to preserve code tied to kept assumptions and only change what's tied to rejected ones, GPT-4o frequently rewrites unrelated code anyway. In early pilot testing, regenerations changed code tied to "kept" assumptions in the majority of trials, with the percentage of changed lines ranging from under 10% to 50% in a single correction pass. This is currently treated as a documented limitation and a candidate research finding (the gap between perceived granular control and actual model behavior) rather than something patched away - see `analyze_fidelity.py` for the measurement approach.
* Multiple consecutive regenerate calls on the same trial compound this drift further, since each regenerate uses the previous regenerate's output as its new "previous code." Treat multi-round regenerate chains as a separate analysis case from single-round ones if doing formal trial counting.
