// Build inside the upstream module to compare its actual unchanged DSP/index code.
package main
import("encoding/json";"os";"path/filepath";"time";"math";"github.com/znz/soundradar/internal/dsp";"github.com/znz/soundradar/internal/index";"github.com/znz/soundradar/internal/wav")
type Case struct { File string `json:"file"`; ExpectedGroup string `json:"expectedGroup"`; Mode string `json:"mode"` }
type Manifest struct { Cases []Case `json:"cases"` }
func main(){
 root:=os.Args[1];p:=dsp.DefaultParams();ix,err:=index.BuildWithParams(filepath.Join(root,"reference.srz"),p);if err!=nil{panic(err)}
 raw,_:=os.ReadFile(filepath.Join(root,"cases.json"));var m Manifest;json.Unmarshal(raw,&m)
 rows:=[]map[string]any{}
 for _,c:=range m.Cases{
  f,err:=os.Open(filepath.Join(root,c.File));if err!=nil{panic(err)};a,err:=wav.Read(f);f.Close();if err!=nil{panic(err)}
  pcm:=make([]float32,len(a.Samples));for j,v:=range a.Samples{pcm[j]=float32(v)}
  start:=time.Now();an,_:=dsp.NewAnalyzer(p);an.Push(pcm);scores:=map[string]float64{}
  if dsp.LevelDBFSOf(pcm)>=-60{for end:=p.WindowFrames-1;end<an.FrameCount();end++{for _,s:=range ix.Search(an.WindowAt(end),0){if s.Score.Float()>scores[s.ID]{scores[s.ID]=s.Score.Float()}}}}
  best:="";v,second:=0.,0.;for id,score:=range scores{if score>v{second=v;v=score;best=id}else{second=math.Max(second,score)}}
  if v<.75||v-second<.05{best=""}
  rows=append(rows,map[string]any{"file":c.File,"mode":c.Mode,"expectedGroup":c.ExpectedGroup,"predictedGroup":best,"hit":best!=""&&best==c.ExpectedGroup,"falsePositive":best!=""&&c.ExpectedGroup=="","milliseconds":float64(time.Since(start).Nanoseconds())/1e6,"score":v})
 }
 out,_:=json.MarshalIndent(map[string]any{"kind":"derived-engineering-only","rows":rows},"","  ");os.WriteFile(os.Args[2],out,0644)
}
