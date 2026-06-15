"""
Local HTTP bridge for Unity. Wraps generate_behavior() from
generation_service.py behind a simple POST endpoint.

Requires: pip install flask
Requires: GEMINI_API_KEY set (same as before)

Run with: python generation_server.py
Listens on http://127.0.0.1:5005/generate
"""

from flask import Flask, request, jsonify
from generation_service import generate_behavior, GenerationError

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


if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5005)
