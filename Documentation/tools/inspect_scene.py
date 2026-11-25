"""Dump the GameObject hierarchy of a Unity scene with script components attached.

Usage: python inspect_scene.py <scene.unity> [substring-filter]
"""
import re
import sys
from collections import defaultdict

SCRIPT_NAMES = {
    "84b81543bd9e5a644acb98bf5ad8fe86": "AttenuatingRadiationBarrier",
    "c09fb68c16b77de42a28487779fb3378": "CriticalRadiationObject",
    "0d1a7144e18b65c49803cd18f81ffe9a": "RadiationDosimeter",
    "adba932410427034ca75a920209e162e": "RadiationDosimeterField",
    "4f9b9ed842b74704b6e2a2cf60bcb4fb": "RadiationMagnet",
    "a50b2b1853d4d8a40990cacff76119e6": "RadiationMirror",
    "1dfbd780c35de08438b454d59d760406": "RadiationParticle",
    "f42830e4ea4760c49b90368db8681914": "RadiationSandboxController",
    "63718cd705018434fb8df62549e9f65c": "RadiationSource",
    "ffb80dbc13510be48b8661b59fa8542d": "RadiationSpeedReducer",
    "00424099d711b1b419a7a91b36336e4f": "SelectionHighlight",
}

TYPE_MAP = {
    "4": "Transform",
    "1": "GameObject",
    "33": "MeshFilter",
    "23": "MeshRenderer",
    "65": "BoxCollider",
    "135": "SphereCollider",
    "136": "CapsuleCollider",
    "64": "MeshCollider",
    "54": "Rigidbody",
    "20": "Camera",
    "108": "Light",
    "114": "MonoBehaviour",
}

DOC_RE = re.compile(r"^--- !u!(\d+) &(\d+)")

COLLIDER_TYPES = {
    "BoxCollider",
    "SphereCollider",
    "CapsuleCollider",
    "MeshCollider",
}


def parse(path):
    docs = []
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        current_type, current_id, body = None, None, []
        for line in handle:
            match = DOC_RE.match(line)
            if match:
                if current_id is not None:
                    docs.append((int(current_type), int(current_id), "".join(body)))
                current_type, current_id, body = match.group(1), match.group(2), []
            elif current_id is not None:
                body.append(line)
        if current_id is not None:
            docs.append((int(current_type), int(current_id), "".join(body)))
    return docs


def field(body, name):
    match = re.search(r"^\s*%s:\s*(.*)$" % re.escape(name), body, re.M)
    return match.group(1).strip() if match else None


def ref_id(value):
    """Extract the numeric id out of `{fileID: 123}`; None when absent/zero."""
    if not value:
        return None
    match = re.search(r"fileID:\s*(-?\d+)", value)
    if not match:
        return None
    number = int(match.group(1))
    return number if number > 0 else None


def main():
    path = sys.argv[1]
    needle = sys.argv[2] if len(sys.argv) > 2 else None
    docs = parse(path)

    game_objects = {}
    transforms = {}
    behaviours = []

    for type_id, file_id, body in docs:
        type_name = TYPE_MAP.get(str(type_id), "Type%s" % type_id)
        if type_name == "GameObject":
            game_objects[file_id] = field(body, "m_Name")
        elif type_name in ("Transform", "RectTransform"):
            transforms[file_id] = {
                "go": ref_id(field(body, "m_GameObject")),
                "father": ref_id(field(body, "m_Father")),
                "children": [int(x) for x in re.findall(r"- \{fileID: (-?\d+)\}", body) if int(x) > 0],
            }
        elif type_name == "MonoBehaviour":
            guid = field(body, "m_Script")
            script = "?"
            if guid:
                match = re.search(r"guid: ([0-9a-f]+)", guid)
                if match:
                    script = SCRIPT_NAMES.get(match.group(1), match.group(1))
            behaviours.append((file_id, ref_id(field(body, "m_GameObject")), script))

    components = defaultdict(list)
    for file_id, go, script in behaviours:
        components[go].append(script)
    for type_id, file_id, body in docs:
        type_name = TYPE_MAP.get(str(type_id), "Type%s" % type_id)
        if type_name in ("MeshFilter", "MeshRenderer", "BoxCollider", "SphereCollider",
                         "CapsuleCollider", "MeshCollider", "Rigidbody"):
            go = ref_id(field(body, "m_GameObject"))
            components[go].append(type_name)

    # Colliders are what the runtime raycast actually hits, so they matter when
    # judging whether a child emitter is reachable with the mouse.
    for type_id, file_id, body in docs:
        type_name = TYPE_MAP.get(str(type_id), "Type%s" % type_id)
        if type_name in COLLIDER_TYPES:
            components[ref_id(field(body, "m_GameObject"))].append(type_name)

    def go_id_of_transform(tid):
        return transforms.get(tid, {}).get("go")

    # map transform id -> children transform ids
    roots = []
    for tid, data in transforms.items():
        father = data["father"]
        if not father or father not in transforms:
            roots.append(tid)

    def label(tid):
        go = go_id_of_transform(tid)
        name = game_objects.get(go, "<missing>")
        comps = components.get(go, [])
        suffix = (" [" + ", ".join(comps) + "]") if comps else ""
        return name + suffix

    def walk(tid, depth):
        text = "  " * depth + label(tid)
        if needle is None or needle.lower() in text.lower():
            print(text)
            # still descend so children of a match are visible
        for child in transforms.get(tid, {}).get("children", []):
            walk(child, depth + 1)

    for root in roots:
        walk(root, 0)


if __name__ == "__main__":
    main()
