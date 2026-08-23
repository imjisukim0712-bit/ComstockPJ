using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 몬스터별 프레임 애니메이션(스프라이트 시퀀스) 조회 테이블.
///
/// 2026-08-20 사용자가 좀비 5종(스피터/스프린터/디스럭터/리더 + 보스 "좀비 군집체")의
/// 이동 모션을 8프레임씩 제공하면서, 그 전까지 <b>기본 좀비(200001) 전용</b>으로만 하드코딩돼
/// 있던 프레임 재생 코드(EnemyUnit.ZombieMoveFrames)를 몬스터 단위로 일반화한 것이다.
///
/// <b>왜 몬스터ID로 나누는가</b> - 몬스터마다 규격(소형~초대형)이 달라 프레임의 픽셀 크기가
/// 다르다. 남의 프레임을 재생하면 몸 크기가 순간적으로 다른 규격으로 바뀌어 보인다.
/// 그래서 프레임 세트는 항상 "그 몬스터 전용"이며, 세트가 없는 몬스터(차저)는 프리팹의
/// 정지 스프라이트를 그대로 유지한다.
///
/// <b>아트 규격</b>(2026-08-20 임포트 시 실측·정렬) - 각 세트는 그 몬스터의 정지 스프라이트와
/// 픽셀 크기가 같다(리더 800→450, 스프린터 256→250은 임포트할 때 축소해 맞췄다).
/// 보스만 원본이 512px로 와서 축소·확대 없이 PPU=64로 초대형 규격(800px = 8유닛)에 맞췄다.
/// 원본이 오른쪽을 보고 있던 세트(스프린터/보스)는 임포트할 때 좌우 반전해
/// "아트는 왼쪽을 본다"는 프로젝트 관례(<see cref="EnemyUnit"/>.LateUpdate의 flipX)에 맞췄다.
/// </summary>
public static class MonsterAnimationLibrary
{
    /// <summary>Resources 폴더 하나(= 프레임 세트 하나)에 대한 재생 정보.</summary>
    public class Clip
    {
        /// <summary>파일명 오름차순으로 정렬된 프레임. 이 순서가 곧 재생 순서다.</summary>
        public Sprite[] Frames = new Sprite[0];

        /// <summary>
        /// 이동하지 않을 때(정지·공격 모션 중)에 보여줄 "멈춘 이미지"의 프레임 번호.
        /// <b>-1이면 프리팹에 박혀 있는 정지 스프라이트를 그대로 쓴다</b>(기본 좀비가 그렇다 -
        /// Zombie.png라는 전용 정지 그림이 이미 있고, 걷기 프레임은 전부 보행 중 자세다).
        /// </summary>
        public int StillFrameIndex;

        /// <summary>기준 이동속도(MonsterData.monster_speed)로 움직일 때의 초당 프레임 수.</summary>
        public float Fps = 6f;

        /// <summary>
        /// 멈춰 있을 때도 계속 재생할지. 제자리 호흡/꿈틀거림처럼 "이동 모션"이 아닌 세트에 쓴다
        /// (보스 군집체가 그렇다 - 완전히 얼어붙어 있으면 죽은 것처럼 보인다).
        /// </summary>
        public bool PlayWhileIdle;

        public bool HasFrames => Frames != null && Frames.Length > 0;

        public Sprite StillFrame =>
            HasFrames && StillFrameIndex >= 0 ? Frames[Mathf.Min(StillFrameIndex, Frames.Length - 1)] : null;
    }

    // 등록 정보(로드 전). 실제 Sprite 로드는 처음 조회될 때 한 번만 한다.
    private struct Entry
    {
        public string ResourceFolder;
        public int StillFrameIndex;
        public float Fps;
        public bool PlayWhileIdle;
    }

    /// <summary>보스(좀비 군집체) 프레임 세트의 Resources 폴더명. <see cref="BossUnit"/>이 참조한다.</summary>
    public const string BossFolder = "BossMove";

    // 몬스터ID → 프레임 세트.
    //
    // Fps는 "그 몬스터의 기준 이동속도로 움직일 때"의 값이다(실제 속도에 비례해 자동으로
    // 빨라지고 느려진다 - EnemyUnit.UpdateWalkAnimation). 그래서 빠른 몬스터가 아니라
    // <b>모션 자체의 보폭</b>에 맞춰 정한다: 스프린터는 네 발로 기는 질주 사이클이라 크게
    // 높이고(14), 덩치가 큰 리더는 조금만 올렸다(7).
    private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>
    {
        // 기본 좀비는 StillFrameIndex = -1 - 전용 정지 그림(Zombie.png)이 이미 프리팹에 있어
        // 2026-08-12부터 쓰던 "멈추면 Zombie.png로 되돌린다" 동작을 그대로 유지한다.
        { 200001, new Entry { ResourceFolder = "ZombieMove",    StillFrameIndex = -1, Fps = 6f } }, // 좀비
        // 2026-08-23 사용자 제공 "차저 걷기" 16프레임 적용 - 그 전까지는 프리팹의 정지
        // 스프라이트(Charger.png)를 그대로 썼다(위 ChargerUnit.ResolveMoveClip 참고: 돌진 중에는
        // 이 세트 대신 "ChargerCharge" 세트로 바뀐다).
        { 200002, new Entry { ResourceFolder = "ChargerMove",   StillFrameIndex = 0, Fps = 8f } },  // 차저
        { 200003, new Entry { ResourceFolder = "SpitterMove",   StillFrameIndex = 0, Fps = 6f } },  // 스피터
        { 200004, new Entry { ResourceFolder = "SprinterMove",  StillFrameIndex = 3, Fps = 14f } }, // 스프린터
        { 200005, new Entry { ResourceFolder = "DisruptorMove", StillFrameIndex = 0, Fps = 6f } },  // 디스럭터
        { 200006, new Entry { ResourceFolder = "LeaderMove",    StillFrameIndex = 0, Fps = 7f } },  // 리더
    };

    // 폴더명 단위 캐시 - 같은 세트를 여러 몬스터가 공유해도 한 번만 로드한다.
    private static readonly Dictionary<string, Clip> clip_cache = new Dictionary<string, Clip>();

    private static readonly Clip EmptyClip = new Clip();

    /// <summary>몬스터ID에 등록된 프레임 세트. 없으면 프레임이 0개인 빈 Clip(널 아님)을 돌려준다.</summary>
    public static Clip GetByMonsterId(int monsterId)
    {
        if (!Entries.TryGetValue(monsterId, out Entry entry)) return EmptyClip;
        return Load(entry);
    }

    /// <summary>
    /// 폴더명으로 직접 조회한다. 보스처럼 몬스터ID가 데이터테이블 밖에 있는(WaveManager가
    /// monster_id = -1로 만들어 넘기는) 유닛용 경로다.
    /// </summary>
    public static Clip GetByFolder(string resourceFolder, int stillFrameIndex = 0, float fps = 6f, bool playWhileIdle = false)
    {
        if (string.IsNullOrEmpty(resourceFolder)) return EmptyClip;

        return Load(new Entry
        {
            ResourceFolder = resourceFolder,
            StillFrameIndex = stillFrameIndex,
            Fps = fps,
            PlayWhileIdle = playWhileIdle,
        });
    }

    private static Clip Load(Entry entry)
    {
        if (clip_cache.TryGetValue(entry.ResourceFolder, out Clip cached)) return cached;

        Sprite[] loaded = Resources.LoadAll<Sprite>(entry.ResourceFolder);
        // 파일명 오름차순 = 재생 순서(ZombieMove의 walk_left_f0~f7과 같은 관례).
        System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));

        var clip = new Clip
        {
            Frames = loaded,
            StillFrameIndex = entry.StillFrameIndex,
            Fps = entry.Fps > 0f ? entry.Fps : 6f,
            PlayWhileIdle = entry.PlayWhileIdle,
        };

        if (loaded.Length == 0)
            Debug.LogWarning($"MonsterAnimationLibrary: Resources/{entry.ResourceFolder} 에서 스프라이트를 찾지 못했습니다.");

        clip_cache[entry.ResourceFolder] = clip;
        return clip;
    }

    /// <summary>씬을 다시 로드해 Resources가 언로드됐을 때 대비용(EnemyUnit.ResetStaticCaches에서 호출).</summary>
    public static void ResetCache() => clip_cache.Clear();
}
