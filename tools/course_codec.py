"""Unpack / repack PGA TOUR 2K25 .course files.

Layers: gzip > UTF-16 JSON envelope > binaryData{CourseDescription, Thumbnail,
CourseMetadata} each base64 > gzip > UTF-16 JSON.  See
docs/course-format/pga2k25-course-format.md.

Usage:
    python tools/course_codec.py info   <file.course>
    python tools/course_codec.py unpack <file.course> <out_dir>
    python tools/course_codec.py pack   <in_dir> <file.course>
"""
import base64
import gzip
import json
import math
import sys
from pathlib import Path

BLOBS = ("CourseDescription", "Thumbnail", "CourseMetadata")


def _decode_text(raw: bytes):
    for enc in ("utf-16", "utf-8-sig"):
        try:
            return json.loads(raw.decode(enc))
        except (UnicodeDecodeError, json.JSONDecodeError):
            continue
    raise ValueError("Unrecognized JSON text encoding")


def _encode_text(obj) -> bytes:
    return json.dumps(obj, separators=(",", ":"), ensure_ascii=False).encode("utf-16")


def load(path):
    """Return (envelope, {blob_name: parsed_json})."""
    envelope = _decode_text(gzip.decompress(Path(path).read_bytes()))
    blobs = {
        name: _decode_text(gzip.decompress(base64.b64decode(value)))
        for name, value in envelope.get("binaryData", {}).items()
    }
    return envelope, blobs


def save(path, envelope, blobs, level=6):
    out = dict(envelope)
    out["binaryData"] = {
        name: base64.b64encode(gzip.compress(_encode_text(obj), compresslevel=level)).decode("ascii")
        for name, obj in blobs.items()
    }
    Path(path).write_bytes(gzip.compress(_encode_text(out), compresslevel=level))


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


def pack(in_dir, path):
    src = Path(in_dir)
    envelope = json.loads((src / "envelope.json").read_text(encoding="utf-8"))
    names = envelope.get("binaryData") or list(BLOBS)
    blobs = {n: json.loads((src / f"{n}.json").read_text(encoding="utf-8")) for n in names}
    envelope["binaryData"] = {}
    save(path, envelope, blobs)


if __name__ == "__main__":
    commands = {"info": info, "unpack": unpack, "pack": pack}
    if len(sys.argv) < 3 or sys.argv[1] not in commands:
        sys.exit(__doc__)
    commands[sys.argv[1]](*sys.argv[2:])
