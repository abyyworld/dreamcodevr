# DreamCodeVR - Generation Server

This is the Python backend for the DreamCodeVR prototype. It takes plain-English instructions and turns them into Unity C# scripts using GPT-4o, then lists out the assumptions it made while writing that code.

## What it does

You type something like "make this cube spin." This server asks GPT-4o to write a Unity script for that, tells you which object it picked, and gives you a list of assumptions it made along the way (like "assumed default speed of 100 degrees/sec"). Unity then shows those assumptions as checkboxes so you can keep the ones that are right and regenerate just the ones that are wrong.

## Files

- **generation_service.py** - the actual AI calls live here. Two functions:
  - `generate_behavior(...)` - first-time generation from an instruction
  - `regenerate_behavior(...)` - takes a previous script plus which assumptions were rejected/kept, and tries to fix only the rejected parts
- **generation_server.py** - the Flask server Unity talks to. Two endpoints: `/generate` and `/regenerate`
- **benchmark_runner.py** - runs through a fixed list of test instructions and saves the results
- **analyze_fidelity.py** - a script you run after collecting data. It checks whether "regenerate" actually left the kept assumptions untouched, by diffing the code before and after
- **results/** - where benchmark runs get saved

## Setup

```
cd ~/dreamcodevr
python -m venv venv
source venv/bin/activate
pip install flask openai
export OPENAI_API_KEY=your_key_here
```

Don't put a `# comment` on the same line as `export` in zsh, it breaks. Put the comment on its own line above.

## Running

```
cd ~/dreamcodevr
python generation_server.py
```

Has to be run from `~/dreamcodevr` so it can find `generation_service.py`. If you edit `generation_service.py`, stop the server and restart it, Flask doesn't auto-reload.

## Checking how well regenerate actually works

After you've used the app for a bit and built up some data in `~/Desktop/dreamcodevr_study_log.csv`, run:

```
python analyze_fidelity.py
```

This compares the code before and after every "Regenerate Unchecked" click and tells you how much actually changed. No extra installs needed.

## Things to know

- GPT-4o sometimes forgets `using System.Collections;` when the code needs it (coroutines). The prompt tells it to always include this, but it doesn't always listen.
- Saying "this ball" or "that thing" doesn't reliably point at the right object, it tends to default to Cube_01 even when a sphere exists. Naming the object directly (e.g. "Sphere_01") works much better.
- **Big one: regenerate doesn't fully respect what you told it to keep.** Even though the prompt explicitly says "don't touch the kept assumptions," GPT-4o still rewrites code tied to them fairly often, sometimes changing half the lines in the script even when you only rejected one small thing. This isn't something I've been able to fully fix with prompting, and at this point it's more interesting as a finding than as a bug, see `analyze_fidelity.py` for how this gets measured.
- If you hit Regenerate multiple times in a row on the same script, this drift adds up each time, since each regenerate uses the last regenerate's output as its starting point.
