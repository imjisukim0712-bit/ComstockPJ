# -*- coding: utf-8 -*-
"""컴스톡 눈알 기증 밈. 표정·시선·입·붕대·관절을 별도 애니메이션한다."""
from pathlib import Path
import math, functools, random, json, argparse, subprocess, time
from PIL import Image, ImageDraw, ImageFont

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
FFMPEG=ROOT/'dev/pv/_vendor/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
W,H,FPS,DUR=1080,1920,30,23
REVISION='_v2'
K=2
INK='#15232c'; CREAM='#f9f1df'; LIME='#dafa76'; TEAL='#68d7cd'; CORAL='#ff997c'
COPY={
 'KO':{'tag':'컴스톡 · 눈알 기증 편','title':'눈 기증 받는 날','news':['얘들아! 나 드디어','눈 기증 받는대!'],'who':['근데…','누가 기증한 거야?'],'friends':['글쎄~','수술 잘 받고 와.'],'later':'잠시 후','see':['보인다!','얘들아, 나 보여!'],'wait':'…얘들아?','cry':'얘들아아아!!!','cta':['좀비보다 먼저','다운로드하세요.'],'button':'지금 itch.io에서','fine':'컴스톡 홍보 애니메이션'},
 'EN':{'tag':'COMSTOCK · EYE DONATION','title':'NEW EYES. NEW ME.','news':['GUYS! I’M GETTING','NEW EYES!'],'who':['WAIT…','WHO’S THE DONOR?'],'friends':['WHO KNOWS?','GOOD LUCK!'],'later':'A LITTLE LATER','see':['I CAN SEE!','GUYS, I CAN SEE!'],'wait':'…GUYS?','cry':'GUUUUYS!!!','cta':['Download before','the zombies do.'],'button':'GET IT ON itch.io','fine':'A COMSTOCK ANIMATED SHORT'}
}
TIMELINE=[(0,3.35,'기증 소식'),(3.35,5.6,'기증자 질문'),(5.6,8,'좀비의 시선 회피'),(8,8.8,'시간 경과'),(8.8,12,'붕대 공개'),(12,15,'외눈 좀비 반전'),(15,18.7,'컴스톡의 반응'),(18.7,23,'다운로드 엔딩')]
TEXT_BOUNDS=[]
def clamp(x):return max(0,min(1,x))
def smooth(x):x=clamp(x);return x*x*(3-2*x)
def mix(a,b,t):return a+(b-a)*t

class Pen:
 def __init__(self,im):self.d=ImageDraw.Draw(im)
 def box(self,b):return tuple(round(v*K) for v in b)
 def pts(self,p):return [(round(x*K),round(y*K)) for x,y in p]
 def rect(self,b,fill,outline=None,width=1,r=0):
  if r:self.d.rounded_rectangle(self.box(b),round(r*K),fill,outline,round(width*K))
  else:self.d.rectangle(self.box(b),fill,outline,round(width*K))
 def ellipse(self,b,fill,outline=None,width=1):self.d.ellipse(self.box(b),fill,outline,round(width*K))
 def line(self,p,fill,width=1):self.d.line(self.pts(p),fill,round(width*K),joint='curve')
 def poly(self,p,fill,outline=None,width=1):
  q=self.pts(p)
  if fill:self.d.polygon(q,fill)
  if outline:self.d.line(q+[q[0]],outline,round(width*K),joint='curve')
 def arc(self,b,start,end,fill,width=1):self.d.arc(self.box(b),start,end,fill,round(width*K))

@functools.lru_cache(None)
def font(size,lang='EN',heavy=True):
 name='malgunbd.ttf' if lang=='KO' else ('arialbd.ttf' if not heavy else 'impact.ttf')
 return ImageFont.truetype('C:/Windows/Fonts/'+name,max(4,round(size*K)))

def txt(im,s,x,y,size=30,color=CREAM,lang='EN',width=450,stroke=0,heavy=True,check=False):
 d=ImageDraw.Draw(im)
 while d.textbbox((0,0),s,font=font(size,lang,heavy))[2]>width*K and size>9:size-=.5
 f=font(size,lang,heavy)
 d.text((x*K,y*K),s,font=f,anchor='mm',fill=color,stroke_width=round(stroke*K),stroke_fill=INK)
 if check:TEXT_BOUNDS.append({'text':s,'lang':lang,'bounds':d.textbbox((x*K,y*K),s,font=f,anchor='mm',stroke_width=round(stroke*K))})

@functools.lru_cache(None)
def source(kind):
 atlas=Image.open(HERE/'assets/heads_atlas.png').convert('RGBA')
 if kind=='robot':return atlas.crop((22,173,744,877))
 if kind=='zombie':return atlas.crop((767,110,1527,908))
 p=Image.open(ROOT/'Assets/Resources/UI/title_logo.png').convert('RGBA');return p.crop(p.getbbox())

def paste(im,src,x,y,w=None,h=None,angle=0):
 if h is None:h=w*src.height/src.width
 if w is None:w=h*src.width/src.height
 sp=src.resize((max(1,round(w*K)),max(1,round(h*K))),Image.Resampling.LANCZOS)
 if angle:sp=sp.rotate(angle,Image.Resampling.BICUBIC,expand=True)
 im.paste(sp,(round(x*K-sp.width/2),round(y*K-sp.height/2)),sp)

@functools.lru_cache(None)
def background(kind='clinic'):
 im=Image.new('RGB',(W,H));p=Pen(im)
 a,b=((32,64,70),(14,32,44)) if kind!='cta' else ((29,56,67),(10,24,37))
 for y in range(960):p.line([(0,y),(540,y)],tuple(round(mix(x,z,y/960)) for x,z in zip(a,b)))
 # 큰 벽면, 창문, 조명은 캐릭터 뒤쪽에만 배치한다.
 p.rect([22,178,518,741],'#29434b',INK,3,r=28)
 p.rect([34,190,506,733],'#3c5b60',r=20)
 for x in [160,380]:p.line([(x,198),(x,724)],'#46666a',2)
 p.rect([62,264,260,477],'#213e47',INK,3,r=18)
 p.rect([71,273,251,468],'#274a53',r=12)
 rng=random.Random(125)
 for i in range(10):
  x=75+i*18; ht=rng.randint(20,80)
  p.rect([x,459-ht,x+15,468],'#203a46')
 p.line([(161,274),(161,468)],'#6d8b88',4)
 p.line([(72,365),(250,365)],'#6d8b88',4)
 # 읽을 필요 없는 시력표와 녹색 진료 표식.
 p.rect([356,269,470,421],'#29474d',r=4)
 p.rect([349,261,463,413],CREAM,INK,2,r=4)
 p.line([(397,281),(414,281),(414,297),(398,297),(398,312),(415,312)],INK,5)
 for row in range(3):
  for col in range(row+3):
   x=377+col*(22-row*3); y=336+row*22; rr=5-row
   p.ellipse([x-rr,y-rr,x+rr,y+rr],'#809186')
 p.rect([378,433,441,478],TEAL,INK,3,r=8)
 p.line([(398,455),(421,455)],INK,7);p.line([(409,444),(409,466)],INK,7)
 p.line([(271,179),(271,225)],INK,5)
 p.poly([(239,218),(301,218),(321,243),(219,243)],'#adc1ae',INK,3)
 p.poly([(224,246),(316,246),(444,694),(97,694)],'#466368')
 p.ellipse([105,632,445,738],'#668179')
 p.rect([0,721,540,960],'#1c333e')
 p.line([(0,722),(540,722)],'#789087',3)
 for x in range(-300,900,140):p.line([(270+(x-270)*.43,723),(x,960)],'#2c4650',2)
 for y in [771,849,945]:p.line([(0,y),(540,y)],'#2c4650',2)
 if kind=='cta':
  overlay=Image.new('RGBA',(W,H),(11,24,34,195));im=Image.alpha_composite(im.convert('RGBA'),overlay).convert('RGB')
 return im

def limb(p,pts,color,width=15,joint=False):
 p.line(pts,INK,width+5);p.line(pts,color,width)
 if joint:
  for x,y in pts[1:-1]:p.ellipse([x-9,y-9,x+9,y+9],'#a3b4b4',INK,3)

def eye(p,x,y,r,iris=None,look=0,blink=1,sleepy=False):
 if blink<.12:
  p.line([(x-r,y),(x+r,y+2)],INK,5);return
 ry=r*blink*(.65 if sleepy else 1)
 p.ellipse([x-r,y-ry,x+r,y+ry],INK)
 if iris:
  rr=r*.73; xx=x+look*r*.2
  iris_ry=rr*blink*(.65 if sleepy else 1)
  p.ellipse([xx-rr,y-iris_ry,xx+rr,y+iris_ry],iris)
  p.ellipse([xx-rr*.53,y-iris_ry*.57,xx+rr*.53,y+iris_ry*.57],INK)
 p.ellipse([x-r*.36+look*r*.2,y-r*.48*blink,x+r*.05+look*r*.2,y-r*.05*blink],CREAM)
 if sleepy:p.line([(x-r-3,y-ry),(x+r+4,y-ry+2)],INK,5)

def hand(p,x,y,color=CREAM,thumb=False):
 if thumb:
  p.poly([(x-6,y+12),(x-17,y-2),(x-17,y-17),(x-12,y-24),(x-6,y-22),(x-4,y-7),(x+9,y-7),(x+13,y-1),(x+10,y+12),(x+5,y+15)],color,INK,3)
  for dy in [-1,4,9]:p.line([(x+1,y+dy),(x+10,y+dy)],INK,1)
 else:p.ellipse([x-11,y-10,x+11,y+10],color,INK,3)

@functools.lru_cache(maxsize=1800)
def puppet(kind='robot',state='normal',phase=0,variant=0,mouth=0,look=0,blink=1,band=1):
 im=Image.new('RGBA',(720,840));p=Pen(im);tt=phase/30*2*math.pi
 bob=math.sin(tt)*2
 if kind=='robot':
  # 컴스톡은 머리 자체가 몸통인 원통형 로봇이다.
  for side in [-1,1]:
   fx=180+side*(42+(8 if state=='cry' else 0));fy=368+max(0,math.sin(tt+side))*3
   limb(p,[(180+side*39,275),(180+side*47,324),(fx,fy-12)],'#9eacad',17,True)
   p.poly([(fx-16,fy-20),(fx+13,fy-20),(fx+17,fy+2),(fx+25,fy+6),(fx+24,fy+17),(fx-20,fy+17)],'#c2966b',INK,4)
   p.line([(fx-15,fy+9),(fx+20,fy+9)],'#8b664b',3)
  hands=[]
  for side in [-1,1]:
   if state=='cry':hx,hy=180+side*144,156+math.sin(tt*2)*10
   elif state=='see':hx,hy=180+side*132,219+math.sin(tt)*7
   elif state=='ask':hx,hy=180+side*137,244+side*17
   else:hx,hy=180+side*128,275+math.sin(tt+side)*5
   limb(p,[(180+side*108,182),(180+side*132,229),(hx,hy)],'#a9b5b5',13,True);hands.append((hx,hy))
  paste(im,source('robot'),180,164+bob,w=240,h=252)
  for hx,hy in hands:hand(p,hx,hy)
  eye_y=180+bob
  if state!='bandage' or band<1:
   if state in ['see','cry','asknew'] or (state=='bandage' and variant==1):
    eye(p,139,eye_y,24,TEAL,look,blink)
    eye(p,219,eye_y+2,22,CORAL,look,blink,sleepy=False)
   else:
    eye(p,141,eye_y,14,None,look,blink);eye(p,219,eye_y,14,None,look,blink)
  if mouth:
   ry=10+mouth*6 if state!='cry' else 26+mouth*4
   p.ellipse([164,231+bob-ry/2,199,231+bob+ry],INK)
   p.ellipse([173,233+bob+ry*.42,194,233+bob+ry*.8],'#e9978b')
  elif state=='asknew':p.ellipse([175,229+bob,185,243+bob],INK)
  else:p.arc([164,222+bob,199,246+bob],8,170,INK,5)
  if state=='cry':
   for x in [130,230]:
    p.poly([(x-5,203),(x+7,203),(x+12,263+math.sin(tt*2)*5),(x-9,271+math.sin(tt*2)*5)],TEAL)
    p.line([(x,209),(x+3,263)],'#c6fff0',3)
  if band>0 and state=='bandage':
   off=(1-band)*400
   pts=[(76+off,147+bob),(282+off,153+bob),(281+off,210+bob),(78+off,204+bob)]
   p.poly(pts,'#f2ecd9',INK,3)
   for dy in [11,24,39,50]:p.line([(80+off,148+bob+dy),(278+off,154+bob+dy)],'#c1bdac',1)
   p.poly([(279+off,171+bob),(308+off,171+bob),(313+off,244+bob),(292+off,228+bob)],'#e4ddc8',INK,2)
 else:
  shirt='#696077' if variant==0 else '#576f7b';skin='#bbc2a1'
  for side in [-1,1]:
   fx=180+side*43+math.sin(tt)*2
   limb(p,[(180+side*30,292),(180+side*35,334),(fx,370)],skin,18)
   p.ellipse([fx-22,363,fx+17,379],skin,INK,3)
   for dx in [-10,0,10]:p.line([(fx+dx,370),(fx+dx+2,378)],INK,2)
  p.poly([(120,215),(239,215),(247,292),(232,309),(218,299),(202,313),(184,300),(165,310),(149,297),(116,299)],shirt,INK,4)
  p.poly([(137,289),(225,289),(224,323),(187,323),(182,309),(168,324),(140,323)],'#98744e',INK,3)
  p.line([(180,300),(184,323)],INK,2)
  hands=[]
  for side in [-1,1]:
   hx=180+side*106;hy=289+math.sin(tt+side)*5
   if state=='cyclops':hy=219+math.sin(tt+side)*3
   limb(p,[(180+side*61,238),(180+side*93,264),(hx,hy)],skin,16)
   hands.append((hx,hy))
  paste(im,source('zombie'),180,131+bob,w=224,h=236)
  for hx,hy in hands:hand(p,hx,hy,skin,state=='cyclops')
  iris=TEAL if variant==0 else CORAL
  if state=='cyclops':eye(p,180,133+bob,31,iris,look,blink)
  else:
   eye(p,139,143+bob,26,iris,look,blink)
   eye(p,215,151+bob,15,iris,look,blink,sleepy=True)
  p.poly([(152,184+bob),(179,179+bob),(210,191+bob),(215,212+bob+mouth*4),(168,216+bob+mouth*4),(151,205+bob)],INK)
  p.rect([158,184+bob,169,200+bob],CREAM,r=2);p.rect([199,201+bob,209,213+bob],CREAM,r=2)
  p.line([(151,238),(181,246),(214,239)],TEAL if variant==0 else CORAL,7)
 return im

def actor(im,x,y,s=1,kind='robot',state='normal',t=0,variant=0,talk=False,look=0,band=1,lean=0):
 phase=int(t*12)%30;mouth=(1+int(t*12)%2) if talk and int(t*10)%4!=0 else 0
 blink=0 if (int(t*30)+variant*43)%113 in [0,1,2] and state!='cry' else 1
 if band>0 and band<1:band=round(band*12)/12
 spr=puppet(kind,state,phase,variant,mouth,look,blink,band)
 p=Pen(im);p.ellipse([x-90*s,y-7*s,x+90*s,y+15*s],'#142d36')
 paste(im,spr,x,y-180*s,w=360*s,h=420*s,angle=lean)

def heading(im,lang,title=None):
 c=COPY[lang];p=Pen(im)
 p.rect([99,93,441,121],INK,r=14)
 txt(im,c['tag'],270,107,12,TEAL,lang,width=320)
 if title:txt(im,title,270,163,31,CREAM,lang,width=450,stroke=1)

def caption(im,lines,lang,check=False,big=False,color=CREAM):
 p=Pen(im);yy=777 if len(lines)>1 else 795
 p.rect([35,743,505,845],'#132934',r=20)
 for i,line in enumerate(lines):txt(im,line,270,yy+i*39,30 if not big else 38,color,lang,width=438,check=check)

def sparkle(im,x,y,t,color=CREAM,size=14):
 p=Pen(im);s=size*(.65+.35*math.sin(t*6))
 p.poly([(x,y-s),(x+3,y-3),(x+s,y),(x+3,y+3),(x,y+s),(x-3,y+3),(x-s,y),(x-3,y-3)],color)

def draw_frame(t,lang='KO',check=False):
 c=COPY[lang];im=background('cta' if t>=18.7 else 'clinic').copy();p=Pen(im)
 heading(im,lang,c['title'] if t<8 else None)
 if t<3.35:
  s=1.20+.035*smooth(t/3.35);actor(im,270,711,s,state='bandage',t=t,talk=t>.55)
  caption(im,c['news'],lang,check)
  if t>1:sparkle(im,423,416,t,TEAL,15)
 elif t<5.6:
  actor(im,270,720,1.25,state='bandage',t=t,talk=t<5.4,lean=-3*smooth((t-3.35)/.4))
  txt(im,'?',439,334,69,LIME,width=70)
  caption(im,c['who'],lang,check)
 elif t<8:
  actor(im,148,718,.86,'zombie','normal',t,0,talk=t<6.6,look=-1,lean=2)
  actor(im,389,704,.98,'zombie','normal',t+.3,1,talk=t>=6.6,look=1,lean=-2)
  caption(im,c['friends'],lang,check)
  txt(im,'…',260,345,55,CREAM,width=70)
 elif t<8.8:
  im=background('cta').copy();heading(im,lang)
  r=smooth((t-8)/.25)
  p=Pen(im);p.ellipse([229,368,311,450],None,TEAL,3)
  p.line([(270,409),(270+24*math.sin(t*7),409-24*math.cos(t*7))],CREAM,4)
  txt(im,c['later'],270,523,38,CREAM,lang,width=450,check=check)
 elif t<12:
  band=1-smooth((t-8.83)/.56)
  # 0.56초 동안 붕대가 오른쪽으로 빠져나간 뒤 새 눈을 공개한다.
  if band>0:actor(im,270,718,1.29,state='bandage',t=t,band=band,variant=1)
  else:actor(im,270,718,1.29,state='see',t=t,talk=t>9.3)
  if band<.7:
   for x,y in [(96,329),(443,363),(85,552),(453,574)]:sparkle(im,x,y,t+(x/100),LIME,16)
  if t>9.2:caption(im,c['see'],lang,check)
 elif t<15:
  q=smooth((t-12)/.22);s=1+.05*q
  # 연한 빛과 조용한 정면 투샷으로 외눈 반전을 충분히 읽게 한다.
  actor(im,144,720,.91*s,'zombie','cyclops',t,0,look=0,lean=1.5*math.sin(t*2))
  actor(im,391,711,1.0*s,'zombie','cyclops',t+.4,1,look=0,lean=-1.5*math.sin(t*2))
  if t>12.6:
   sparkle(im,92,371,t,TEAL,10);sparkle(im,437,311,t,CORAL,10)
  txt(im,'…',270,799,64,CREAM,width=180,check=check)
 elif t<16.8:
  actor(im,270,739,1.40,state='asknew',t=t,talk=15.2<t<15.8)
  caption(im,[c['wait']],lang,check,True)
 else:
  if t<18.7:
   q=smooth((t-16.8)/.30)
   actor(im,270+math.sin(t*75)*3,766,1.48+.06*q,state='cry',t=t,talk=True)
   caption(im,[c['cry']],lang,check,True,LIME)
   for i in range(14):
    a=i/14*math.pi*2;x=270+math.cos(a)*250;y=460+math.sin(a)*285
    p.line([(x,y),(270+math.cos(a)*215,460+math.sin(a)*250)],TEAL,2)
  else:
   q=smooth((t-18.7)/.28)
   paste(im,source('logo'),270,271,w=442*(.94+.06*q))
   txt(im,'COMSTOCK',270,365,20,TEAL,width=400,heavy=False)
   for i,line in enumerate(c['cta']):txt(im,line,270,467+i*50,35,CREAM,lang,width=450,check=check,heavy=(lang=='KO'))
   p.rect([132,576,408,626],LIME,r=14)
   txt(im,c['button'],270,601,21,INK,lang,width=250,check=check,heavy=False)
   txt(im,'pyramid-studio.itch.io/comstock',270,668,21,CREAM,width=472,heavy=False,check=check)
   actor(im,269,829,.41,state='see',t=t)
   actor(im,116,834,.32,'zombie','cyclops',t,0)
   actor(im,426,834,.34,'zombie','cyclops',t+.2,1)
   txt(im,c['fine'],270,866,11,'#86a19e',lang,width=450,check=check,heavy=False)
 # 검수 편의를 위해 각 언어 최종 글자 경계를 수집한다.
 return im

SAMPLES=[.9,3.9,6.5,8.4,9.0,10.4,12.65,14.2,15.6,17.5,20.1,22.5]
def previews():
 for lang in COPY:
  sheet=Image.new('RGB',(270*4,480*3),'#0b1822')
  for i,t in enumerate(SAMPLES):
   im=draw_frame(t,lang,True);im.save(HERE/f'frame_{lang}_{i:02d}.jpg',quality=91)
   tile=im.resize((270,480),Image.Resampling.LANCZOS);d=ImageDraw.Draw(tile);d.rectangle((0,0,74,23),fill=INK);d.text((8,4),f'{t:.2f}s',fill='white')
   sheet.paste(tile,((i%4)*270,(i//4)*480))
  sheet.save(HERE/f'contact_{lang}.jpg',quality=93)
 (HERE/'text_bounds.json').write_text(json.dumps(TEXT_BOUNDS,ensure_ascii=False,indent=2),encoding='utf-8')
 print('언어별 장면 미리보기 생성 완료',flush=True)

def render(lang):
 out=HERE/f'Comstock_EyeDonation_{lang}_1080x1920{REVISION}.mp4'
 cmd=[str(FFMPEG),'-y','-hide_banner','-loglevel','error','-f','rawvideo','-vcodec','rawvideo','-pix_fmt','rgb24','-s',f'{W}x{H}','-r',str(FPS),'-i','-','-i',str(HERE/f'audio_{lang}.wav'),'-map','0:v:0','-map','1:a:0','-c:v','libx264','-preset','fast','-crf','18','-pix_fmt','yuv420p','-af','alimiter=limit=0.63:attack=5:release=50:level=false:latency=true','-c:a','aac','-b:a','192k','-ar','48000','-t',str(DUR),'-movflags','+faststart',str(out)]
 p=subprocess.Popen(cmd,stdin=subprocess.PIPE);begin=time.time()
 try:
  for n in range(FPS*DUR):
   p.stdin.write(draw_frame(n/FPS,lang).tobytes())
   if n%150==0:print(f'{lang}: {n}/{FPS*DUR} 프레임 ({time.time()-begin:.1f}초)',flush=True)
 finally:p.stdin.close()
 if p.wait()!=0:raise RuntimeError('영상 인코딩 실패')
 print(f'{lang} 완료: {out.stat().st_size:,}바이트',flush=True)

if __name__=='__main__':
 ap=argparse.ArgumentParser();ap.add_argument('--preview',action='store_true');ap.add_argument('--lang',choices=['KO','EN','both'],default='both');args=ap.parse_args()
 if args.preview:previews()
 else:
  for lang in (['KO','EN'] if args.lang=='both' else [args.lang]):render(lang)
