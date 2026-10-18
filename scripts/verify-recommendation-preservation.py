"""Compare private pre/post API exports, including every old field and ID.

The only permitted change to an existing field during this upgrade is archiving
an active edition's day key. --restart permits no changes at all. New fields and
new daily entries are additive; input files are never modified.
"""
import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before")
    parser.add_argument("after")
    parser.add_argument("--output")
    parser.add_argument("--restart", action="store_true")
    args = parser.parse_args()
    before, after = [json.loads(Path(p).read_text(encoding="utf-8")) for p in [args.before, args.after]]
    problems, archived = [], []
    for table, rows in before.items():
        if not isinstance(rows, list):
            continue
        def identity(row):
            if table == "itemTopics":
                return row["itemId"] + ":" + row["topicId"]
            return row.get("id", row.get("key", row.get("itemId")))
        new_rows = {identity(row): row for row in after[table]}
        if args.restart and len(rows) != len(new_rows):
            problems.append(f"{table}: row count changed")
        for row in rows:
            key = identity(row)
            if key not in new_rows:
                problems.append(f"{table}: missing {key}")
                continue
            current = new_rows[key]
            for field, value in row.items():
                if current.get(field) == value:
                    continue
                if not args.restart and table == "editions" and field == "day" and current[field] == value + ":before-recommendation-evidence:" + key:
                    archived.append(key)
                    continue
                problems.append(f"{table}: {key}: {field} changed")
    if before["settings"] != after["settings"]:
        problems.append("User settings changed")
    result = {"preserved": not problems, "differences": problems, "archivedEditionIds": archived,
              "beforeCounts": {k: len(v) for k, v in before.items() if isinstance(v, list)},
              "afterCounts": {k: len(v) for k, v in after.items() if isinstance(v, list)},
              "holeOrigins": {h["id"]: h.get("origin") for h in after["holes"]},
              "nodeOrigins": {n["id"]: n.get("origin") for n in after["nodes"]}}
    text = json.dumps(result, indent=2)
    if args.output:
        Path(args.output).write_text(text, encoding="utf-8")
    print(text)
    raise SystemExit(1 if problems else 0)


if __name__ == "__main__":
    main()
