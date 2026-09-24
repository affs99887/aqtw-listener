"""Copy SoundRadar's original game captures into the library as 听样本 playback clips.

Matching references are processed (the peer references are normalised to 0 dBFS,
gated to silence and trimmed), so they sound unlike the game. These recordings are
copied byte for byte from the pinned SoundRadar archives and only played as a time
window. Each clip's groups are what the production matcher returns for it;
tests/Listener.Tests re-checks that on every run.

    python scripts/import-soundradar-playback.py --eaa0348 eaa0348.srz --4f8289d 4f8289d.srz data/library
"""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path

REPOSITORY = "https://github.com/blood77458/soundradar/blob/{}/soundradar/data/library.srz"
ARCHIVES = {
    "eaa0348": ("eaa0348fa60e8b8477e82b72591f74f40f9574bf", "4cd29319407c8ca01bfaaf3b67ea151c218fc00321ef815139d6d6e6c496f694"),
    "4f8289d": ("4f8289d5bb32813cd7c15922df1a2a769128740a", "a019fced1e3298f3ac087e1a574fca75ed6cd2ad068aef79294638582f43e610"),
}
AMBER = ["peer-012", "peer-016", "peer-021", "peer-023", "peer-024", "peer-035", "peer-037", "peer-038", "peer-040", "peer-050", "peer-053", "peer-055"]
LOCATOR = ["peer-000", "peer-006", "peer-022", "peer-030", "peer-044", "peer-046", "peer-057", "peer-060"]
TABLET = ["peer-001", "peer-002", "peer-018", "peer-042"]
FRAME = ["peer-004", "peer-013", "peer-014", "peer-015", "peer-017", "peer-019", "peer-031", "peer-032", "peer-033", "peer-034", "peer-039"]
# archive, member, sha256, label, action, play window (None = whole file), groups
CLIPS = [
    ("4f8289d", "items/0c5ac384/samples/0001.wav", "8117b13cfcd61e667f560d3b431a4c4113b5cc4be51470df4b9f1f66b17bf89c", "琥珀天心拿起录音", "pickup", (1.857, 3.0), AMBER),
    ("4f8289d", "items/ad6b7dfa/samples/0001.wav", "a63dea1b7d6bed4ee80afe17caa7139bdb183a838bb2a775fb633ac277deadd5", "琥珀天心放下录音", "putdown", (2.118, 3.0), ["peer-047"]),
    ("4f8289d", "items/99d97932/samples/0001.wav", "cf3d1e09c17a5df040400e1fe03e29c137ce7cf00d7e8fa0fe4ca45ee1d3c7ec", "目标定位拿起录音", "pickup", (1.771, 2.971), LOCATOR),
    ("4f8289d", "items/ec9b8a7f/samples/0001.wav", "d48940eba2b373242840803ea1d3ede4e061a96655cad487c73b9883b938e269", "目标定位放下录音 1", "putdown", (1.553, 2.753), ["peer-051"]),
    ("4f8289d", "items/ec9b8a7f/samples/0002.wav", "829236b1d9674526eca45f05de4252d9bb7c28a2f52f0b71b1bb9656fbae3369", "目标定位放下录音 2", "putdown", (2.273, 3.0), ["peer-051"]),
    ("4f8289d", "items/56dfb35b/samples/0001.wav", "041cfc6c1545de86a5508e4c47f0a4401c72e327320fb5053b8f89277793102b", "古董茶壶拿起录音", "pickup", (1.265, 2.465), ["peer-028"]),
    ("eaa0348", "items/388e122c/samples/0001.wav", "9fbae0f94fa4909654733af9c940b45574eb55f34504454894bbae306c4a5fc7", "花瓶拿起录音", "pickup", (0.379, 1.579), ["peer-005", "peer-026"]),
    ("eaa0348", "items/9e7e9e83/samples/0001.wav", "cf0b4329c6bed0e5fc14c1a570539cfabc21d307b938736a69ea76805117871d", "盛宴雕塑拿起录音", "pickup", (2.209, 3.0), ["peer-008", "peer-011", "peer-020", "peer-025", "peer-027"]),
    ("4f8289d", "items/7d037c31/samples/0001.wav", "444d1f81e79356639ef57169fd8f014d431d622a2918f12bbc0a584ef74c2e68", "天命泥板拖动录音 1", "pickup", None, TABLET),
    ("4f8289d", "items/7d037c31/samples/0002.wav", "05bae7a5067a3d138fabfd51479eca541a2ca5029ba8fc15c137744fc2d1b747", "天命泥板拖动录音 2", "pickup", None, TABLET),
    ("4f8289d", "items/7d037c31/samples/0003.wav", "55cf3a37d1ed0289baa395f8e91f63a59afcc241847d4d470edf2a3e3ce7ab4f", "天命泥板拖动录音 3", "pickup", None, TABLET),
    ("eaa0348", "items/cb1c0d1d/samples/0004.wav", "8935966b122f065c75f9ea98217f32203a6c4b2fc0e95737f9aba3202fd44272", "天命泥板拖动录音 4", "pickup", (1.979, 3.0), TABLET),
    ("4f8289d", "items/537b0217/samples/0001.wav", "b31207675f1270055e627314c9c14366a595da4c42218fb942a56aca76a14043", "卡莫纳之星拖动录音", "pickup", None, ["peer-029", "peer-036", "peer-045"]),
    ("4f8289d", "items/983e0b90/samples/0001.wav", "a6360cd3cf2192e8aff1dc53f4e6ff4177056decd0a20c2cc867241645798a57", "金狮子拖动录音", "pickup", None, ["peer-009", "peer-010"]),
    ("4f8289d", "items/ae5a6ebc/samples/0001.wav", "f68901df12f459f8e0bfb6161317613b0fd6774c09c366c8b7e3148949bf5068", "脑电接收装置拖动录音 1", "pickup", None, FRAME),
    ("4f8289d", "items/ae5a6ebc/samples/0002.wav", "9f926f04bdf3944ed2c2e2ba07225a0093d2316bf2400ef12114c131ee6f55bf", "脑电接收装置拖动录音 2", "pickup", None, FRAME),
    ("4f8289d", "items/ae5a6ebc/samples/0003.wav", "5da6521bd53eb53e8a4424d3ce80bc705b2bba4c00990ae48d00a9c7d6f73f2b", "脑电接收装置拖动录音 3", "pickup", None, FRAME),
]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--eaa0348", type=Path, required=True, help="soundradar/data/library.srz at eaa0348")
    parser.add_argument("--4f8289d", dest="r4f8289d", type=Path, required=True, help="soundradar/data/library.srz at 4f8289d")
    parser.add_argument("library", type=Path, help="library folder holding library.json")
    args = parser.parse_args()
    archives = {}
    for name, path in (("eaa0348", args.eaa0348), ("4f8289d", args.r4f8289d)):
        assert sha(path.read_bytes()) == ARCHIVES[name][1], f"{path} is not SoundRadar library.srz at {name}"
        archives[name] = zipfile.ZipFile(path)
    groups = {group["id"] for group in json.loads((args.library / "library.json").read_text(encoding="utf-8"))["groups"]}
    clips = []
    for archive, member, digest, label, action, window, linked in CLIPS:
        data = archives[archive].read(member)
        assert sha(data) == digest, f"{archive}:{member} changed"
        meta = json.loads(archives[archive].read(member.rsplit("/samples/", 1)[0] + "/meta.json"))
        sample = next(s for s in meta["samples"] if s["file"] == member.split("/", 2)[2])
        assert set(linked) <= groups, f"{label}: unknown groups"
        clip_id = "sr-" + member.split("/")[1] + "-" + Path(member).stem
        target = args.library / "playback" / f"{clip_id}.wav"
        target.parent.mkdir(exist_ok=True)
        target.write_bytes(data)
        origin = sample.get("origin", {}).get("fileName", "")
        clips.append(dict(id=clip_id, label=label, action=action, file=f"playback/{clip_id}.wav", sha256=digest,
                          startSeconds=window[0] if window else 0.0, endSeconds=window[1] if window else None,
                          groupIds=sorted(linked),
                          source=f"{REPOSITORY.format(ARCHIVES[archive][0])} {member} ({meta['name']}, {sample.get('source')}, {origin})"))
    manifest = dict(note="听样本只播放这些原始游戏录音：逐字节复制自 SoundRadar 固定版本的 library.srz，仅按时间窗口播放，"
                         "未做滤波、归一化或重采样。识别用参考经过归一化与裁剪，不用于试听。",
                    clips=clips)
    (args.library / "playback.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"copied {len(clips)} original recordings covering {len({g for c in clips for g in c['groupIds']})} groups")


if __name__ == "__main__":
    main()
