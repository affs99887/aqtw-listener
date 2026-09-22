"""Create a trimmed SoundRadar reference archive from our provenance manifest."""
import json,pathlib,sys,wave,zipfile,io,copy
manifest=pathlib.Path(sys.argv[1]); m=json.loads(manifest.read_text(encoding='utf-8'));root=manifest.parent
with zipfile.ZipFile(sys.argv[2],'w',zipfile.ZIP_DEFLATED) as z:
 ids=[]
 for group in m['library']['groups']:
  if group['action']!='pickup':continue
  ids.append(group['id']);meta=dict(id=group['id'],name=group['name'],threshold=.75,samples=[])
  for n,s in enumerate(x for x in m['samples'] if x['groupId']==group['id']):
   with wave.open(str(root/s['file'])) as w:
    rate=w.getframerate();ch=w.getnchannels();bits=w.getsampwidth();w.setpos(int(s['startSeconds']*rate));raw=w.readframes(int((s['endSeconds']-s['startSeconds'])*rate))
   out=io.BytesIO()
   with wave.open(out,'wb') as w:w.setparams((ch,bits,rate,0,'NONE','not compressed'));w.writeframes(raw)
   sf=f'samples/{n}.wav';z.writestr(f'items/{group["id"]}/{sf}',out.getvalue())
   meta['samples'].append(dict(file=sf,storedSampleRate=rate,storedChannels=ch,storedBitsPerSample=bits*8,frames=len(raw)//ch//bits))
  z.writestr(f'items/{group["id"]}/meta.json',json.dumps(meta,ensure_ascii=False))
 z.writestr('manifest.json',json.dumps(dict(schema=1,name='Aqtw Listener pickup references',items=ids)))
