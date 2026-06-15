"""
Runs the benchmark task set through the generation service and logs
results to JSON for later analysis (compile testing + assumption taxonomy).
"""

import json
from datetime import datetime
from pathlib import Path

from generation_service import generate_behavior, GenerationError

BENCHMARK_TASKS = [
    {"id": "physics_01", "category": "physics", "instruction": "Make this ball bounce when it hits the floor"},
    {"id": "physics_02", "category": "physics", "instruction": "Make this object fall slowly, like it's underwater"},
    {"id": "interaction_01", "category": "interaction", "instruction": "Make this cube turn red when the player grabs it"},
    {"id": "interaction_02", "category": "interaction", "instruction": "Make this lever rotate 90 degrees when pulled"},
    {"id": "visual_01", "category": "visual_feedback", "instruction": "Make this object glow when the player looks at it"},
    {"id": "visual_02", "category": "visual_feedback", "instruction": "Make this panel fade out after 3 seconds"},
    {"id": "spatial_01", "category": "spatial", "instruction": "Make this door open when the player walks within 1 meter"},
    {"id": "spatial_02", "category": "spatial", "instruction": "Make this object follow the player's hand at a distance"},
    {"id": "multiobject_01", "category": "multi_object", "instruction": "When this button is pressed, turn on that light"},
    {"id": "multiobject_02", "category": "multi_object", "instruction": "Make this object copy the rotation of that other object"},
    {"id": "ambiguous_01", "category": "ambiguous", "instruction": "Make the thing move when I grab it"},
    {"id": "ambiguous_02", "category": "ambiguous", "instruction": "Make it bigger when I look at it"},
]

DEFAULT_SCENE = [
    {"name": "Cube_01", "components": ["Transform", "MeshRenderer", "BoxCollider", "Rigidbody"], "tags": ["grabbable"]},
    {"name": "Sphere_01", "components": ["Transform", "MeshRenderer", "SphereCollider", "Rigidbody"], "tags": ["grabbable"]},
    {"name": "Lever_01", "components": ["Transform", "MeshRenderer", "BoxCollider", "HingeJoint"], "tags": ["interactable"]},
    {"name": "Door_01", "components": ["Transform", "MeshRenderer", "BoxCollider"], "tags": ["interactable"]},
    {"name": "Button_01", "components": ["Transform", "MeshRenderer", "BoxCollider"], "tags": ["interactable"]},
    {"name": "Light_01", "components": ["Transform", "Light"], "tags": []},
    {"name": "Panel_01", "components": ["Transform", "CanvasRenderer", "RawImage"], "tags": []},
    {"name": "Player", "components": ["XROrigin", "Camera"], "tags": []},
]


def run_benchmark(tasks=BENCHMARK_TASKS, scene=DEFAULT_SCENE, out_dir="results"):
    out_dir = Path(out_dir)
    out_dir.mkdir(exist_ok=True)

    results = []
    for task in tasks:
        print(f"Running {task['id']}: {task['instruction']}")
        try:
            output = generate_behavior(scene, task["instruction"])
            results.append({
                "id": task["id"],
                "category": task["category"],
                "instruction": task["instruction"],
                "status": "ok",
                **output,
            })
        except GenerationError as e:
            results.append({
                "id": task["id"],
                "category": task["category"],
                "instruction": task["instruction"],
                "status": "error",
                "error": str(e),
            })

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_path = out_dir / f"benchmark_run_{timestamp}.json"
    with open(out_path, "w") as f:
        json.dump(results, f, indent=2)

    print(f"\nSaved {len(results)} results to {out_path}")
    return results


if __name__ == "__main__":
    run_benchmark()
