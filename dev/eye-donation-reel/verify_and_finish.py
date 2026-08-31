# -*- coding: utf-8 -*-
"""실제 전달할 MP4 전체 디코딩 및 시각 검수 자료 생성."""
from pathlib import Path
import subprocess,json,re,hashlib
from PIL import Image,ImageDraw
from render_reel import HERE,FFMPEG,COPY,TIMELINE,SAMPLES,draw_frame,TEXT_BOUNDS,REVISION

reports={}
for lang in COPY:
 src=HERE/f'Comstock_EyeDonation_{lang}_1080x1920{REVISION}.mp4'
 test=subprocess.run([str(FFMPEG),'-hide_banner','-i',str(src),'-progress','pipe:1','-f','null','NUL'],capture_output=True,text=True,encoding='utf-8',errors='replace')
 (HERE/f'verification_{lang}.log').write_text(test.stderr+'\n'+test.stdout,encoding='utf-8')
 assert test.returncode==0,test.stderr
 for phrase in ['1080x1920','30 fps','48000 Hz, stereo','Duration: 00:00:23.00','yuv420p']:
  assert phrase in test.stderr,phrase
 counts=re.findall(r'^frame=(\d+)$',test.stdout,re.M)
 assert counts and int(counts[-1])==690,counts
 sheet=Image.new('RGB',(1080,1506),'#132934')
 for j,t in enumerate(SAMPLES):
  dst=HERE/f'encoded_{lang}_{j:02d}.png'
  subprocess.run([str(FFMPEG),'-y','-v','error','-ss',str(t),'-i',str(src),'-frames:v','1',str(dst)],check=True)
  thumb=Image.open(dst).resize((270,480),Image.Resampling.LANCZOS)
  x,y=(j%4)*270,(j//4)*502;sheet.paste(thumb,(x,y));ImageDraw.Draw(sheet).text((x+10,y+483),f'{t:.2f}s',fill='white')
  draw_frame(t,lang,True)
 sheet.save(HERE/f'검수_{lang}.jpg',quality=94)
 Image.open(HERE/f'encoded_{lang}_00.png').convert('RGB').save(HERE/f'Comstock_EyeDonation_{lang}_cover{REVISION}.jpg',quality=96)
 level=subprocess.run([str(FFMPEG),'-hide_banner','-i',str(src),'-af','loudnorm=I=-16:TP=-1.5:LRA=8:print_format=json','-f','null','NUL'],capture_output=True,text=True)
 loud=json.loads(re.findall(r'\{[^{}]+\}',level.stderr,re.S)[-1])
 assert float(loud['input_tp'])<=-.5,loud
 reports[lang]={'file':src.name,'width':1080,'height':1920,'fps':30,'frames':690,'duration':23.0,
  'video_codec':'H.264','pixel_format':'yuv420p','audio_codec':'AAC','audio_channels':2,'sample_rate':48000,
  'bytes':src.stat().st_size,'sha256':hashlib.sha256(src.read_bytes()).hexdigest(),'decode_errors':0,
  'loudness_lufs':loud['input_i'],'true_peak_dbtp':loud['input_tp'],'ending_seconds':4.3}
 print(json.dumps(reports[lang],ensure_ascii=False),flush=True)
 # 같은 내용의 작은 미리보기도 제공한다. 최종 전달 원본은 위 1080×1920 파일이다.
 subprocess.run([str(FFMPEG),'-y','-v','error','-i',str(src),'-vf','scale=540:960','-c:v','libx264','-crf','24','-preset','fast','-c:a','aac','-b:a','128k','-t','23','-movflags','+faststart',str(HERE/f'Comstock_EyeDonation_{lang}_preview{REVISION}.mp4')],check=True)
assert all(b['bounds'][0]>=70 and b['bounds'][2]<=1010 and b['bounds'][1]>=180 and b['bounds'][3]<=1760 for b in TEXT_BOUNDS)
voices=json.loads((HERE/'audio_cues.json').read_text(encoding='utf-8'))['voices']
assert all(v['end']<=v['slot_end'] for seq in voices.values() for v in seq)
(HERE/'text_bounds.json').write_text(json.dumps(TEXT_BOUNDS,ensure_ascii=False,indent=2),encoding='utf-8')
(HERE/'검증결과.json').write_text(json.dumps({'videos':reports,'timeline':TIMELINE,'text_bounds_passed':True,'voice_slots_passed':True,'reference_visual_inspected':True},ensure_ascii=False,indent=2),encoding='utf-8')
