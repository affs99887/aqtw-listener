"""Score frozen CLI scan output against independently reviewed video labels.

No media is loaded, no model parameters are changed, and unlabelled events do
not become negatives. Private WAVs/screenshots stay outside the report.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def associate(labels, events, tolerance):
    """Match on timestamp only; choosing a window cannot depend on its verdict.

    Ambiguous labels fail instead of letting one analysis count twice. This
    small, explicitly selected set should be temporally unambiguous.
    """
    claimed = set()
    results = []
    for label in labels:
        lo, hi = label["onset"]
        require(0 <= lo <= hi and hi - lo <= .5, "Invalid visual onset interval")
        candidates = [i for i, event in enumerate(events)
                      if lo - tolerance[0] <= event["onset"] <= hi + tolerance[1]]
        require(not claimed.intersection(candidates), "Ambiguous neighbouring labels")
        claimed.update(candidates)
        nearby = [events[i] for i in candidates]
        correct = [event for event in nearby if event["status"] == "matched"
                   and label.get("item") in event["candidateIds"]]
        release = label.get("drop", [None])[0]
        results.append({
            **label,
            "associatedAnalyses": nearby,
            "analysisTriggered": bool(nearby),
            "correctCandidate": bool(correct) if "item" in label else None,
            "analysisBeforeRelease": any(event["analyzedAt"] < release for event in nearby)
                if release is not None else None,
            "correctCandidateBeforeRelease": any(event["analyzedAt"] < release for event in correct)
                if release is not None and "item" in label else None,
        })
    return results


def evaluate(manifest, root):
    recordings = []
    totals = dict(seconds=0, analyses=0, matchedOutputs=0, knownOperations=0,
                  correctKnownOperations=0, knownDragOperations=0, correctKnownDrags=0,
                  knownTransferOperations=0, correctKnownTransfers=0,
                  hiddenPickups=0, hiddenTriggered=0, hiddenAnalysesBeforeRelease=0,
                  confirmedFalseOutputs=0, unverifiableOutputs=0)
    for recording in manifest["recordings"]:
        events_path = root / recording["id"] / "replay/events.json"
        require(digest(events_path) == recording["eventsSha256"], "Scan output changed: " + recording["id"])
        events = read(events_path)
        for event in events:
            require(all(math.isfinite(event[key]) for key in ["onset", "analyzedAt", "clipStart", "clipEnd"]),
                    "Non-finite scan timestamp")
            require(0 <= event["onset"] <= event["analyzedAt"] <= recording["duration"] + .001,
                    "Scan event outside recording")
            require(event["clipStart"] <= event["clipEnd"] <= event["analyzedAt"] + .001,
                    "Scan used future audio")
            require(event["status"] == "matched" or not event["candidateIds"],
                    "Non-matched event contains accepted candidates")
        known = associate(recording["known"], events, manifest["onsetTolerance"])
        hidden = associate(recording.get("hiddenPickups", []), events, manifest["onsetTolerance"])
        matched = [event for event in events if event["status"] == "matched"]
        reviews = recording["predictionReviews"]
        require(len(reviews) == len(matched), "Every accepted output needs a review or explicit unknown label")
        require(len({r["index"] for r in reviews}) == len(reviews), "Duplicate output review")
        reviewed = []
        for review in reviews:
            require(0 <= review["index"] < len(matched), "Invalid accepted-output index")
            event = matched[review["index"]]
            require(abs(event["onset"] - review["onset"]) < .001, "Review timestamp mismatch")
            require(review["verdict"] in ["false", "unverifiable"], "Unknown review verdict")
            reviewed.append({**review, "event": event})
        totals["seconds"] += recording["duration"]
        totals["analyses"] += len(events)
        totals["matchedOutputs"] += len(matched)
        totals["knownOperations"] += len(known)
        totals["correctKnownOperations"] += sum(e["correctCandidate"] for e in known)
        for action, suffix in [("drag", "Drags"), ("transfer", "Transfers")]:
            subset = [e for e in known if e["action"] == action]
            totals["knownDragOperations" if action == "drag" else "knownTransferOperations"] += len(subset)
            totals["correctKnown" + suffix] += sum(e["correctCandidate"] for e in subset)
        totals["hiddenPickups"] += len(hidden)
        totals["hiddenTriggered"] += sum(e["analysisTriggered"] for e in hidden)
        totals["hiddenAnalysesBeforeRelease"] += sum(e["analysisBeforeRelease"] for e in hidden)
        totals["confirmedFalseOutputs"] += sum(e["verdict"] == "false" for e in reviews)
        totals["unverifiableOutputs"] += sum(e["verdict"] == "unverifiable" for e in reviews)
        recordings.append({
            "id": recording["id"], "duration": recording["duration"],
            "audioSha256": recording["audioSha256"], "eventsSha256": recording["eventsSha256"],
            "analysisCount": len(events), "matchedOutputCount": len(matched),
            "knownOperations": known, "hiddenPickups": hidden, "reviewedOutputs": reviewed,
        })
    totals["seconds"] = round(totals["seconds"], 3)
    return {
        "kind": "frozen-version-gameplay-replay-with-partial-visual-labels",
        "baseline": manifest["baseline"], "onsetTolerance": manifest["onsetTolerance"],
        "totals": totals, "acceptancePassed": False, "overallAccuracy": None,
        "limitations": manifest["limitations"], "recordings": recordings,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("replay_root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    report = evaluate(read(args.manifest), args.replay_root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report["totals"], ensure_ascii=False))
    print("Partial visual labels only: overall accuracy is not estimated.")


if __name__ == "__main__":
    main()
