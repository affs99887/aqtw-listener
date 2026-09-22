"""Import the user-selected SoundRadar archive. Never executes upstream code.
Group aliases and action handling are adapted from internal/library/tingsheng_merge.go.
See docs/THIRD-PARTY.md. Raw recordings remain in the research directory.
"""
import argparse, hashlib, json, pathlib, zipfile, wave, io, array

p = argparse.ArgumentParser()
p.add_argument('archive'); p.add_argument('output'); p.add_argument('--revision', required=True)
a = p.parse_args()
root = pathlib.Path(a.output); root.mkdir(parents=True, exist_ok=True)
(root/'audio').mkdir(exist_ok=True); (root/'images').mkdir(exist_ok=True)
# Rarity was manually checked against every upstream displayHint thumbnail.
non_red = {'石膏像','密码机','数据线','电台','检测仪','钯金线材','除颤仪','笔记本电脑','摄影机',
           '激光指示模块','天线','红外理疗灯','工具套组','钢筋剪','吹风机','电钻','炮弹',
           '手持接收机','航天实验室','人工心脏','单兵通讯装置'}
source = 'https://github.com/blood77458/soundradar/blob/'+a.revision+'/soundradar/data/library.srz'
library = dict(schemaVersion=1, version='0.1.1-pickup-source-fix', gameVersion='来源未明确标注版本，2026-09-19/20 素材',
    validationStatus='uncalibrated', notes='SoundRadar 社区样本。阈值未以独立验证集校准；同音关系和格数沿用来源，未逐件实机复核。', items=[], groups=[])
samples=[]; provenance=[]
# Rechecked against the original starter archive at 4f8289d5: these bytes are
# tagged 拿起 there and differ from every 放下 sample. See docs/COVERAGE-REVIEW.md.
verified_pickups = {
 '267afca3': ('merged:目标定位-拿起', 'cf3d1e09c17a5df040400e1fe03e29c137ce7cf00d7e8fa0fe4ca45ee1d3c7ec'),
 'e9f62d39': ('merged:琥珀天心-拿起', '8117b13cfcd61e667f560d3b431a4c4113b5cc4be51470df4b9f1f66b17bf89c'),
}
with zipfile.ZipFile(a.archive) as z:
 for name in z.namelist():
  if not name.endswith('/meta.json'): continue
  meta=json.loads(z.read(name)); base=name.rsplit('/',1)[0]+'/'
  ids=[]
  for idx,h in enumerate(meta['displayHints']):
   item_id=meta['id']+'-'+str(idx); ids.append(item_id)
   w,ht=map(int,h['grid'].lower().replace('×','x').split('x'))
   image='images/'+item_id+'.png'; (root/image).write_bytes(z.read(base+h['icon']))
   library['items'].append(dict(id=item_id,name=h['name'],isGold=h['name'] not in non_red,
      gridWidth=w,gridHeight=ht,thumbnail=image,referenceValue=None))
  enabled = bool(meta['samples']) and not any('放下' in s.get('source', '') for s in meta['samples'])
  if meta['id'] in verified_pickups:
   expected_source, expected_hash = verified_pickups[meta['id']]
   enabled = len(meta['samples']) == 1 and all(s.get('source') == expected_source
       and hashlib.sha256(z.read(base+s['file'])).hexdigest() == expected_hash for s in meta['samples'])
  library['groups'].append(dict(id=meta['id'],name=meta['name'],action='pickup' if enabled else 'unverified',
                               itemIds=ids,threshold=0.86,templates=[]))
  for i,s in enumerate(meta['samples']):
   raw=z.read(base+s['file']); rel='audio/'+meta['id']+'-'+str(i)+'.wav'; (root/rel).write_bytes(raw)
   with wave.open(io.BytesIO(raw)) as wf:
    rate=wf.getframerate(); channels=wf.getnchannels(); pcm=array.array('h',wf.readframes(wf.getnframes()))
   duration=len(pcm)/channels/rate
   # Keep short explicit-drag references; recall clips are peak-aligned to a 0.50s segment.
   start=0.; end=duration
   if duration>0.7:
    mono=[sum(pcm[j:j+channels])/channels for j in range(0,len(pcm),channels)]
    hop=int(rate*.01); span=int(rate*.06)
    energy=[sum(v*v for v in mono[j:j+span]) for j in range(0,len(mono)-span,hop)]
    peak=max(range(len(energy)),key=energy.__getitem__)*.01+.03
    start=max(0.,peak-.17); end=min(duration,start+.5)
   # Original recording unavailable; all material in this archive shares one conservative source ID.
   record='soundradar-community-20260919-20'
   provenance.append(dict(group=meta['name'],file=rel,origin=s.get('origin'),metadataSource=s.get('source'),
       sha256=hashlib.sha256(raw).hexdigest(),start=start,end=end,enabled=enabled))
   if enabled: samples.append(dict(groupId=meta['id'],file=rel,sourceUrl=source,recordingId=record,startSeconds=start,endSeconds=end))
(root/'import.json').write_text(json.dumps(dict(library=library,samples=samples),ensure_ascii=False,indent=2),encoding='utf-8')
(root/'provenance.json').write_text(json.dumps(dict(source=source,revision=a.revision,samples=provenance),ensure_ascii=False,indent=2),encoding='utf-8')
print(f'{len(library["items"])} items, {len(library["groups"])} groups, {len(samples)} enabled samples')
