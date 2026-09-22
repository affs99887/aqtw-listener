import hashlib,json,pathlib,sys
root=pathlib.Path(sys.argv[1]);path=root/'library.json'
data=json.loads(path.read_text(encoding='utf-8'))
data['primaryEngine']='soundradar'
data['engineIndexSha256']=hashlib.sha256((root/'radar-index.bin').read_bytes()).hexdigest()
for group in data['groups']:group['threshold']=.75
path.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
