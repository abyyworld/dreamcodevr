"""
Local HTTP bridge for Unity. Wraps generate_behavior() and
regenerate_behavior() from generation_service.py behind simple
POST endpoints.
Requires: pip install flask
Requires: OPENAI_API_KEY set
Run with: python generation_server.py
Listens on http://127.0.0.1:5005/generate
         http://127.0.0.1:5005/regenerate
"""
from flask import Flask, request, jsonify
from generation_service import generate_behavior, regenerate_behavior, GenerationError

app = Flask(__name__)


@app.route("/generate", methods=["POST"])
def generate():
    data = request.get_json(force=True)
    scene = data.get("scene", [])
    instruction = data.get("instruction", "")

    if not instruction:
        return jsonify({"error": "missing 'instruction'"}), 400

    try:
        result = generate_behavior(scene, instruction)
        return jsonify(result)
    except GenerationError as e:
        return jsonify({"error": str(e)}), 500


@app.route("/regenerate", methods=["POST"])
def regenerate():
    data = request.get_json(force=True)
    scene = data.get("scene", [])
    instruction = data.get("instruction", "")
    previous_code = data.get("previous_code", "")
    rejected_assumptions = data.get("rejected_assumptions", [])
    kept_assumptions = data.get("kept_assumptions", [])

    if not instruction:
        return jsonify({"error": "missing 'instruction'"}), 400
    if not previous_code:
        return jsonify({"error": "missing 'previous_code'"}), 400

    try:
        result = regenerate_behavior(
            scene,
            instruction,
            previous_code,
            rejected_assumptions,
            kept_assumptions,
        )
        return jsonify(result)
    except GenerationError as e:
        return jsonify({"error": str(e)}), 500


if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5005)
