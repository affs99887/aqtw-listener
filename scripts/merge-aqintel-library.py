"""Merge the 暗区情报站小助手 1.4.0 material library into a staging copy of data/library.

The game plays one pickup sound per *class* of items, and both libraries describe the same
classes: 13 of the peer's 17 sounds were matched by our own recognizer to our reference
recordings at 0.95–0.998 (9 are the same recording). For those sounds the peer library is
an item catalogue extension: its items join every one of our groups that carries that sound,
so a recognised sound lists all items known to use it. Three sounds we do not have (电脑硬件,
奢华茶具类, 火焰喷灯) become new groups whose reference is the peer's audition WAV, added
through Listener.Cli add-reference so library.json / references.json / the manifest hash
are produced by the same code path as every other reference.

Usage: python scripts/merge-aqintel-library.py <staging-dir>
Reads data/library, the extracted package under artifacts/暗区情报站-1.4.0-素材库 and the
handoff correspondence; writes the staged library plus merge-plan.json for review. Does not
touch data/library.
"""
from __future__ import annotations

import hashlib
import json
import shutil
import sys
import wave
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT / "data/library"
SRC = ROOT / "artifacts/暗区情报站-1.4.0-素材库"
CORR = ROOT / "artifacts/material-merge-handoff-20261013/analysis/item-correspondence.json"
PROBE = ROOT / "artifacts/material-merge-handoff-20261013/analysis/baseline-audio-probe.json"
PACKAGE_SHA = "de01d901ac6142db82b09697e4dd71935cc46b946af659163550ac44d16cb0ca"
SOURCE_LABEL = "暗区情报站小助手 1.4.0 内置素材（安装包静态解包；未附许可证，原权利保留）"
# Measured against the same takes among our SoundRadar originals (.tools/research/merge-aqintel/peer-audio-impact.json).
PLAYBACK_PROCESSING = ("暗区情报站安装包内的试听 WAV，不是原始录音：与同一段原始游戏录音相比音量整体不同（约 +5 dB），"
                       "约 18 kHz 以上被截去（疑似有损编码）。")
PLAYBACK_NOTE = ("听样本优先播放原始游戏录音：逐字节复制自 SoundRadar 固定版本的 library.srz，仅按时间窗口播放，未做滤波、归一化或重采样。"
                 "没有原始录音的音效组才附暗区情报站的试听 WAV，标为非原始录音（processing 字段写明差异）。识别用参考经过归一化与裁剪，不用于试听。")

# Names the peer uses for items we already have under another name. Confirmed by image
# comparison on 2026-10-14 (same render in both libraries). 电台 is NOT 军用电台: different
# image, different grid, different sound class.
ALIASES = {"鎏金羽宫": "鎏金羽宫八音盒", "黄金面具": "黄金面具仿件", "理想国": "理想国试剂盒", "量子2000": "量子F2000玩具机器人",
           "赫夫拉彩蛋": "赫芙拉彩蛋", "体外除颤器": "体外去颤器", "液压扩张仪": "液压扩张器"}
# Peer sounds that are not in our library: new groups seeded from the audition WAV.
NEW_SOUNDS = {"11": ("aqintel140-sound-11", "电脑硬件类（情报站声音 11）"),
              "14": ("aqintel140-sound-14", "奢华茶具类（情报站声音 14）"),
              "16": ("aqintel140-sound-16", "火焰喷灯（情报站声音 16）")}
# Peer sounds whose WAV is a different recording of a sound we have (not a waveform duplicate,
# scored below 0.97 against our templates): worth a second template.
VARIANT_SOUNDS = {"6"}
# Grid conflicts kept at our value: the same cells rotated, and our thumbnail shows our orientation.
GRID_CONFLICTS = {"燃料桶": "我们 3×2，情报站 2 宽 3 高；同为 6 格，本库缩略图的物品底色为 3:2"}
# Our sizes for these two were swapped: the coloured item background in our own thumbnail (which
# covers the item's grid footprint, as for the other 59 screenshot thumbnails) agrees with 情报站.
GRID_CORRECTIONS = {"胶囊电视": (2, 3, "原 2×2；本库缩略图物品底色 2:3（与 2×3 的盛宴雕像相同），情报站 2 宽 3 高"),
                    "花瓶": (2, 2, "原 2×3；本库缩略图物品底色 1:1（与 2×2 的古董茶壶相同），情报站 2×2")}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def to_mono_wav(src: Path, dst: Path):
    with wave.open(str(src)) as h:
        sr, ch, bits = h.getframerate(), h.getnchannels(), h.getsampwidth() * 8
        assert bits == 16, src
        pcm = np.frombuffer(h.readframes(h.getnframes()), dtype="<i2").astype(np.float32)
    if ch > 1:
        pcm = pcm.reshape(-1, ch).mean(axis=1)
    with wave.open(str(dst), "wb") as h:
        h.setparams((1, 2, sr, len(pcm), "NONE", "not compressed"))
        h.writeframes(np.clip(np.round(pcm), -32768, 32767).astype("<i2").tobytes())


# The package's renders are transparent, without the game's rarity background. The app draws this
# colour over the item's grid footprint behind them (ItemDefinition.ThumbnailBackground): the median
# background of our screenshot tiles of the same rarity, over the 50 items both libraries share.
RARITY_BACKGROUND = {"红": "#321612", "金": "#32220E", "紫": "#221A2F"}


def thumbnail(src: Path, dst: Path, size=192):
    """Scale the original uniformly to ≤ size; nothing else is changed (transparency kept)."""
    im = Image.open(src).convert("RGBA")
    if max(im.size) > size:
        im.thumbnail((size, size), Image.LANCZOS)
    im.save(dst, optimize=True)


def main(staging: Path):
    if staging.exists():
        shutil.rmtree(staging)
    shutil.copytree(LIB, staging)
    lib = read_json(staging / "library.json")
    refs = read_json(staging / "references.json")
    playback = read_json(staging / "playback.json")
    prov = read_json(staging / "provenance.json")
    if "aqintel140" in prov:
        sys.exit("data/library already contains the merge; run against the v0.4.5 library (git show b384910:data/library)")
    original_clips = {g for c in playback["clips"] for g in c["groupIds"]}
    playback["note"] = PLAYBACK_NOTE
    index = read_json(SRC / "素材索引.json")
    corr = {c["incomingId"]: c for c in read_json(CORR)["items"]}
    probe = {r["soundId"]: r for r in read_json(PROBE)["results"]}
    items = {i["id"]: i for i in lib["items"]}
    by_name = {i["name"]: i["id"] for i in lib["items"]}
    groups = {g["id"]: g for g in lib["groups"]}
    file_groups: dict[str, set[str]] = {}
    for s in refs["samples"]:
        file_groups.setdefault(s["file"], set()).add(s["groupId"])

    plan = {"package": {"name": index["source"]["name"], "version": index["source"]["version"], "installerSha256": index["source"]["installerSha256"],
                        "templatesGeneratedAt": index["source"]["templatesGeneratedAt"]},
            "items": [], "sounds": [], "newGroups": [], "referenceWavs": [], "playbackClips": [], "gridConflicts": GRID_CONFLICTS,
            "aliasesConfirmed": ALIASES, "aliasesRejected": {"电台": "与军用电台图片、格数、声音类均不同，按新物品处理"}}
    assert index["source"]["installerSha256"] == PACKAGE_SHA

    # ---- items -------------------------------------------------------------------------
    incoming_to_ours: dict[str, str] = {}
    (staging / "images").mkdir(exist_ok=True)
    for sound in index["sounds"]:
        for it in sound["items"]:
            c = corr[it["itemId"]]
            name = it["itemName"]
            h, w = (int(v) for v in it["gridSize"].split("*"))  # peer stores rows*columns
            entry = {"incomingId": it["itemId"], "name": name, "soundId": sound["soundId"], "rarity": it["rarity"], "sourcePriceText": it["value"],
                     "incomingGridRowsCols": [h, w]}
            if name in by_name:
                ours = by_name[name]; entry.update(action="existing", ourId=ours, matchedBy="exact-name")
            elif name in ALIASES:
                ours = by_name[ALIASES[name]]; entry.update(action="existing", ourId=ours, matchedBy="alias", ourName=ALIASES[name])
            else:
                ours = f"aqintel140-{it['itemId']}"
                image = f"images/{ours}.png"
                thumbnail(SRC / it["image"], staging / image)
                items[ours] = {"id": ours, "name": name, "isGold": it["rarity"] == "红", "referenceValue": None, "valueSource": None, "valueDate": None,
                               "gridWidth": w, "gridHeight": h, "thumbnail": image, "gridVerified": True, "catalogSource": "aqintel-1.4.0",
                               "thumbnailBackground": RARITY_BACKGROUND[it["rarity"]]}
                lib["items"].append(items[ours]); by_name[name] = ours
                entry.update(action="new", ourId=ours, isGold=it["rarity"] == "红", gridWidth=w, gridHeight=h,
                             sourceImage=it["image"], sourceImageSha256=sha256(SRC / it["image"]), thumbnail=image)
            if name in GRID_CONFLICTS:
                entry["gridConflict"] = GRID_CONFLICTS[name]
            if name in GRID_CORRECTIONS:
                gw, gh, why = GRID_CORRECTIONS[name]
                items[ours].update(gridWidth=gw, gridHeight=gh, cells=gw * gh)
                entry["gridCorrected"] = {"gridWidth": gw, "gridHeight": gh, "evidence": why}
            incoming_to_ours[it["itemId"]] = ours
            plan["items"].append(entry)

    # ---- sound classes -----------------------------------------------------------------
    (staging / "playback").mkdir(exist_ok=True)
    for sound in index["sounds"]:
        sid = sound["soundId"]
        member_ids = [incoming_to_ours[it["itemId"]] for it in sound["items"]]
        entry = {"soundId": sid, "audioFile": sound["audioFile"], "audioSha256": sha256(SRC / sound["audioFile"]), "durationMs": sound["audioDurationMs"],
                 "peerVariants": sound["variantCount"], "items": member_ids}
        if sid in NEW_SOUNDS:
            gid, gname = NEW_SOUNDS[sid]
            lib["groups"].append({"id": gid, "name": gname, "action": "pickup", "itemIds": member_ids, "threshold": .82, "templates": []})
            groups[gid] = lib["groups"][-1]
            class_groups = [gid]
            entry.update(mapping="new-group", groupIds=class_groups)
            plan["newGroups"].append({"id": gid, "name": gname, "items": member_ids, "referenceFrom": sound["audioFile"]})
        else:
            # The class = our groups the probe matched with this WAV, plus any group already holding
            # one of its items (both agree for every sound; the union guards the two short clips the
            # probe could only match with padding).
            class_groups = set()
            res = probe[sid]["result"]
            class_groups.update(c["groupId"] for c in res.get("candidates", []))
            syn = probe[sid].get("syntheticContextDiagnostic") or {}
            class_groups.update(c["groupId"] for c in syn.get("candidates", []))
            for mid in member_ids:
                for g in lib["groups"]:
                    if mid in g["itemIds"]:
                        class_groups.add(g["id"])
            class_groups = sorted(class_groups)
            # Only the peer's *new* items join the class groups. Our existing items keep their own
            # group: candidates are deduplicated by item, so an item listed in several near-identical
            # groups would surface from the top-scoring one only and the others would vanish from
            # the candidate list (and from the per-reference regression tests).
            new_ids = {e["ourId"] for e in plan["items"] if e["action"] == "new"}
            added = {}
            for gid in class_groups:
                g = groups[gid]
                new_members = [mid for mid in member_ids if mid in new_ids and mid not in g["itemIds"]]
                if new_members:
                    g["itemIds"] = list(g["itemIds"]) + new_members
                    added[gid] = new_members
            entry.update(mapping="existing-class", groupIds=class_groups, itemsAddedPerGroup=added)
        # reference WAV for new sounds and for the confirmed variant
        if sid in NEW_SOUNDS or sid in VARIANT_SOUNDS:
            mono = staging / "audio" / f"aqintel140-sound-{int(sid):02d}.wav"
            to_mono_wav(SRC / sound["audioFile"], mono)
            plan["referenceWavs"].append({"soundId": sid, "file": f"audio/{mono.name}", "sha256": sha256(mono), "sourceFile": sound["audioFile"],
                                          "sourceSha256": entry["audioSha256"], "conversion": "stereo 48 kHz 16-bit → mono average, no other processing",
                                          "groupIds": [g for g in class_groups if groups[g]["action"] == "pickup"],
                                          "recordingId": f"aqintel140-sound-{int(sid):02d}"})
        # audition clip: the package's own stereo file, byte for byte, but only for groups with no
        # original SoundRadar capture (the package's WAVs are level-shifted and band-limited)
        pickup_groups = [g for g in class_groups if groups[g]["action"] == "pickup" and g not in original_clips]
        if pickup_groups:
            clip_file = f"playback/aqintel140-sound-{int(sid):02d}.wav"
            shutil.copyfile(SRC / sound["audioFile"], staging / clip_file)
            label = f"情报站试听 · {sound['items'][0]['itemName']}" + (f"等 {len(sound['items'])} 件" if len(sound["items"]) > 1 else "") + "（非原始录音）"
            playback["clips"].append({"id": f"aqintel140-sound-{int(sid):02d}", "label": label, "action": "pickup", "file": clip_file,
                                      "sha256": entry["audioSha256"].upper(), "startSeconds": 0, "endSeconds": None, "groupIds": pickup_groups,
                                      "source": SOURCE_LABEL, "processing": PLAYBACK_PROCESSING})
            plan["playbackClips"].append({"id": f"aqintel140-sound-{int(sid):02d}", "groupIds": pickup_groups})
        plan["sounds"].append(entry)

    # ---- metadata ----------------------------------------------------------------------
    lib["version"] = "0.4.6-aqintel-merge-20261014"
    lib["validationStatus"] = "merged-aqintel-1.4.0-pending-raid-validation"
    lib["notes"] = ("合并暗区情报站小助手 1.4.0 素材库（138 件物品、17 段试听音效）：对方 17 个声音中 13 个经本库识别器确认与本库已有声音类相同"
                    "（9 个为同一段录音），其物品并入本库对应声音类的全部音效组；电脑硬件类、奢华茶具类、火焰喷灯为本库没有的声音，以对方试听 WAV 建组；"
                    "宝石类补一条变体参考。原 62 件物品、62 个音效组、全部参考与两个放下声组保留，稳定 ID 不变。格数按对方的 行*列 转置为 宽×高；"
                    "大红 = 对方稀有度「红」；对方价格只作来源记录。" + lib["notes"])
    prov["aqintel140"] = {"package": plan["package"], "source": SOURCE_LABEL, "mergedOn": "2026-10-14",
                          "method": "物品按名称（含 7 组图片确认的别名）对应；声音类按本库识别器对对方 17 段 WAV 的匹配（analysis/baseline-audio-probe.json）与波形重合判定；"
                                    "格数 行*列 → 宽×高；稀有度 红 → isGold。",
                          "items": plan["items"], "sounds": plan["sounds"], "newGroups": plan["newGroups"], "referenceWavs": plan["referenceWavs"],
                          "gridConflictsKeptOurs": GRID_CONFLICTS,
                          "gridCorrected": {k: {"gridWidth": v[0], "gridHeight": v[1], "evidence": v[2]} for k, v in GRID_CORRECTIONS.items()},
                          "aliasesConfirmedByImage": ALIASES, "aliasesRejected": plan["aliasesRejected"]}
    prov["version"] = lib["version"]
    write_json(staging / "library.json", lib)
    write_json(staging / "playback.json", playback)
    write_json(staging / "provenance.json", prov)
    write_json(staging.parent / "merge-plan.json", plan)
    new_items = [e for e in plan["items"] if e["action"] == "new"]
    print(f"items: {len(lib['items'])} (new {len(new_items)}, existing matched {len(plan['items']) - len(new_items)}); groups: {len(lib['groups'])}; "
          f"playback clips: {len(playback['clips'])}; reference WAVs to add: {len(plan['referenceWavs'])}")
    for r in plan["referenceWavs"]:
        print("  add-reference", r["file"], r["recordingId"], ",".join(r["groupIds"]))


if __name__ == "__main__":
    main(Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "artifacts/material-merge-staging/library")
