"""Package verified, trimmed reference WAVs for playback and personal-library rebuilds."""
import hashlib, io, json, pathlib, sys, wave

source = pathlib.Path(sys.argv[1])
target = pathlib.Path(sys.argv[2])
manifest = json.loads(source.read_text(encoding="utf-8"))
library = json.loads((target / "library.json").read_text(encoding="utf-8"))
references = []
for group in library["groups"]:
    samples = [s for s in manifest["samples"] if s["groupId"] == group["id"]]
    if len(samples) != len(group["templates"]):
        raise ValueError(f"Sample count mismatch: {group['id']}")
    for sample, template in zip(samples, group["templates"]):
        path = source.parent / sample["file"]
        if hashlib.sha256(path.read_bytes()).hexdigest().lower() != template["audioSha256"].lower():
            raise ValueError(f"Source hash mismatch: {path}")
        with wave.open(str(path)) as src:
            rate, channels, bits = src.getframerate(), src.getnchannels(), src.getsampwidth()
            src.setpos(int(template["startSeconds"] * rate))
            raw = src.readframes(int((template["endSeconds"] - template["startSeconds"]) * rate))
        output = io.BytesIO()
        with wave.open(output, "wb") as dst:
            dst.setparams((channels, bits, rate, 0, "NONE", "not compressed"))
            dst.writeframes(raw)
        relative = f"audio/{template['id']}.wav"
        (target / "audio").mkdir(exist_ok=True)
        (target / relative).write_bytes(output.getvalue())
        references.append(dict(groupId=group["id"], templateId=template["id"], file=relative,
            sha256=hashlib.sha256(output.getvalue()).hexdigest(), recordingId=template["recordingId"]))
(target / "references.json").write_text(json.dumps(dict(samples=references), ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Verified and packaged {len(references)} reference clips")
