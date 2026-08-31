# -*- coding: utf-8 -*-
"""완성 영상 전체 디코딩, 오디오 보정, 엔딩/대표 프레임 검수 자료 생성."""
from pathlib import Path
import subprocess,json,re,hashlib
from PIL import Image,ImageDraw
from render_reel import HERE,FFMPEG,COPY,TIMELINE

reports={}
for lang in COPY:
    src=HERE/f'Comstock_Football_{lang}_1080x1920.mp4'
    target=HERE/f'Comstock_Football_{lang}_final.mp4'
    subprocess.run([str(FFMPEG),'-y','-hide_banner','-loglevel','error','-i',str(src),'-i',str(HERE/'football_audio.wav'),
                    '-map','0:v','-map','1:a','-c:v','copy',
                    '-af','loudnorm=I=-16:TP=-1.5:LRA=8','-c:a','aac','-b:a','192k','-ar','48000','-movflags','+faststart','-t','22',str(target)],check=True)
    target.replace(src)
    # 전체 영상을 끝까지 디코딩하여 손상 여부와 프레임 수를 검사한다.
    test=subprocess.run([str(FFMPEG),'-hide_banner','-i',str(src),'-progress','pipe:1','-f','null','NUL'],capture_output=True,text=True)
    (HERE/f'verification_{lang}.log').write_text(test.stderr+'\n'+test.stdout,encoding='utf-8')
    assert test.returncode==0,test.stderr
    assert '1080x1920' in test.stderr
    assert '30 fps' in test.stderr
    assert '48000 Hz, stereo' in test.stderr
    counts=re.findall(r'^frame=(\d+)$',test.stdout,re.M)
    assert counts and int(counts[-1])==660,counts
    assert 'Duration: 00:00:22.00' in test.stderr
    # 실제 MP4에서 뽑은 프레임을 사용한다. 렌더 전 미리보기와 구분한다.
    times=[.95,3.75,5.7,7.7,9.55,10.28,13.12,14.5,17.58,21.75]
    sheet=Image.new('RGB',(1350,1004),'#101e29')
    for j,t in enumerate(times):
        dst=HERE/f'encoded_{lang}_{j:02}.png'
        subprocess.run([str(FFMPEG),'-y','-hide_banner','-loglevel','error','-ss',str(t),'-i',str(src),'-frames:v','1',str(dst)],check=True)
        thumb=Image.open(dst).resize((270,480),Image.Resampling.LANCZOS)
        sheet.paste(thumb,((j%5)*270,(j//5)*502))
        ImageDraw.Draw(sheet).text(((j%5)*270+8,(j//5)*502+482),f'{t:.2f}s',fill='white')
    sheet.save(HERE/f'검수_{lang}.jpg',quality=94)
    Image.open(HERE/f'encoded_{lang}_07.png').convert('RGB').save(HERE/f'Comstock_Football_{lang}_cover.jpg',quality=96)
    level=subprocess.run([str(FFMPEG),'-hide_banner','-i',str(src),'-af','loudnorm=I=-16:TP=-1.5:LRA=8:print_format=json','-f','null','NUL'],capture_output=True,text=True)
    loud=json.loads(re.findall(r'\{[^{}]+\}',level.stderr,re.S)[-1])
    reports[lang]={'file':src.name,'width':1080,'height':1920,'fps':30,'frames':660,'duration':22.0,
      'video_codec':'H.264','pixel_format':'yuv420p','audio_codec':'AAC','audio_channels':2,'sample_rate':48000,
      'bytes':src.stat().st_size,'sha256':hashlib.sha256(src.read_bytes()).hexdigest(),'decode_errors':0,
      'loudness_lufs':loud['input_i'],'true_peak_dbtp':loud['input_tp'],'ending_seconds':4.4}
    print(json.dumps(reports[lang],ensure_ascii=False),flush=True)

subprocess.run([str(FFMPEG),'-y','-hide_banner','-loglevel','error','-i',str(HERE/'Comstock_Football_KO_1080x1920.mp4'),
                '-vf','scale=540:960','-c:v','libx264','-crf','24','-preset','fast','-c:a','aac','-b:a','128k','-movflags','+faststart',
                str(HERE/'Comstock_Football_preview.mp4')],check=True)
boxes=json.loads((HERE/'text_bounds.json').read_text(encoding='utf-8'))
assert all(b['bounds'][0]>=70 and b['bounds'][2]<=1010 and b['bounds'][1]>=100 and b['bounds'][3]<=1740 for b in boxes)
(HERE/'검증결과.json').write_text(json.dumps({'videos':reports,'timeline':TIMELINE,'ending_text_bounds_passed':True},ensure_ascii=False,indent=2),encoding='utf-8')
