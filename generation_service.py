import json
from openai import OpenAI, OpenAIError

client = OpenAI()  # reads OPENAI_API_KEY from env


class GenerationError(Exception):
    """Raised when the AI generation step fails for any reason."""
    pass


BEHAVIOR_SCHEMA = {
    "name": "behavior_output",
    "strict": True,
    "schema": {
        "type": "object",
        "properties": {
            "code": {"type": "string"},
            "target_object": {"type": "string"},
            "assumptions": {
                "type": "array",
                "items": {"type": "string"}
            }
        },
        "required": ["code", "target_object", "assumptions"],
        "additionalProperties": False
    }
}


def generate_behavior(scene_description, instruction):
    system_prompt = (
        "You are a Unity C# code generator for a VR scene. "
        "Given a scene description and an instruction, generate a C# "
        "MonoBehaviour script that implements the instruction, the name "
        "of the target GameObject it should be attached to, and a list "
        "of any assumptions you made (ambiguous references, assumed "
        "components, default values, etc).\n\n"
        "IMPORTANT CODE REQUIREMENTS:\n"
        "- Always include ALL necessary using statements at the top of "
        "the script, including 'using UnityEngine;' and "
        "'using System.Collections;' if you use IEnumerator or "
        "coroutines (StartCoroutine, yield return, WaitForSeconds, etc).\n"
        "- The class must inherit from MonoBehaviour.\n"
        "- The generated code must be complete and compilable on its own - "
        "do not omit imports, even if they seem obvious.\n"
        "- If you reference an object by name explicitly given in the "
        "instruction (e.g. 'Sphere_01'), use GameObject.Find(\"Sphere_01\") "
        "or similar to locate it at runtime, do not assume 'this' refers "
        "to the GameObject the script is attached to unless that is the "
        "target_object itself."
    )

    user_prompt = (
        f"Scene description:\n{scene_description}\n\n"
        f"Instruction: {instruction}"
    )

    try:
        response = client.chat.completions.create(
            model="gpt-4o",
            messages=[
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt}
            ],
            response_format={"type": "json_schema", "json_schema": BEHAVIOR_SCHEMA}
        )
    except OpenAIError as e:
        raise GenerationError(f"OpenAI API error: {e}")

    try:
        result = json.loads(response.choices[0].message.content)
    except (json.JSONDecodeError, IndexError, AttributeError) as e:
        raise GenerationError(f"Failed to parse model response: {e}")

    return result
