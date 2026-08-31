# -*- coding: utf-8 -*-
"""컴스톡 축구 밈: 관절 애니메이션, 카툰 합성, 1080×1920 언어별 출력."""
from pathlib import Path
import math, random, functools, subprocess, json, argparse, time
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
RES = ROOT / 'Assets/Resources'
FFMPEG = ROOT / 'dev/pv/_vendor/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
W, H, FPS, DUR = 1080, 1920, 30, 22.0
K = 2
INK = '#101e29'
CREAM = '#f6f1dd'
LIME = '#d9ff65'
RED = '#f14758'
TEAL = '#45d9c1'
COPY = {
 'KO': {'hook':'축구공이 없다고?', 'hook2':'그럼 있는 걸로.', 'run':'컴날두, 출격.',
        'dodge':['하나 제끼고.','둘 제끼고.','셋 제끼고.'], 'nutmeg':'가랑이 사이로!',
        'shot':'머리 좀 굴려볼까?', 'goal':'골이다!!!', 'cta':['좀비보다 먼저','다운로드하세요.'],
        'button':'지금 itch.io에서', 'fine':'코믹 패러디 · 실제 축구 모드 아님', 'team':'좀비 FC', 'tag':'컴스톡 축구부'},
 'EN': {'hook':'NO FOOTBALL?', 'hook2':'IMPROVISE.', 'run':'COMNALDO. LET’S GO.',
        'dodge':['ONE DOWN.','TWO DOWN.','THREE DOWN.'], 'nutmeg':'NUTMEG!',
        'shot':'USE YOUR HEAD.', 'goal':'GOOOAL!!!', 'cta':['Download before','the zombies do.'],
        'button':'GET IT ON itch.io', 'fine':'COMEDY PARODY · NOT A GAME MODE', 'team':'ZOMBIE FC', 'tag':'COMSTOCK FOOTBALL CLUB'}
}
TIMELINE = [(0,2.4,'소개'),(2.4,8.4,'좀비 세 명 돌파'),(8.4,11.7,'슈팅과 골'),(11.7,17.6,'점프 회전 SIUUU'),(17.6,22,'다운로드 엔딩')]

def clamp(x): return max(0., min(1., x))
def smooth(x): x=clamp(x); return x*x*(3-2*x)
def mix(a,b,t): return a+(b-a)*t

@functools.lru_cache(None)
def font(size, lang='EN', heavy=True):
    name = 'malgunbd.ttf' if lang=='KO' else ('impact.ttf' if heavy else 'arialbd.ttf')
    return ImageFont.truetype('C:/Windows/Fonts/'+name, max(2,int(size*K)))

class Pen:
    def __init__(self, im): self.d=ImageDraw.Draw(im)
    def pts(self,p): return [(round(x*K),round(y*K)) for x,y in p]
    def box(self,b): return tuple(round(x*K) for x in b)
    def rect(self,b,fill,outline=None,width=1,r=0):
        if r: self.d.rounded_rectangle(self.box(b),radius=int(r*K),fill=fill,outline=outline,width=max(1,int(width*K)))
        else: self.d.rectangle(self.box(b),fill=fill,outline=outline,width=max(1,int(width*K)))
    def ellipse(self,b,fill,outline=None,width=1): self.d.ellipse(self.box(b),fill=fill,outline=outline,width=max(1,int(width*K)))
    def line(self,p,fill,width=1): self.d.line(self.pts(p),fill=fill,width=max(1,int(width*K)),joint='curve')
    def poly(self,p,fill,outline=None,width=1):
        q=self.pts(p)
        if fill is not None: self.d.polygon(q,fill=fill)
        if outline: self.d.line(q+[q[0]],fill=outline,width=max(1,int(width*K)),joint='curve')

TEXT_BOUNDS=[]
def text(im,s,x,y,size=32,color=CREAM,lang='EN',width=440,stroke=0,anchor='mm',heavy=True,check=False):
    d=ImageDraw.Draw(im)
    while d.textbbox((0,0),s,font=font(size,lang,heavy))[2] > width*K and size>8: size-=.5
    f=font(size,lang,heavy)
    d.text((x*K,y*K),s,font=f,fill=color,anchor=anchor,stroke_width=int(stroke*K),stroke_fill=INK)
    if check: TEXT_BOUNDS.append({'text':s,'bounds':d.textbbox((x*K,y*K),s,font=f,anchor=anchor),'lang':lang})

@functools.lru_cache(None)
def asset(path):
    im=Image.open(path).convert('RGBA'); box=im.getbbox()
    return im.crop(box) if box else im

def paste(im,src,x,y,w=None,h=None,anchor='center',angle=0):
    if w is not None: size=(max(1,round(w*K)), max(1,round(w*K*src.height/src.width)))
    elif h is not None: size=(max(1,round(h*K*src.width/src.height)),max(1,round(h*K)))
    else: size=src.size
    spr=src.resize(size,Image.Resampling.LANCZOS)
    if angle: spr=spr.rotate(angle,resample=Image.Resampling.BICUBIC,expand=True)
    px=round(x*K-spr.width/2); py=round(y*K-(spr.height if anchor=='bottom' else spr.height/2))
    im.paste(spr,(px,py),spr)

@functools.lru_cache(None)
def stadium(kind='wide'):
    im=Image.new('RGB',(W,H)); p=Pen(im)
    for y in range(960):
        u=y/960; c=tuple(round(mix(a,b,u)) for a,b in zip((12,28,46),(37,90,91)))
        p.line([(0,y),(540,y)],c)
    # 도시와 경기장 조명.
    rng=random.Random(35)
    for x in range(-20,560,35):
        ht=rng.randint(40,115); p.rect([x,298-ht,x+27,340],'#17313f')
        for wy in range(305-ht,295,16): p.rect([x+8,wy,x+12,wy+5],'#34535b')
    for x in [50,490]:
        p.line([(x,165),(x,330)],'#5d777a',5)
        p.rect([x-30,158,x+30,178],INK,r=5)
        for j in range(5): p.rect([x-23+j*10,162,x-17+j*10,170],CREAM,r=2)
    p.poly([(25,178),(140,400),(-40,400)],'#254a54')
    p.poly([(515,178),(400,400),(580,400)],'#254a54')
    p.rect([0,302,540,405],'#243e4a')
    # 관중은 간단한 군상으로 그려 전경 캐릭터가 잘 보이게 한다.
    for row in range(4):
        for col in range(25):
            cx=col*23+(row%2)*10; cy=313+row*20
            c=rng.choice(['#65776e','#59695b','#95a078','#334e57','#466f73'])
            p.ellipse([cx-5,cy-6,cx+5,cy+4],c)
            p.line([(cx-7,cy+10),(cx,cy+3),(cx+7,cy+10)],c,5)
    p.rect([0,389,540,421],INK)
    for x in range(-30,600,190): text(im,'COMSTOCK',x+65,405,19,TEAL,width=150)
    # 원근감 있는 잔디와 터치라인.
    p.rect([0,421,540,960],'#427b61')
    for j in range(10):
        a=421+((j/10)**1.6)*560; b=421+(((j+1)/10)**1.6)*560
        if j%2==0: p.rect([0,a,540,b],'#396e59')
    p.line([(130,430),(-170,960)],'#abc7a0',3)
    p.line([(410,430),(710,960)],'#abc7a0',3)
    p.line([(0,768),(540,768)],'#aac3a0',3)
    p.ellipse([60,682,480,847],None,'#aac3a0',3)
    rng=random.Random(31)
    for i in range(120):
        x=rng.randrange(540); y=rng.randrange(440,960)
        p.line([(x,y),(x+3,y-2)],'#518368',1)
    # 화면 위아래는 릴스 UI를 위한 안전 여백.
    return im

def shadow(im,x,y,w=100,h=17): Pen(im).ellipse([x-w/2,y-h/2,x+w/2,y+h/2],'#2a5248')

@functools.lru_cache(maxsize=640)
def robot_pose(mode='run',phase=0,back=False):
    """각 관절을 실제로 이동시킨 포즈. 로봇 얼굴은 원본 스프라이트."""
    im=Image.new('RGBA',(560,600)); p=Pen(im)
    ox,oy=140,274
    def pt(x,y): return (ox+x,oy+y)
    def limb(coords,c,width):
        points=[pt(*q) for q in coords];p.line(points,INK,width+5);p.line(points,c,width)
        for x,y in points[1:-1]: p.ellipse([x-8,y-8,x+8,y+8],'#91a3a6',INK,3)
    a=phase/24*2*math.pi
    if mode=='run':
        sw=math.sin(a); feet=[(-21+sw*31,-3-max(0,sw)*25),(21-sw*31,-3-max(0,-sw)*25)]
        hands=[(-43-sw*15,-97-sw*29),(43+sw*15,-97+sw*29)]
    elif mode=='kick':
        q=phase/23; feet=[(-24,0),(mix(-52,94,smooth(q)),mix(-10,-51,smooth(q)))];hands=[(-67,-133),(65,-105)]
    elif mode=='jump':
        feet=[(-29,-28),(34,-34)]; hands=[(-28,-251),(29,-247)]
    elif mode=='siu':
        feet=[(-63,0),(63,0)];hands=[(-82,-96),(82,-96)]
    elif mode=='crouch':
        feet=[(-45,0),(45,0)]; hands=[(-56,-63),(56,-63)]
    else:
        feet=[(-23,0),(23,0)];hands=[(-57,-94),(57,-94)]
    # 다리와 축구화.
    for j,(fx,fy) in enumerate(feet):
        hx=-19 if j==0 else 19
        ky=-40+(10 if mode=='crouch' else 0)
        kx=mix(hx,fx,.53)+(-8 if j==0 else 8)
        limb([(hx,-78),(kx,ky),(fx,fy-12)],'#b8c6c7',13)
        limb([(kx,ky+4),(fx,fy-12)],CREAM,11)
        bx,by=pt(fx,fy-5)
        p.poly([(bx-10,by-8),(bx+10,by-8),(bx+21,by+1),(bx+19,by+10),(bx-14,by+10)],LIME,INK,3)
        p.line([(bx-11,by+7),(bx+17,by+7)],INK,3)
    # 달릴 때 팔이 몸통 뒤로 지나가도록 먼저 그린다.
    for j,(hx,hy) in enumerate(hands):
        sx=-36 if j==0 else 36
        limb([(sx,-146),(mix(sx,hx,.62),mix(-146,hy,.4)),(hx,hy)],'#b8c6c7',12)
        x,y=pt(hx,hy);p.ellipse([x-9,y-9,x+9,y+9],CREAM,INK,3)
    # 특정 구단 상표가 없는 빨강·초록 7번 유니폼.
    points=[pt(-37,-154),pt(37,-154),pt(40,-81),pt(28,-70),pt(-28,-70),pt(-40,-81)]
    p.poly(points,RED,INK,4)
    p.poly([pt(-37,-154),pt(-23,-149),pt(-24,-74),pt(-40,-81)],'#21695a')
    p.poly([pt(37,-154),pt(24,-149),pt(25,-73),pt(40,-81)],'#21695a')
    p.line([pt(-17,-152),pt(0,-141),pt(17,-152)],LIME,4)
    text(im,'7',ox,oy-104,39,CREAM,width=50,stroke=1)
    if back: text(im,'COMSTOCK',ox,oy-134,9,CREAM,width=64,heavy=False)
    else: text(im,'CFC',ox-18,oy-133,8,CREAM,width=28,heavy=False)
    p.rect([ox-30,oy-77,ox+30,oy-64],'#174d47',INK,3,r=4)
    # 전 장면에 같은 원본 얼굴을 사용해 캐릭터를 유지한다.
    if not back: paste(im,asset(str(RES/'Heads/ComstockMk01.png')),ox,oy-195,w=101)
    else:
        p.rect([ox-43,oy-245,ox+43,oy-149],'#c1c7c8',INK,4,r=19)
        p.ellipse([ox-44,oy-252,ox+44,oy-226],'#e1e5e5',INK,3)
        p.line([(ox-22,oy-179),(ox+22,oy-179)],'#8e999d',3)
    # 호날두 밈을 연상시키는 검은 앞머리, 실제 인물 얼굴은 사용하지 않는다.
    p.poly([pt(-37,-242),pt(-33,-255),pt(-20,-251),pt(-8,-265),pt(3,-257),pt(20,-262),pt(31,-250),pt(39,-243),pt(20,-244),pt(5,-238),pt(-12,-243)],'#202830',INK,2)
    p.line([pt(-16,-252),pt(5,-249),pt(19,-253)],'#536168',2)
    return im

def robot(im,x,y,scale=1,mode='run',phase=0,back=False,turn=1,lean=0):
    src=robot_pose(mode,phase%24,back)
    src=src.resize((max(4,int(src.width*abs(turn)*scale)),max(4,int(src.height*scale))),Image.Resampling.LANCZOS)
    if turn<0: src=src.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    if lean: src=src.rotate(lean,resample=Image.Resampling.BICUBIC,expand=False)
    # 발의 로컬 원점은 (140,274), 전체 높이는 300이다.
    im.paste(src,(round(x*K-src.width/2),round(y*K-274*K*scale)),src)

def zombie(im,x,y,size=178,t=0,flip=False,fall=0):
    seq=sorted((RES/'ZombieMove').glob('*.png'))
    spr=asset(str(seq[int(t*10)%len(seq)]))
    if flip:spr=spr.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    shadow(im,x,y,size*.58,13)
    paste(im,spr,x,y,h=size,anchor='bottom',angle=fall)
    if not fall:
        # 원본 보라색 옷에 노란 번호를 붙여 상대 팀을 구분한다.
        text(im,'13',x,y-size*.285,15,LIME,width=30,stroke=1)

def ball(im,x,y,r=31,t=0,rotate=True):
    shadow(im,x,y+r*.7,r*1.45,8)
    paste(im,asset(str(HERE/'assets/zombie_head.png')),x,y,w=r*2,angle=(-t*330)%360 if rotate else 0)

def impact(im,x,y,r=45,color=LIME,seed=4):
    p=Pen(im);points=[]
    for i in range(24):
        a=i*math.pi/12;rad=r if i%2==0 else r*.43
        points.append((x+math.cos(a)*rad,y+math.sin(a)*rad))
    p.poly(points,color,INK,3)

def speed(im,t,y=560):
    p=Pen(im)
    for i in range(9):
        yy=y-110+i*31; xx=40+((i*81-t*270)%480)
        p.line([(xx,yy),(xx+35+(i%3)*15,yy)],'#a2c39a',2)

def board(im,lang,t,scored=False):
    p=Pen(im)
    p.rect([40,108,500,147],INK,r=12)
    p.rect([47,115,125,140],RED,r=6)
    text(im,'COM',86,126,19,CREAM,width=67)
    text(im,'1 : 0' if scored else '0 : 0',168,126,24,LIME,width=72)
    text(im,COPY[lang]['team'],282,126,18,CREAM,lang,width=125)
    text(im,'90+7′',435,126,20,TEAL,width=85)
    text(im,'COMSTOCK',76,81,18,CREAM,width=100)
    text(im,'07 / ZOMBIE LEAGUE',404,81,12,TEAL,width=182,heavy=False)

def caption(im,s,lang,y=205,size=39,color=CREAM):
    text(im,s,270,y,size,color,lang,width=448,stroke=2)

def goal(im,wiggle=0):
    p=Pen(im)
    # 정면이 열린 골대, 뒤쪽 그물과 지지대를 서로 다른 색으로 그린다.
    p.poly([(257,415),(477,415),(497,572),(278,572)],'#233f43',INK,3)
    for i in range(12):
        x=257+i*20; p.line([(x,416),(x+21,572)],'#75998c',1)
    for i in range(9):
        y=416+i*19; p.line([(257+(y-416)*.13,y),(477+(y-416)*.13,y+wiggle)],'#75998c',1)
    p.line([(242,562),(242,397),(461,397),(461,561)],CREAM,7)
    p.line([(242,397),(257,415),(477,415),(461,397)],CREAM,3)
    p.line([(461,561),(497,572)],CREAM,3)
    p.line([(242,562),(278,572)],CREAM,3)
    p.poly([(198,563),(495,563),(554,698),(93,698)],None,'#b0cba0',3)

def confetti(im,t):
    p=Pen(im);rng=random.Random(72)
    for i in range(80):
        x=rng.uniform(22,518)+math.sin(t*2+i)*12
        y=180+(rng.uniform(-600,300)+t*rng.uniform(95,180))%620
        col=rng.choice([LIME,TEAL,CREAM,RED])
        p.line([(x,y),(x+math.cos(i+t)*7,y+math.sin(i+t)*7)],col,3)

def frame(t,lang='KO'):
    c=COPY[lang]; im=stadium().copy();p=Pen(im)
    if t<2.4:
        board(im,lang,t)
        caption(im,c['hook'],lang,y=202,size=43)
        if t>.72: caption(im,c['hook2'],lang,y=259,size=30,color=LIME)
        zombie(im,427,562,139,t=t)
        robot(im,180,718,1.29,'idle',int(t*8)%24,lean=-3)
        # 공 대신 좀비 머리가 튀어 들어오는 오프닝.
        u=smooth(t/.6); bx=mix(580,370,u);by=664-abs(math.sin(t*5))*13
        ball(im,bx,by,51,t=t)
        if t>.75:
            p.line([(387,563),(396,588),(380,609)],LIME,4)
            caption(im,'?!',lang,y=495,size=60,color=LIME)
        text(im,c['tag'],270,804,18,TEAL,lang,width=420)
    elif t<8.4:
        st=t-2.4; board(im,lang,t); speed(im,t)
        times=[1.0,2.85,4.75]; idx=min(2,max(0,int((st+.35)/1.85)))
        caption(im,c['run'] if st<.55 else c['dodge'][idx],lang,y=211,size=37)
        bob=math.sin(st*12)*5
        rx=200+17*math.sin(st*3.3); ry=696+21*math.sin(st*2.5)
        for j,ct in enumerate(times):
            dt=st-ct; zx=285-dt*207;zy=658+(j%2)*48
            if -100<zx<650:
                if dt>.15:
                    zombie(im,zx,zy+15,173,t=t,fall=min(84,(dt-.15)*200)*(-1 if j%2 else 1))
                    if dt<.75:
                        text(im,'?!',zx,zy-170,31,LIME,width=60,stroke=2)
                        p.ellipse([zx-35,zy-185,zx+35,zy-174],None,LIME,2)
                else: zombie(im,zx,zy,173,t=t)
        # 실시간 다리 사이 왕복 드리블. 세 번째는 반대발로 방향 전환.
        phase=int(st*40)%24
        bx=rx+43+20*math.sin(st*8.2);by=ry-7-abs(math.sin(st*8.2))*16
        shadow(im,rx,ry+9,116,16)
        robot(im,rx,ry+bob,.94,'run',phase,lean=-5)
        ball(im,bx,by,29,t=st)
        if 2.75<st<3.28: text(im,c['nutmeg'],290,417,31,LIME,lang,width=425,stroke=2)
        for j,ct in enumerate(times):
            if 0<st-ct<.22: impact(im,rx+72,ry-26,26*(1-(st-ct)/.22),CREAM)
        p.rect([76,799,464,805],INK,r=3)
        p.rect([76,799,76+388*clamp(st/6),805],LIME,r=3)
    elif t<11.7:
        st=t-8.4; score=st>=1.50;board(im,lang,t,score);goal(im,math.sin(st*44)*3 if score else 0)
        caption(im,c['goal'] if score else c['shot'],lang,y=211,size=48 if score else 36,color=LIME if score else CREAM)
        if st<1.10:zombie(im,350+max(0,st-.6)*100,562,133,t=t)
        else:zombie(im,404+min(1,st-1.1)*87,574,133,t=t,fall=-80)
        shadow(im,155,704,122,18)
        q=clamp((st-.3)/.54)
        robot(im,155,698,1.03,'kick',int(q*23))
        if st<.78:ball(im,232,674,32,t=t)
        elif st<1.50:
            u=(st-.78)/.72;bx=mix(232,429,u);by=mix(674,448,u)-math.sin(u*math.pi)*65
            for v in [max(0,u-.11),max(0,u-.06)]:
                xx=mix(232,429,v); yy=mix(674,448,v)-math.sin(v*math.pi)*65
                p.line([(xx-20,yy+18),(xx+7,yy-7)],LIME,4)
            ball(im,bx,by,mix(32,24,u),t=t*1.7)
        else:
            u=st-1.50;ball(im,429+math.sin(u*8)*9*math.exp(-u),min(548,448+u*180),24,t=t)
            if u<.33:impact(im,429,453,78*(1-u/.33))
            if u>.15:text(im,'1–0',270,327,65,CREAM,width=230,stroke=3)
            confetti(im,u)
        if .78<st<.97:impact(im,241,661,51*(1-(st-.78)/.19),LIME)
    elif t<17.6:
        st=t-11.7;board(im,lang,t,True)
        p.ellipse([-90,473,630,977],None,'#8dae8b',4)
        if st<.75:
            caption(im,c['goal'],lang,y=210,size=43,color=LIME)
            x=mix(133,270,smooth(st/.75));shadow(im,x,719,150,20)
            robot(im,x,704,1.36,'run',int(st*36)%24)
        elif st<1.0:
            shadow(im,270,719,159,20);robot(im,270,723,1.36,'crouch',0)
        elif st<1.95:
            u=(st-1)/.95;rise=140*math.sin(math.pi*u)
            shadow(im,270,719,150-50*math.sin(math.pi*u),18)
            turn=math.cos(math.pi*u)
            robot(im,270,701-rise,1.36,'jump',0,back=turn<0,turn=.58+.42*abs(turn))
            for j in range(3):p.line([(180-j*10,630+20*j),(170-j*12,657+20*j)],LIME,3)
        else:
            u=st-1.95;shadow(im,270,730,220,24)
            robot(im,270,711+math.exp(-u*8)*math.sin(u*25)*9,1.55,'siu',0,back=True)
            if u<.5:
                p.ellipse([270-u*360,725-u*45,270+u*360,725+u*45],None,LIME,5)
            # 착지 후 앞/뒤 구도가 튀지 않도록 끝까지 뒷모습 유지.
            caption(im,'SIUUU!',lang,y=248,size=110,color=LIME)
            text(im,'7',457,590,88,'#aac591',width=65,stroke=2)
            if u>.8: confetti(im,u-.8)
        if st>2.65:
            text(im,'COMNALDO',270,810,35,CREAM,width=400,stroke=2)
        else:text(im,'COMSTOCK  /  07',270,809,21,TEAL,width=400)
    else:
        st=t-17.6
        # 엔딩은 주소를 읽을 수 있도록 충분히 유지한다.
        im=Image.new('RGB',(W,H),INK);p=Pen(im)
        for i in range(9):
            x=-240+i*110+st*9
            p.poly([(x,0),(x+38,0),(x+420,960),(x+382,960)],'#142b35')
        p.ellipse([81,140,459,518],None,'#28464a',2)
        p.ellipse([49,108,491,550],None,'#28464a',1)
        paste(im,asset(str(RES/'UI/title_logo.png')),270,245,w=438)
        text(im,'COMSTOCK',270,340,25,TEAL,width=410)
        text(im,c['cta'][0],270,415,39,CREAM,lang,width=446,check=True)
        text(im,c['cta'][1],270,466,39,LIME,lang,width=446,check=True)
        p.rect([52,530,488,591],LIME,r=13)
        text(im,c['button'],270,560,26,INK,lang,width=409,check=True)
        text(im,'pyramid-studio.itch.io/comstock',270,627,24,CREAM,width=453,heavy=False,check=True)
        robot(im,266,808,.46,'siu',0,back=True)
        ball(im,372+math.sin(st*2)*17,799,21,t=st)
        text(im,c['fine'],270,850,12,'#8fa5a5',lang,width=452,heavy=False,check=True)
    return im

def previews():
    pts=[.95,3.15,5.28,7.63,9.6,10.15,13.17,14.4,16.9,20.0]
    for lang in COPY:
        sheet=Image.new('RGB',(5*270,2*502),INK)
        for j,t in enumerate(pts):
            im=frame(t,lang); im.save(HERE/f'preview_{lang}_{j:02}.jpg',quality=91)
            thumb=im.resize((270,480),Image.Resampling.LANCZOS)
            sheet.paste(thumb,((j%5)*270,(j//5)*502))
            d=ImageDraw.Draw(sheet);d.text(((j%5)*270+8,(j//5)*502+481),f'{t:.2f}s',fill='white')
        sheet.save(HERE/f'contact_{lang}.jpg',quality=94)
    (HERE/'text_bounds.json').write_text(json.dumps(TEXT_BOUNDS,ensure_ascii=False,indent=2),encoding='utf-8')

def render(lang):
    path=HERE/f'Comstock_Football_{lang}_1080x1920.mp4'
    cmd=[str(FFMPEG),'-y','-hide_banner','-loglevel','warning','-f','rawvideo','-pix_fmt','rgb24','-s',f'{W}x{H}','-r',str(FPS),'-i','-',
         '-i',str(HERE/'football_audio.wav'),'-map','0:v','-map','1:a','-c:v','libx264','-preset','fast','-crf','18','-pix_fmt','yuv420p',
         '-af','loudnorm=I=-16:TP=-1.5:LRA=8','-c:a','aac','-b:a','192k','-ar','48000','-movflags','+faststart','-t',str(DUR),str(path)]
    with open(HERE/f'encode_{lang}.log','w',encoding='utf-8') as log:
        proc=subprocess.Popen(cmd,stdin=subprocess.PIPE,stderr=log)
        start=time.time()
        try:
            for i in range(int(DUR*FPS)):
                proc.stdin.write(frame(i/FPS,lang).tobytes())
                if i%90==0:print(f'{lang} {i}/{int(DUR*FPS)} {time.time()-start:.1f}s',flush=True)
        finally:proc.stdin.close()
        if proc.wait()!=0:raise RuntimeError('인코딩 실패: '+str(HERE/f'encode_{lang}.log'))
    print(str(path),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--preview',action='store_true');parser.add_argument('--lang',default='KO');args=parser.parse_args()
    if args.preview:previews()
    else:render(args.lang)
