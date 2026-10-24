"""Read-only recommendation audit. Outputs are private; use an ignored data/ folder.

Capture before changing ranking, then run again with the same snapshot. This never
opens items, creates trails, changes preferences, or refreshes sources/editions.
The seven seed IDs identify evaluation inputs only, never ranking rules.
"""
import argparse
import hashlib
import json
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import urlencode

SEEDS = {
    "AI consciousness": "c25d45c7-d764-4162-a9f9-49218adc6f2c",
    "Flood traditions": "a76c12ee-7680-4e4e-b32d-3f91fafe9f0c",
    "UFO history": "b42751cd-a1e0-45e6-933c-e9c1ef8d111e",
    "Remote viewing": "0023d292-5d70-4c67-a1a1-81364efd4d6f",
    "Robotics announcement": "0ee48966-38f2-4104-8005-4d6f1426072b",
    "Audio DSP": "2d613310-204d-4f30-a627-b7e55f77738b",
    "Robot learning": "37b2a02a-4e9e-42b5-b645-edcb78c96870",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="http://127.0.0.1:47832/api")
    parser.add_argument("--output", required=True)
    parser.add_argument("--snapshot", help="Existing snapshot to verify unchanged catalog/settings")
    parser.add_argument("--path-aware", action="store_true")
    args = parser.parse_args()
    out = Path(args.output)
    out.mkdir(parents=True, exist_ok=True)

    def request(path, method="GET"):
        req = Request(args.base + path, method=method)
        with urlopen(req, timeout=60) as response:
            return json.load(response)

    snapshot = request("/export", "POST")
    def fingerprint(data):
        # Only old fields participate, so additive schema changes are permitted.
        fields = ["id", "title", "canonicalUrl", "excerpt", "body", "topicIds", "publishedAt", "sourceId", "provider", "metadataJson"]
        value = {"items": sorted([{k: i.get(k) for k in fields} for i in data["items"]], key=lambda i: i["id"]),
                 "topics": sorted(data["topics"], key=lambda i: i["id"]), "settings": data["settings"],
                 "sources": sorted([{k: s.get(k) for k in ["id", "enabled", "topicIds", "url", "query", "resultLimit", "intervalMinutes"]} for s in data["sources"]], key=lambda s: s["id"])}
        return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()
    digest = fingerprint(snapshot)
    if args.snapshot:
        previous = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))
        if fingerprint(previous) != digest:
            raise SystemExit("Catalog/settings changed: use the frozen database for comparison.")
    (out / "snapshot.json").write_text(json.dumps(snapshot, ensure_ascii=False, indent=2), encoding="utf-8")
    items = {i["id"]: i for i in snapshot["items"]}
    fields = ["id", "title", "reason", "evidenceItemIds", "rationale", "sourceName", "concepts", "revisit"]
    related = {}
    for item_id in items:
        related[item_id] = [{k: row.get(k) for k in fields} for row in request(f"/items/{item_id}/related")]
    paths = []
    for label, seed in SEEDS.items():
        if seed not in items:
            raise SystemExit(f"Missing evaluation seed: {label} ({seed})")
        current, visited, levels = seed, [seed], []
        for level in range(1, 4):
            candidates = related[current]
            if args.path_aware:
                candidates = [{k: row.get(k) for k in fields} for row in request(
                    f"/items/{current}/related?" + urlencode({"visitedIds": ",".join(visited)}))]
            choice = next((r for r in candidates if r["id"] not in visited), None)
            levels.append({"level": level, "seedId": current, "seedTitle": items[current]["title"],
                           "visitedIds": list(visited), "candidates": candidates, "chosenId": choice["id"] if choice else None})
            if not choice:
                break
            current = choice["id"]
            visited.append(current)
        paths.append({"category": label, "levels": levels})
    result = {"catalogFingerprint": digest, "catalogCount": len(items), "pathAware": args.path_aware,
              "paths": paths, "related": related, "edition": request("/discover/today")}
    (out / "audit.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"catalogCount": len(items), "catalogFingerprint": digest,
                      "paths": [{"category": p["category"], "titles": [r["seedTitle"] for r in p["levels"]]} for p in paths]}, ensure_ascii=True))


if __name__ == "__main__":
    main()
