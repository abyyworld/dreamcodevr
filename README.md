# DreamCodeVR - Generation Server

Python backend for the DreamCodeVR error-recovery prototype (CHI 2027).
Generates Unity C# behavior scripts from natural-language instructions
using OpenAI (gpt-4o), with structured JSON output.

## Files

- `generation_service.py` - Core function `generate_behavior(scene_description, instruction)`.
  Calls OpenAI with a strict JSON schema, returns `{code, target_object, assumptions[]}`.
- `generation_server.py` - Flask wrapper. Run this to serve Unity.
  Listens on `http://127.0.0.1:5005`, one endpoint: `POST /generate`.
- `benchmark_runner.py` - Runs 12 fixed benchmark instructions offline,
  saves results to `results/benchmark_run_<timestamp>.json`.
- `results/` - Offline benchmark outputs.

## Setup

```bash
cd ~/dreamcodevr
python -m venv venv
source venv/bin/activate
pip install flask openai
export OPENAI_API_KEY=your_key_here
```

**Important:** never put `# comments` on the same line as `export` in zsh -
it breaks the shell. Put the export on its own line, comment above it.

## Running

```bash
cd ~/dreamcodevr
python generation_server.py
```

Must be run from `~/dreamcodevr` so it can import `generation_service`.

## Known issues / gotchas

- GPT-4o sometimes omits `using System.Collections;` for coroutine-based
  code (IEnumerator). System prompt now explicitly requires this, but
  not 100% guaranteed.
- "this ball" / "that thing" style instructions may not map to the
  semantically obvious object (e.g. "this ball" -> Cube_01 instead of
  Sphere_01) if the LLM has no notion of what the user is currently
  looking at/holding. Explicit object names (e.g. "Sphere_01") work
  more reliably.
