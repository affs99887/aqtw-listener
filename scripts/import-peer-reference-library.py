"""Attach verified catalog metadata to the frozen 62-reference experiment.

This copies data only. It does not import or execute the other application's code.
The output must be a new directory so an existing library cannot be damaged.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


# Aliases additionally checked against existing catalog images/source metadata.
ITEM_ALIASES = {
    6: "267afca3-6", 11: "9e7e9e83-0", 14: "361affd2-8",
    15: "361affd2-7", 18: "cb1c0d1d-1", 47: "e9f62d39-8",
}
IMAGE_ALIASES = {
    "盛宴雕像": "盛宴雕塑", "黎明": "《黎明》", "黄金面具仿件": "古国黄金面具仿件",
    "机械手臂": "T-008 终极机械手臂", "理想国试剂盒": "“理想国”试剂盒",
    "陈列镜片": "同步阵列镜片", "目标定位模块": "目标定位器模块",
    "脑电装置": "脑电接收装置", "原型机模块": "“TESM-6”原型计算机验算模块",
}
# New items: grid lines were visually checked in the inventory icon crops.
# Do not derive grid size from an image's aspect ratio (several are square crops).
NEW_GRIDS = {
    1: (4, 2), 2: (3, 2), 3: (2, 3), 5: (2, 3), 7: (2, 2),
    8: (1, 2), 9: (2, 1), 12: (2, 3), 20: (1, 2), 25: (2, 3),
    41: (3, 2), 42: (3, 2), 43: (2, 2), 44: (1, 1), 45: (1, 1),
    46: (1, 2), 48: (1, 1), 49: (1, 1), 50: (2, 1), 52: (2, 2),
    54: (2, 2), 55: (2, 1), 56: (1, 2), 57: (1, 3), 58: (2, 1),
    59: (1, 1), 60: (1, 1), 61: (2, 2),
}
USER_CONFIRMED = {12, 20, 25}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--experiment", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    prepared = args.experiment / "library"
    assert not args.output.exists(), "output must be a new directory"
    assert sha(prepared / "radar-index.bin") == "b154e7b5a09acfaa14b5fc3a7aca607c377c432fbd8ff615875405d9ea89742a"
    manifest = read(args.experiment / "manifest.json")
    assert sha(args.assets / "_lib/refs.npz") == manifest["sourceRefSha256"]
    assert sha(args.assets / "_lib/index.json") == manifest["sourceIndexSha256"]
    library = read(prepared / "library.json")
    baseline = read(args.baseline / "library.json")
    originals = {item["id"]: item for item in baseline["items"]}
    metadata = read(args.assets / "_lib/index.json")
    images = {p.stem: p for p in (args.assets / "图标库").rglob("*.png")}
    assert len(library["items"]) == len(library["groups"]) == len(metadata) == 62
    planned, references, provenance = [], [], []
    for number, (item, group, mapping, source) in enumerate(zip(
            library["items"], library["groups"], manifest["mapping"], metadata, strict=True)):
        group_id = f"peer-{number:03}"
        assert item["id"] == group["id"] == mapping["id"] == group_id
        assert item["name"] == source["name"] == mapping["sourceName"]
        old_id = ITEM_ALIASES.get(number, mapping["baselineItemId"])
        source_name = source["name"]
        image_source = images[IMAGE_ALIASES.get(source_name, source_name)]
        if old_id:
            old = originals[old_id]
            width, height = old["gridWidth"], old["gridHeight"]
            grid_source = "existing-catalog-reviewed-source"
            image_path = args.baseline / old["thumbnail"]
        else:
            width, height = NEW_GRIDS[number]
            grid_source = "user-confirmed-20260924" if number in USER_CONFIRMED else "inventory-icon-grid-visual-review"
            image_path = image_source
        item.update(id=old_id or group_id, name="阵列镜片" if number == 21 else source_name,
                    isGold=source["tier"] == "大金", gridWidth=width, gridHeight=height,
                    gridVerified=True, cells=width * height,
                    thumbnail=f"images/{old_id or group_id}.png", catalogSource=grid_source)
        group["name"] = item["name"]
        group["itemIds"] = [item["id"]]
        assert group["action"] == "pickup" and group["threshold"] == .75
        assert len(group["templates"]) == 1
        template = group["templates"][0]
        template["sourceUrl"] = "local:user-provided-peer-v0.67/refs.npz"
        audio = prepared / mapping["file"]
        digest = sha(audio)
        assert digest == mapping["sha256"] == template["audioSha256"].lower()
        planned.extend([(audio, args.output / mapping["file"]),
                        (image_path, args.output / item["thumbnail"])])
        references.append(dict(groupId=group_id, templateId=template["id"],
                               file=mapping["file"], sha256=digest, recordingId=template["recordingId"]))
        provenance.append(dict(sourceReferenceId=group_id, sourceName=source_name,
                               itemId=item["id"], name=item["name"], baselineItemId=old_id,
                               gridWidth=width, gridHeight=height, gridSource=grid_source,
                               file=mapping["file"], sha256=digest,
                               sourceFloat32Sha256=mapping["sourceFloat32Sha256"],
                               thumbnail=item["thumbnail"], thumbnailSha256=sha(image_path),
                               thumbnailSource="soundradar-existing-catalog" if old_id else "user-provided-peer-v0.67",
                               gridEvidenceImageSha256=sha(image_source)))
    assert len({item["id"] for item in library["items"]}) == 62
    library.update(version="0.3.3-peer-pickup-20260924", validationStatus="uncalibrated",
                   notes="62 条用户指定的拿起参考；沿用本项目识别逻辑和阈值，一声可返回多件同音候选。不使用放下辅助库。匹配度不是单件确定率或实战正确率。")
    args.output.mkdir(parents=True)
    for source, target in planned:
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    shutil.copy2(prepared / "radar-index.bin", args.output / "radar-index.bin")
    write(args.output / "library.json", library)
    write(args.output / "references.json", {"samples": references})
    write(args.output / "provenance.json", dict(
        version=library["version"], source="User-provided peer v0.67 packaged material library",
        sourceExecutableSha256="f421fc13e12dcb495ef143895e1398b9c44ac4c73ba3d94b74f3f17fb7cfec35",
        sourceReferencesSha256=manifest["sourceRefSha256"], sourceIndexSha256=manifest["sourceIndexSha256"],
        licenseStatus="No verifiable source repository or license supplied for these peer materials; original rights retained.",
        conversion=manifest["conversion"], indexSha256=library["engineIndexSha256"],
        recognition="Existing SoundRadar-based matcher, pickup scanner and .75/.78 thresholds unchanged; no putdown auxiliary matching.",
        entries=provenance))
    print(json.dumps(dict(items=62, references=len(references), existingIdsRetained=sum(bool(p["baselineItemId"]) for p in provenance),
                          userConfirmed={p["name"]: f'{p["gridWidth"]}x{p["gridHeight"]}' for p in provenance if p["gridSource"].startswith("user-confirmed")},
                          indexSha256=library["engineIndexSha256"]), ensure_ascii=False))


if __name__ == "__main__":
    main()
