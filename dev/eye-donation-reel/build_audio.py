# -*- coding: utf-8 -*-
"""원본 틱톡 음원을 사용하지 않는 음악·효과음·대사 믹스."""
from pathlib import Path
import wave, json, subprocess
import numpy as np
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
FF=ROOT/'dev/pv/_vendor/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
SR,DUR=48000,23
N=SR*DUR
rng=np.random.default_rng(910)
ts=np.arange(N)/SR
CUES=[]

def add(buf,x,at,gain=1,pan=0,label=None):
 start=round(at*SR);end=min(N,start+len(x))
 if end<=start:return
 x=x[:end-start]*gain
 buf[start:end,0]+=x*np.sqrt((1-pan)/2)
 buf[start:end,1]+=x*np.sqrt((1+pan)/2)
 if label:CUES.append({'time':at,'duration':len(x)/SR,'name':label})

def bell(f,dur=.7):
 t=np.arange(round(SR*dur))/SR
 x=np.sin(2*np.pi*f*t)*np.exp(-t*4.5)
 x+=.23*np.sin(2*np.pi*f*2.004*t)*np.exp(-t*9)
 x+=.10*np.sin(2*np.pi*f*3.002*t)*np.exp(-t*15)
 return x*np.minimum(1,t*250)*np.minimum(1,(dur-t)*50)

def sweep(f1,f2,dur,decay=4):
 t=np.arange(round(SR*dur))/SR;f=f1+(f2-f1)*t/dur
 return np.sin(2*np.pi*np.cumsum(f)/SR)*np.minimum(1,t*90)*np.exp(-t*decay)*np.minimum(1,(dur-t)*35)

def noise(dur):
 t=np.arange(round(SR*dur))/SR
 x=rng.normal(0,.3,len(t));x=np.convolve(x,np.ones(7)/7,'same')
 return x*np.sin(np.pi*t/dur)**1.4

def writewav(path,arr):
 with wave.open(str(path),'wb') as f:
  f.setnchannels(2);f.setsampwidth(2);f.setframerate(SR)
  f.writeframes((np.clip(arr,-1,1)*32767).astype('<i2').tobytes())

music=np.zeros((N,2));fx=np.zeros((N,2))
# 담백한 장조 벨 화음. 반전 순간 완전히 끊어 외눈 투샷을 강조한다.
chords=[[261.626,329.628,391.995],[220,261.626,329.628],[174.614,220,261.626],[195.998,246.942,293.665]]
for j,at in enumerate(np.arange(0,12,.375)):
 chord=chords[(j//8)%4]
 add(music,bell(chord[j%3]*(2 if j%4==3 else 1),.9),at,.07,pan=.25 if j%2 else -.25)
 if j%4==0:
  add(music,bell(chord[0]/2,1.2),at,.09)
  for f in chord:add(music,bell(f,1.2),at,.018)
for j,at in enumerate(np.arange(18.7,23,.3)):
 chord=chords[(j//4)%4]
 add(music,bell(chord[j%3]*2,.6),at,.09,pan=.25 if j%2 else -.25)
 if j%2==0:add(music,sweep(110,48,.22,15),at,.13)
 for f in chord:add(music,bell(f,.7),at,.015)
# 컷 전환, 소품, 표정 큐.
add(fx,sweep(460,850,.14,11),.14,.13,label='시작 팝')
add(fx,sweep(450,240,.26,5),3.40,.10,label='질문 고개 기울이기')
add(fx,noise(.15),5.58,.17,label='좀비 투샷 전환')
for at in [8.05,8.32,8.58]:add(fx,bell(950,.1),at,.08,label='시간 경과 시계')
add(fx,noise(.60),8.81,.7,pan=.3,label='붕대 풀리는 소리')
for j,f in enumerate([523.25,659.25,783.99,1046.5]):add(fx,bell(f,.85),9.18+j*.075,.12,label='새 눈 공개 반짝임')
add(fx,sweep(900,75,.34,4),11.98,.22,label='반전 레코드 스크래치')
# 귀뚜라미와 어색한 삑 소리는 작게, 대사 없는 3초를 지탱한다.
for at in [12.75,13.15,13.7,14.1]:
 t=np.arange(round(SR*.14))/SR
 x=np.sin(2*np.pi*2850*t)*np.maximum(0,np.sin(2*np.pi*35*t))**3*np.sin(np.pi*t/.14)**2
 add(fx,x,at,.035,pan=-.45,label='어색한 정적 귀뚜라미')
add(fx,sweep(640,900,.15,8),12.35,.065,label='좀비 엄지 척')
add(fx,sweep(90,43,.27,9),15.0,.19,label='당황한 심장 박자')
add(fx,sweep(90,43,.27,9),15.52,.15)
add(fx,sweep(130,42,.65,5),16.82,.30,label='눈물 클로즈업 임팩트')
for f in [164.81,196,246.94]:add(fx,bell(f,1.7),16.88,.09,label='과장된 슬픔 화음')
add(fx,noise(.27),18.54,.36,label='엔딩 전환')
for j,f in enumerate([523.25,659.25,783.99]):add(fx,bell(f,.6),18.82+j*.1,.08,label='다운로드 엔딩')
music[(ts>=12)&(ts<18.7)]=0
lines=json.loads((HERE/'dialogue.json').read_text(encoding='utf-8'))
report={}
for lang in ['KO','EN']:
 voice=np.zeros((N,2));duck=np.ones(N);vr=[]
 for line in lines[lang]:
  inp=HERE/'voices'/f'{lang}_{line["id"]}.wav'
  raw=subprocess.check_output([str(FF),'-v','error','-i',str(inp),'-f','f32le','-ac','1','-ar',str(SR),'-'])
  x=np.frombuffer(raw,dtype='<f4').astype(float)
  inds=np.flatnonzero(abs(x)>.002)
  if len(inds):x=x[max(0,inds[0]-int(.045*SR)):min(len(x),inds[-1]+int(.08*SR))]
  slot=line['end']-line['start'];speed=max(1,len(x)/SR/(slot-.06))
  # 로봇은 밝은 음색과 약한 전자 떨림, 좀비는 낮은 음색으로 구분한다.
  pitch=1.07 if line['role']=='robot' else (.87 if line['role']=='zombie' else 1.0)
  if line['id']=='cry':pitch=.94
  ratio=speed/pitch
  filt=f'asetrate={round(SR*pitch)},aresample={SR},atempo={ratio:.5f},highpass=f=100,lowpass=f=8500'
  raw=subprocess.run([str(FF),'-v','error','-f','f32le','-ar',str(SR),'-ac','1','-i','-','-af',filt,'-f','f32le','-ar',str(SR),'-ac','1','-'],input=x.astype('<f4').tobytes(),stdout=subprocess.PIPE,check=True).stdout
  x=np.frombuffer(raw,dtype='<f4').astype(float)
  if line['id']=='cry' and len(x)<int(slot*.90*SR):
   # 비명 대사를 슬롯에 맞춰 늘린다. 특정 인물 목소리를 모사하지 않는다.
   target=int(slot*.93*SR);x=np.interp(np.linspace(0,len(x)-1,target),np.arange(len(x)),x)
  norm=max(.02,np.sqrt(np.mean(x*x)));x=x/norm*.14
  if line['role']=='robot':x*=.94+.06*np.sin(2*np.pi*38*np.arange(len(x))/SR)
  x=np.tanh(x*1.10)/1.10
  add(voice,x,line['start'],1,label=lang+' '+line['id'])
  if line['id']=='cry':add(voice,x,line['start']+.08,.13,pan=.6)
  start=int(line['start']*SR);end=min(N,start+len(x)+int(.08*SR));duck[max(0,start-int(.08*SR)):end]=.24
  vr.append({'id':line['id'],'start':line['start'],'end':line['start']+len(x)/SR,'slot_end':line['end']})
 out=music*duck[:,None]+fx+voice
 out*=np.minimum(1,ts*30)[:,None]*np.minimum(1,(DUR-ts)*4)[:,None]
 out*=min(1,.86/max(.001,np.max(abs(out))))
 rawpath=HERE/f'audio_{lang}_raw.wav';writewav(rawpath,out)
 subprocess.run([str(FF),'-y','-v','error','-i',str(rawpath),'-af','loudnorm=I=-16:TP=-1.5:LRA=8','-ar',str(SR),'-t',str(DUR),str(HERE/f'audio_{lang}.wav')],check=True)
 report[lang]=vr
 print(lang,'대사·음악·효과음 믹스 완료',flush=True)
(HERE/'audio_cues.json').write_text(json.dumps({'voices':report,'effects':CUES},ensure_ascii=False,indent=2),encoding='utf-8')
