"""Inspect and unpack PGA TOUR 2K25 .course files (read only: never writes a .course file).

Layers: gzip > UTF-16 JSON envelope > binaryData{CourseDescription, Thumbnail,
CourseMetadata} each base64 > gzip > UTF-16 JSON.  See
docs/course-format/pga2k25-course-format.md.

Usage:
    python tools/course_codec.py info   <file.course>
    python tools/course_codec.py unpack <file.course> <out_dir>
"""
import base64
import gzip
import json
import math
import sys
from pathlib import Path


def _decode_text(raw: bytes):
    for enc in ("utf-16", "utf-8-sig"):
        try:
            return json.loads(raw.decode(enc))
        except (UnicodeDecodeError, json.JSONDecodeError):
            continue
    raise ValueError("Unrecognized JSON text encoding")


def load(path):
    """Return (envelope, {blob_name: parsed_json})."""
    envelope = _decode_text(gzip.decompress(Path(path).read_bytes()))
    blobs = {
        name: _decode_text(gzip.decompress(base64.b64decode(value)))
        for name, value in envelope.get("binaryData", {}).items()
    }
    return envelope, blobs


def route_length(hole):
    wps = hole.get("waypoints", [])
    return sum(math.dist((a["x"], a["z"]), (b["x"], b["z"])) for a, b in zip(wps, wps[1:]))


def info(path):
    _, blobs = load(path)
    desc, meta = blobs["CourseDescription"], blobs.get("CourseMetadata", {})
    holes = desc.get("holes3", [])
    print(f"name        {desc.get('name')!r}  id={desc.get('_id')}  schema v{desc.get('version')}")
    print(f"holes       {len(holes)}  par {sum(h.get('par', 0) for h in holes)}"
          f"  length {sum(route_length(h) for h in holes):.1f} m (metadata {meta.get('length')})")
    print(f"tees/hole   {[len(h.get('tees', [])) for h in holes]}")
    print(f"pins/hole   {[len(h.get('pins', [])) for h in holes]}")
    for key in ("height", "surfaceBrushes2", "surfaceSplines2", "placedObjects4"):
        print(f"{key:<16}{len(desc.get(key, []))}")


def unpack(path, out_dir):
    envelope, blobs = load(path)
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    env = dict(envelope)
    env["binaryData"] = list(blobs)
    (out / "envelope.json").write_text(json.dumps(env, indent=1), encoding="utf-8")
    for name, obj in blobs.items():
        (out / f"{name}.json").write_text(json.dumps(obj, indent=1, ensure_ascii=False), encoding="utf-8")
    thumb = blobs.get("Thumbnail", {}).get("image")
    if thumb:
        (out / "Thumbnail.jpg").write_bytes(base64.b64decode(thumb))


if __name__ == "__main__":
    commands = {"info": info, "unpack": unpack}
    if len(sys.argv) < 3 or sys.argv[1] not in commands:
        sys.exit(__doc__)
    commands[sys.argv[1]](*sys.argv[2:])
