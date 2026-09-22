"""Engineering regression using reference audio; NOT independent accuracy evaluation."""
import argparse, array, json, pathlib, subprocess, tempfile, wave

p = argparse.ArgumentParser()
p.add_argument('manifest'); p.add_argument('library'); p.add_argument('cli'); p.add_argument('report')
p.add_argument('--dotnet', required=True)
a = p.parse_args()
manifest_path = pathlib.Path(a.manifest).resolve()
manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
checks = []
with tempfile.TemporaryDirectory(prefix='aqtw-reference-') as folder:
 for index, sample in enumerate(manifest['samples']):
  with wave.open(str(manifest_path.parent / sample['file'])) as source:
   rate = source.getframerate(); channels = source.getnchannels(); bits = source.getsampwidth()
   source.setpos(int(sample['startSeconds'] * rate))
   raw = source.readframes(int((sample['endSeconds'] - sample['startSeconds']) * rate))
  cases = [('reference', raw)]
  if sample['groupId'] in {'e9f62d39', '267afca3'}:
   for offset in [0, .2, .4, .6]:
    # Simulate a quieter event at different positions within the automatic window.
    pcm = array.array('h', raw)
    quiet = array.array('h', (round(value * .25) for value in pcm)).tobytes()
    padded = bytes(int(offset * rate) * channels * bits) + quiet
    padded += bytes(max(0, int(1.15 * rate) * channels * bits - len(padded)))
    cases.append((f'auto-window-offset-{offset}', padded))
  for variant, pcm in cases:
   path = pathlib.Path(folder) / f'{index}-{variant}.wav'
   with wave.open(str(path), 'wb') as output:
    output.setparams((channels, bits, rate, 0, 'NONE', 'not compressed')); output.writeframes(pcm)
   run = subprocess.run([a.dotnet, a.cli, 'match', a.library, str(path)], capture_output=True, text=True, encoding='utf-8')
   if run.returncode:
    raise RuntimeError(run.stderr)
   result = json.loads(run.stdout)
   groups = sorted({candidate['groupId'] for candidate in result['candidates']})
   checks.append(dict(group=sample['groupId'], sample=sample['file'], variant=variant,
       passed=sample['groupId'] in groups, status=result['status'], matchedGroups=groups))
report = dict(kind='reference-regression', independent=False,
    note='原参考片段及补零/降音量派生窗口，只检查接入和扫描窗口兼容性，不代表实战准确率。',
    total=len(checks), passed=sum(c['passed'] for c in checks), checks=checks)
pathlib.Path(a.report).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'{report["passed"]}/{report["total"]} reference regression checks passed')
raise SystemExit(0 if all(c['passed'] for c in checks) else 1)
