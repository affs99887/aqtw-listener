"""Deterministic engineering stress set. Derived samples are NOT independent events."""
import array, io, json, math, pathlib, random, wave, zipfile
root=pathlib.Path('.tools/research/imported'); out=pathlib.Path('.tools/research/stress'); out.mkdir(exist_ok=True)
m=json.loads((root/'import.json').read_text(encoding='utf-8')); cases=[]
def read(file,start,end):
 with wave.open(str(file)) as w:
  rate=w.getframerate(); pcm=array.array('h',w.readframes(w.getnframes()))
 return [v/32768 for v in pcm[int(start*rate):int(end*rate)]],rate
def write(file,data,rate):
 with wave.open(str(file),'wb') as w:
  w.setparams((1,2,rate,0,'NONE','not compressed')); w.writeframes(array.array('h',[int(max(-1,min(1,v))*32767) for v in data]).tobytes())
with zipfile.ZipFile(out/'reference.srz','w') as z:
 for group in m['library']['groups']:
  if group['action']!='pickup':continue
  entries=[s for s in m['samples'] if s['groupId']==group['id']]
  meta=dict(id=group['id'],name=group['name'],threshold=.75,samples=[])
  for n,s in enumerate(entries):
   pcm,rate=read(root/s['file'],s['startSeconds'],s['endSeconds'])
   tmp=out/'reference-temp.wav';write(tmp,pcm,rate)
   sf='samples/'+str(n)+'.wav';z.write(tmp,'items/'+group['id']+'/'+sf)
   meta['samples'].append(dict(file=sf,storedSampleRate=rate,storedChannels=1,storedBitsPerSample=16,frames=len(pcm)))
   rms=math.sqrt(sum(v*v for v in pcm)/len(pcm));rng=random.Random(123+n)
   for mode in ['clean','quiet20','quiet04','noise10db','noise0db','rumble']:
    data=pcm.copy()
    if mode.startswith('quiet'):data=[v*(.2 if mode=='quiet20' else .04) for v in data]
    if mode.startswith('noise'):data=[v+rng.gauss(0,rms*(.316 if mode=='noise10db' else 1)) for v in data]
    if mode=='rumble':data=[v+2*rms*math.sin(2*math.pi*90*j/rate) for j,v in enumerate(data)]
    data=[0.]*int(rate*.06)+data+[0.]*int(rate*.2)
    f=group['id']+'-'+str(n)+'-'+mode+'.wav';write(out/f,data,rate)
    item=next(x for x in m['library']['items'] if x['id']==group['itemIds'][0])
    cases.append(dict(file=f,recordingId=s['recordingId'],expectedItemId=item['id'],expectedGroup=group['id'],isNonGoldOrNoise=not item['isGold'],split='test',mode=mode))
  z.writestr('items/'+group['id']+'/meta.json',json.dumps(meta))
 z.writestr('manifest.json',json.dumps(dict(version=1,order=[g['id'] for g in m['library']['groups'] if g['action']=='pickup'])))
for i in range(20):
 rng=random.Random(i);rate=48000
 data=([0.]*48000 if i==0 else [rng.gauss(0,.015) for _ in range(rate)]) if i<10 else [math.sin(2*math.pi*(200+(i-10)*530)*j/rate)*.03 for j in range(rate)]
 f='noise-'+str(i)+'.wav';write(out/f,data,rate)
 cases.append(dict(file=f,recordingId='synthetic-'+str(i),expectedItemId=None,isNonGoldOrNoise=True,split='test',mode='negative'))
(out/'cases.json').write_text(json.dumps(dict(kind='derived-engineering-only',cases=cases),ensure_ascii=False,indent=2),encoding='utf-8')
print(str(len(cases))+' derived engineering cases; NOT an independent acceptance set')
