"""Build optional confirmation-only sounds from explicitly labelled source samples.
The pickup catalog/index is unchanged. No gameplay recording enters this archive.
"""
import argparse, array, hashlib, io, json, pathlib, subprocess, wave, zipfile

p = argparse.ArgumentParser()
p.add_argument('archive'); p.add_argument('library'); p.add_argument('engine')
a = p.parse_args()
root = pathlib.Path(a.library); output = root / 'putdown'; output.mkdir(exist_ok=True)
(output / 'audio').mkdir(exist_ok=True)
base = json.loads((root / 'library.json').read_text('utf-8-sig'))
mapping = {'ec9b8a7f': ('目标定位-放下', '267afca3'),
           'e8b0425c': ('天命泥板-放下', 'cb1c0d1d'),
           'ad6b7dfa': ('琥珀天心-放下', 'e9f62d39'),
           'b9d3e597': ('古董茶壶-放下', '56dfb35b')}
provenance = []; groups = []; reference = output / 'reference.srz'
with zipfile.ZipFile(a.archive) as source, zipfile.ZipFile(reference, 'w', zipfile.ZIP_DEFLATED) as target:
    for original_id, (expected_name, group_id) in mapping.items():
        original = f'items/{original_id}/'
        meta = json.loads(source.read(original + 'meta.json'))
        if meta['name'] != expected_name: raise ValueError('Source action label changed: ' + original_id)
        group = next(g for g in base['groups'] if g['id'] == group_id)
        groups.append(dict(id=group_id, name=expected_name, action='putdown',
                           itemIds=group['itemIds'], threshold=.86, templates=[]))
        samples = []
        for i, entry in enumerate(meta['samples']):
            raw = source.read(original + entry['file'])
            with wave.open(io.BytesIO(raw)) as w:
                rate=w.getframerate(); channels=w.getnchannels()
                if w.getsampwidth()!=2 or channels!=1 or rate!=48000: raise ValueError('Unsupported source audio')
                pcm = array.array('h', w.readframes(w.getnframes()))
            hop=int(rate*.01); span=int(rate*.06)
            energies=[sum(v*v for v in pcm[n:n+span]) for n in range(0,len(pcm)-span,hop)]
            peak=max(range(len(energies)),key=energies.__getitem__)*.01+.03
            start=max(0,peak-.17); end=min(len(pcm)/rate,start+.5)
            path=output/'audio'/f'{group_id}-{i+1}.wav'
            with wave.open(str(path),'wb') as w:
                w.setparams((1,2,rate,0,'NONE','not compressed'))
                w.writeframes(pcm[round(start*rate):round(end*rate)].tobytes())
            filename=f'samples/{i+1}.wav';target.writestr(f'items/{group_id}/{filename}',path.read_bytes())
            samples.append(dict(file=filename,storedSampleRate=rate,storedChannels=1,storedBitsPerSample=16,frames=round((end-start)*rate)))
            provenance.append(dict(groupId=group_id, action='putdown', sourceName=expected_name,
                sourceFile=original+entry['file'], sourceSha256=hashlib.sha256(raw).hexdigest(),
                file='audio/'+path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                startSeconds=start,endSeconds=end))
        target.writestr(f'items/{group_id}/meta.json',json.dumps(dict(id=group_id,name=expected_name,threshold=.86,samples=samples),ensure_ascii=False))
    target.writestr('manifest.json',json.dumps(dict(version=1,order=[g['id'] for g in groups])))
index=output/'radar-index.bin'
subprocess.run([a.engine,'build',str(reference),str(index)],check=True)
reference.unlink()
catalog=dict(schemaVersion=1,version='putdown-confirmation-1',primaryEngine='soundradar',
    engineIndexSha256=hashlib.sha256(index.read_bytes()).hexdigest(),validationStatus='uncalibrated',
    notes='仅为已匹配拿起声提供辅助确认；不得创建候选、筛除候选或提高单件匹配度。',
    items=[item for item in base['items'] if any(item['id'] in g['itemIds'] for g in groups)],groups=groups)
(output/'library.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),'utf-8')
(output/'provenance.json').write_text(json.dumps(dict(
    source='https://github.com/blood77458/soundradar/blob/4f8289d5bb32813cd7c15922df1a2a769128740a/soundradar/data/library.srz',
    archiveSha256=hashlib.sha256(pathlib.Path(a.archive).read_bytes()).hexdigest(),samples=provenance),ensure_ascii=False,indent=2),'utf-8')
print(f'Built {len(groups)} confirmation groups / {len(provenance)} putdown samples')
