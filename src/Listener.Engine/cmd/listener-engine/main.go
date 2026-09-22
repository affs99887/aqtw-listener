// Local protocol adapter for the user-selected SoundRadar DSP.
// All files under internal/ are preserved from the upstream revision in UPSTREAM-REVISION.
package main

import (
 "bufio"
 "encoding/base64"
 "encoding/binary"
 "encoding/json"
 "fmt"
 "math"
 "os"
 "strconv"
 "github.com/znz/soundradar/internal/dsp"
 "github.com/znz/soundradar/internal/index"
)
type request struct { ID int64 `json:"id"`; PCM string `json:"pcm"` }
type response struct { ID int64 `json:"id"`; Scores map[string]float64 `json:"scores"`; Error string `json:"error,omitempty"` }
func main() {
 if err:=run();err!=nil {fmt.Fprintln(os.Stderr,err);os.Exit(1)}
}
func run() error {
 if len(os.Args)<3{return fmt.Errorf("build library.srz index.bin | serve index.bin")}
 if os.Args[1]=="build" {
  if len(os.Args)<4{return fmt.Errorf("build requires output")}
  p:=dsp.DefaultParams()
  noise:=p.EffectiveNoise();noise.HighPassHz=240;p=p.WithNoise(noise)
  if len(os.Args)==5{hz,err:=strconv.ParseFloat(os.Args[4],64);if err!=nil{return err};n:=p.EffectiveNoise();n.HighPassHz=hz;p=p.WithNoise(n)}
  ix,err:=index.BuildWithParams(os.Args[2],p);if err!=nil{return err};return ix.Save(os.Args[3])
 }
 if os.Args[1]!="serve"{return fmt.Errorf("unknown command")}
 ix,err:=index.Load(os.Args[2]);if err!=nil{return err};p:=ix.Params()
 if p.SampleRate!=48000||p.Fingerprint()!=ix.Fingerprint(){return fmt.Errorf("incompatible engine index")}
 scanner:=bufio.NewScanner(os.Stdin);scanner.Buffer(make([]byte,65536),2*1024*1024)
 encoder:=json.NewEncoder(os.Stdout)
 for scanner.Scan(){
  var req request
  if err:=json.Unmarshal(scanner.Bytes(),&req);err!=nil{return err}
  result:=response{ID:req.ID,Scores:map[string]float64{}}
  raw,err:=base64.StdEncoding.DecodeString(req.PCM)
  if err!=nil||len(raw)%4!=0||len(raw)>48000*5*4 {result.Error="invalid PCM";encoder.Encode(result);continue}
  pcm:=make([]float32,len(raw)/4)
  for i:=range pcm{v:=math.Float32frombits(binary.LittleEndian.Uint32(raw[i*4:]));if math.IsNaN(float64(v))||math.IsInf(float64(v),0){v=0};pcm[i]=v}
  an,err:=dsp.NewAnalyzer(p);if err!=nil{return err};an.Push(pcm)
  // Silence gating is done by the C# click controller at -82 dBFS. The original
  // continuous-listening -60 dBFS gate loses quiet pickup events in this workflow.
  for end:=p.WindowFrames-1;end<an.FrameCount();end++{
   for _,s:=range ix.Search(an.WindowAt(end),0){if s.Score.Float()>result.Scores[s.ID]{result.Scores[s.ID]=s.Score.Float()}}
  }
  if err:=encoder.Encode(result);err!=nil{return err}
 }
 return scanner.Err()
}
