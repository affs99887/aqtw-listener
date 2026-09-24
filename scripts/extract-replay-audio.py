"""Decode a user-supplied recording to timestamp-aligned mono PCM for CLI scan.

Requires PyAV and NumPy. Does not concatenate packets or discard audio gaps:
every output frame is placed using its resampled PTS. Missing/overlapping data
fails validation instead of silently shifting subsequent video labels.
"""
import argparse
import hashlib
import json
import wave
from pathlib import Path

import av
import numpy as np


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("video", type=Path)
    parser.add_argument("output_dir", type=Path)
    parser.add_argument("--start", type=float, default=0)
    parser.add_argument("--end", type=float, required=True)
    parser.add_argument("--stream", type=int, default=1, help="Container stream index, not audio ordinal")
    args = parser.parse_args()
    rate = 24000
    first, last = round(args.start * rate), round(args.end * rate)
    if first < 0 or last <= first or (last - first) * 2 > 100_000_000:
        raise ValueError("Invalid range or PCM exceeds CLI's 100 MB input limit")
    with av.open(str(args.video)) as container:
        stream = next((s for s in container.streams.audio if s.index == args.stream), None)
        if stream is None:
            raise ValueError("Requested audio stream does not exist")
        samples = np.zeros(last - first, dtype="<i2")
        covered = np.zeros(last - first, dtype=bool)
        resampler = av.AudioResampler(format="s16", layout="mono", rate=rate)
        container.seek(max(0, int((args.start - 1) / stream.time_base)), stream=stream, backward=True)

        def accept(frame):
            if frame.pts is None:
                raise ValueError("Audio frame is missing PTS")
            position = round(frame.pts * frame.time_base * rate)
            data = frame.to_ndarray().reshape(-1)
            lo, hi = max(first, position), min(last, position + len(data))
            if hi <= lo:
                return
            if covered[lo - first:hi - first].any():
                raise ValueError("Overlapping resampled PTS")
            samples[lo - first:hi - first] = data[lo - position:hi - position]
            covered[lo - first:hi - first] = True

        for frame in container.decode(stream):
            if frame.pts is None:
                raise ValueError("Source audio frame is missing PTS")
            if float(frame.pts * frame.time_base) > args.end + .1:
                break
            for output in resampler.resample(frame):
                accept(output)
        for output in resampler.resample(None):
            accept(output)
        missing = int((~covered).sum())
        if missing:
            raise ValueError(f"{missing} uncovered audio samples; inspect timestamps before evaluation")
        args.output_dir.mkdir(parents=True, exist_ok=True)
        path = args.output_dir / f"audio-{stream.index}.wav"
        with wave.open(str(path), "wb") as writer:
            writer.setparams((1, 2, rate, len(samples), "NONE", "not compressed"))
            writer.writeframes(samples.tobytes())
        with path.open("rb") as file:
            sha256 = hashlib.file_digest(file, "sha256").hexdigest()
        info = {
            "file": args.video.name, "duration": container.duration / 1e6,
            "start": args.start, "end": args.end,
            "streams": [{"index": s.index, "type": s.type, "codec": s.codec_context.name,
                         "startTime": float(s.start_time * s.time_base) if s.start_time is not None else None}
                        for s in container.streams],
            "audio": {"path": path.name, "sampleRate": rate, "samples": len(samples),
                      "uncoveredSamples": missing, "sha256": sha256,
                      "peak": float(np.max(np.abs(samples.astype(float))) / 32768)},
        }
    (args.output_dir / "media.json").write_text(json.dumps(info, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(info, ensure_ascii=False))


if __name__ == "__main__":
    main()
