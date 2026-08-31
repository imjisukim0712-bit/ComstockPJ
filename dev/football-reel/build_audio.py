# -*- coding: utf-8 -*-
"""외부 음원 없이 만드는 축구 휘슬, 비트, 드리블, 관중, 로봇 SIUUU."""
from pathlib import Path
import wave, json, subprocess
import numpy as np

HERE=Path(__file__).resolve().parent
SR=48000
DUR=22
N=SR*DUR
rng=np.random.default_rng(707)
music=np.zeros((N,2),np.float64)
fx=np.zeros((N,2),np.float64)
CUES=[]

def add(buf,x,start,gain=1,pan=0,label=None):
    at=round(start*SR);end=min(N,at+len(x))
    if end<=at:return
    src=x[:end-at]*gain
    buf[at:end,0]+=src*np.sqrt((1-pan)/2)
    buf[at:end,1]+=src*np.sqrt((1+pan)/2)
    if label:CUES.append({'time':start,'duration':len(x)/SR,'name':label})

def tone(freq,dur,decay=7):
    t=np.arange(round(SR*dur))/SR
    return np.sin(2*np.pi*freq*t)*np.exp(-decay*t)*np.minimum(1,t*150)

def kick(dur=.24):
    t=np.arange(round(SR*dur))/SR
    freq=48+110*np.exp(-t*35)
    ph=2*np.pi*np.cumsum(freq)/SR
    return np.sin(ph)*np.exp(-t*17)+rng.normal(0,.11,len(t))*np.exp(-t*140)

def noise(dur,decay=10):
    t=np.arange(round(SR*dur))/SR
    x=rng.normal(0,1,len(t))
    return (x-np.roll(x,1))*.4*np.exp(-t*decay)

def whoosh(dur=.30):
    t=np.arange(round(SR*dur))/SR
    x=rng.normal(0,.5,len(t))
    x=np.convolve(x,np.ones(9)/9,'same')
    return x*np.sin(np.pi*t/dur)**1.5

def whistle(dur=.40):
    t=np.arange(round(SR*dur))/SR
    f=2400+180*np.sin(2*np.pi*24*t)
    env=np.minimum(1,t*60)*np.minimum(1,(dur-t)*18)
    return (np.sin(2*np.pi*np.cumsum(f)/SR)+.3*np.sin(2*np.pi*3200*t))*env

# 150 BPM 오리지널 전자 비트. 축구 액션의 접촉 시점은 별도 큐로 고정한다.
beat=.4
notes=[110,110,130.81,98,110,164.81,146.83,98]
for j,start in enumerate(np.arange(0,DUR,beat)):
    gain=.12 if start<17.6 else .075
    add(music,kick(),start,gain)
    if j%2==1:add(music,noise(.18,22),start,.055)
    if j%2==0:
        freq=notes[(j//2)%len(notes)]
        tt=np.arange(round(SR*.28))/SR
        bass=(np.sin(2*np.pi*freq*tt)+.21*np.sin(2*np.pi*freq*2*tt))*np.exp(-tt*10)
        add(music,bass,start+.015,.12 if start<17.6 else .065)
    for k in range(2):add(music,noise(.055,90),start+k*.2,.037,pan=(-.4 if k else .4))
    if j%4==0:
        for freq in [440,523.25,659.25]:add(music,tone(freq,.19,14),start+.2,.022)

add(fx,whistle(),.12,.12,label='킥오프 휘슬')
add(fx,kick(),.58,.15,label='좀비 머리 착지')
for start in np.arange(2.62,8.22,.39):
    add(fx,kick(.14),float(start),.14,pan=.15,label='드리블 접촉')
for start in [3.4,5.25,7.15]:
    add(fx,whoosh(.25),start-.10,.7,pan=.3,label='수비 돌파')
    add(fx,noise(.16,15),start+.2,.08,pan=-.3,label='좀비 넘어짐')
    add(fx,tone(610,.17,12),start+.22,.06)
add(fx,whoosh(.34),8.83,.5,label='슈팅 준비')
add(fx,kick(.35),9.18,.48,label='슈팅 임팩트')
add(fx,whoosh(.67),9.21,.85,pan=.25,label='머리 공 비행')
add(fx,noise(.24,18),9.90,.11,label='골망 충돌')
for j,f in enumerate([523.25,659.25,783.99,1046.5]):add(fx,tone(f,.48,7),10.02+j*.065,.075,label='골 득점 아르페지오')
add(fx,whistle(.26),10.18,.07,label='골 인정 휘슬')
add(fx,whoosh(.8),12.68,.65,label='세레머니 점프')
add(fx,kick(.65),13.65,.53,label='SIUUU 착지')

# 관중 환호: 목소리를 모사하지 않은 다중 음정과 부드러운 군중 잡음.
def crowd(dur):
    t=np.arange(round(SR*dur))/SR;x=np.zeros(len(t))
    for j in range(24):
        f=rng.uniform(180,550)
        x+=np.sin(2*np.pi*f*t+2*np.sin(t*rng.uniform(5,11))+rng.uniform(0,6.3))
    x/=24
    n=rng.normal(0,.20,len(t)); n=np.convolve(n,np.ones(12)/12,'same')
    return (x+n)*np.minimum(1,t*4)*np.minimum(1,(dur-t)*1.8)
add(fx,crowd(2.0),9.92,.28,pan=-.15,label='득점 관중 환호')

# /s/ + /i/→/u/ 포먼트 합성. 특정 인물 녹음이나 목소리 복제 없음.
def siuuu(dur=2.45,shift=1.):
    t=np.arange(round(SR*dur))/SR
    x=np.zeros(len(t));vt=np.maximum(0,t-.15)
    f0=(175-28*np.minimum(1,vt/dur)+3.5*np.sin(2*np.pi*5.8*t))*shift
    ph=2*np.pi*np.cumsum(f0)/SR
    morph=np.clip((t-.33)/.44,0,1)
    formants=[(290,340,85),(2250,860,130),(3020,2240,175)]
    for j in range(1,45):
        freq=f0*j
        amp=np.full(len(t),.008)
        for a,b,bw in formants:
            center=a+(b-a)*morph
            amp+=np.exp(-.5*((freq-center)/bw)**2)
        x+=(amp/(j**.58))*np.sin(ph*j+.045*np.sin(t*34))
    env=np.minimum(1,vt*25)*np.minimum(1,(dur-t)*4.5)
    x*=env
    s=rng.normal(0,.20,len(t));s=s-np.roll(s,1)
    s*=np.clip((.24-t)*8,0,1)*np.minimum(1,t*100)
    x+=s
    return x/(np.max(np.abs(x))+1e-8)

voice=siuuu()
add(fx,voice,13.66,.56,label='로봇 SIUUU 합성 음성')
add(fx,siuuu(2.32,.88),13.74,.20,pan=-.6,label='SIUUU 군중 코러스')
add(fx,siuuu(2.38,1.08),13.80,.17,pan=.6)
add(fx,voice,13.92,.09,pan=.4,label='경기장 잔향')
add(fx,crowd(2.5),14.10,.18,pan=.25)
add(fx,whoosh(.30),17.46,.60,label='엔딩 전환')
for j,f in enumerate([523.25,659.25,783.99]):add(fx,tone(f,.65,5),17.65+j*.1,.065)

# SIUUU 대사 순간에는 음악을 낮추고 마지막에만 짧게 페이드아웃.
ts=np.arange(N)/SR
duck=np.ones(N)
duck[(ts>13.52)&(ts<16.52)]=.32
out=music*duck[:,None]+fx
fade=np.minimum(1,ts*25)*np.minimum(1,(DUR-ts)*3)
out*=fade[:,None]
peak=float(np.max(np.abs(out)))
out*=.82/max(peak,.82)
pcm=np.clip(out*32767,-32768,32767).astype('<i2')
with wave.open(str(HERE/'football_audio.wav'),'wb') as wav:
    wav.setnchannels(2);wav.setsampwidth(2);wav.setframerate(SR);wav.writeframes(pcm.tobytes())
with wave.open(str(HERE/'siuuu_preview.wav'),'wb') as wav:
    wav.setnchannels(2);wav.setsampwidth(2);wav.setframerate(SR);wav.writeframes(pcm[round(13.4*SR):round(16.6*SR)].tobytes())
(HERE/'audio_cues.json').write_text(json.dumps(CUES,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'duration':DUR,'sample_rate':SR,'channels':2,'peak':float(np.max(np.abs(out))),'cues':len(CUES)}))
