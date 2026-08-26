using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 배경음악(BGM) 재생을 혼자서 전담하는 전역 매니저.
///
/// 설계 요점:
/// - **AudioSource가 딱 하나뿐**이다. 여러 곡이 동시에 겹쳐 나오는 사고는 소스를 하나로 묶는 것이
///   가장 확실한 예방책이라, 페이드 아웃/인도 소스 하나의 볼륨을 오르내리는 방식으로 처리한다.
/// - **씬에 배치하지 않는다.** `[RuntimeInitializeOnLoadMethod]`로 게임이 시작될 때 스스로
///   생성되고 `DontDestroyOnLoad`로 살아남는다. 씬마다 오브젝트를 넣으면 씬을 넘길 때 두 개가
///   잠깐 공존해 음악이 겹치거나, 넘어갈 때마다 곡이 처음부터 다시 시작된다.
/// - 씬이 바뀌면 <see cref="SceneManager.sceneLoaded"/>로 알아서 재생 목록을 바꾼다
///   (타이틀 씬 = 타이틀 곡 반복 / 그 외 = 인게임 곡 랜덤).
/// - **인게임 곡은 한 곡이 끝나면 방금 튼 곡을 제외하고** 랜덤으로 다음 곡을 고른다.
/// - 볼륨은 <see cref="PlayerPrefs"/>에 저장돼 다음 실행에도 유지된다.
///
/// 정비/상점 화면은 <c>Time.timeScale = 0</c>이라 시간에 의존하는 처리는 전부
/// <c>Time.unscaledDeltaTime</c>을 쓴다(오디오 자체는 timeScale의 영향을 받지 않아 계속 흐른다).
/// </summary>
public class MusicManager : MonoBehaviour
{
    /// <summary>Resources 안의 음악 폴더.</summary>
    private const string MusicFolder = "Musics";

    /// <summary>타이틀 화면에서 반복 재생할 곡 이름.</summary>
    private const string TitleClipName = "Title_BGM";

    /// <summary>인게임 곡 이름 접두사(Game_BGM01, Game_BGM02 … 몇 개든 자동으로 잡힌다).</summary>
    private const string GameClipPrefix = "Game_BGM";

    /// <summary>보스 전투 전용 곡(2026-08-26 사용자 제공 <c>boss_battle_bgm (Condensed Rush Mix)</c>).
    /// 접두사가 <see cref="GameClipPrefix"/>와 달라서 일반 재생목록에는 섞이지 않는다.</summary>
    private const string BossClipName = "Boss_BGM";

    /// <summary>타이틀 곡을 쓸 씬 이름.</summary>
    private const string TitleSceneName = "Title";

    private const string VolumePrefsKey = "comstock_music_volume";
    private const float DefaultVolume = 0.7f;
    private const float FadeSeconds = 0.5f;

    public static MusicManager Instance { get; private set; }

    /// <summary>볼륨이 바뀌면 알린다(볼륨 슬라이더 UI가 여러 화면에 있을 수 있어서).</summary>
    public static event System.Action<float> OnVolumeChanged;

    private AudioSource source;
    private AudioClip title_clip;
    private AudioClip boss_clip;
    private readonly List<AudioClip> game_clips = new List<AudioClip>();

    // 지금 어떤 재생 목록을 쓰는 중인지. 씬을 넘겨도 목록이 같으면 곡을 끊지 않는다.
    private bool is_title_playlist;
    private bool playlist_started;

    /// <summary>보스 전투 곡을 트는 중인지. 재생목록(타이틀/인게임)과 <b>직교하는</b> 상태다.</summary>
    private bool is_boss_battle;

    private float target_volume = DefaultVolume;
    private float fade_velocity;      // 0이면 페이드 없음, 양수/음수면 초당 볼륨 변화량
    private AudioClip pending_clip;   // 페이드 아웃이 끝나면 재생할 곡
    private bool pending_loop;        // 그 곡을 반복 재생할지

    /// <summary>0~1 음악 볼륨. 설정하면 즉시 반영되고 PlayerPrefs에 저장된다.</summary>
    public static float Volume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePrefsKey, DefaultVolume));
        set
        {
            float clamped = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumePrefsKey, clamped);

            if (Instance != null)
            {
                Instance.target_volume = clamped;
                // 페이드 중이 아니면 즉시 반영(페이드 중이면 페이드가 끝나면서 이 값에 도달한다)
                if (Instance.fade_velocity == 0f && Instance.source != null)
                {
                    Instance.source.volume = clamped;
                }
            }

            OnVolumeChanged?.Invoke(clamped);
        }
    }

    /// <summary>지금 재생 중인 곡 이름(검수/디버그용).</summary>
    public string CurrentClipName => source != null && source.clip != null ? source.clip.name : "(없음)";

    /// <summary>인게임 재생 목록에 잡힌 곡 개수(검수/디버그용).</summary>
    public int GameClipCount => game_clips.Count;

    // 씬에 오브젝트를 두지 않고 게임 시작 시 스스로 생성된다. 어느 씬에서 플레이를 시작하든
    // (에디터에서 Ground01을 직접 실행하는 경우 포함) 동일하게 동작한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject("MusicManager");
        go.AddComponent<MusicManager>();
    }

    private void Awake()
    {
        // 두 번째 인스턴스는 즉시 자살한다(음악이 겹치는 사고의 대부분이 여기서 생긴다).
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;               // 다음 곡을 직접 고르려고 반복을 끈다(타이틀만 예외로 켠다)
        source.spatialBlend = 0f;          // 2D
        source.ignoreListenerPause = true; // 일시정지 상태에서도 음악은 흐르게

        target_volume = Volume;
        source.volume = target_volume;

        LoadClips();

        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureSingleAudioListener();
        ApplyPlaylistForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Instance = null;
        }
    }

    /// <summary>
    /// 보스 전투 곡으로 갈아탄다(보스 등장) / 원래 인게임 곡으로 돌아온다(보스 처치·웨이브 종료·게임오버).
    ///
    /// <b>호출부가 상태를 들고 있지 않아도 되도록 멱등하게 만들었다</b> - 같은 값으로 여러 번
    /// 불러도 곡이 처음부터 다시 시작되지 않는다(보스 처치/웨이브 종료/게임오버가 겹쳐서
    /// 들어오는 경로가 실제로 있다).
    /// </summary>
    public static void SetBossBattle(bool active)
    {
        if (Instance == null) return;
        Instance.ApplyBossBattle(active);
    }

    private void ApplyBossBattle(bool active)
    {
        if (is_boss_battle == active) return;
        if (active && boss_clip == null) return; // 곡이 없으면 아무것도 하지 않는다(기존 곡 유지)

        is_boss_battle = active;

        // 타이틀 화면에서는 보스 곡을 틀 이유가 없다(있을 수 없는 조합이지만 방어).
        if (is_title_playlist) return;

        CrossFadeTo(active ? boss_clip : PickNextGameClip(), loop: active);
    }

    private void LoadClips()
    {
        title_clip = Resources.Load<AudioClip>($"{MusicFolder}/{TitleClipName}");
        boss_clip = Resources.Load<AudioClip>($"{MusicFolder}/{BossClipName}");

        // 곡 파일을 추가/삭제해도 코드를 고치지 않도록 폴더를 통째로 읽어 접두사로 거른다.
        AudioClip[] all = Resources.LoadAll<AudioClip>(MusicFolder);
        game_clips.Clear();
        foreach (AudioClip clip in all)
        {
            if (clip != null && clip.name.StartsWith(GameClipPrefix)) game_clips.Add(clip);
        }
        game_clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name)); // 순서를 안정적으로

        if (title_clip == null) Debug.LogWarning($"[MusicManager] 타이틀 곡을 찾지 못했습니다: Resources/{MusicFolder}/{TitleClipName}");
        if (boss_clip == null) Debug.LogWarning($"[MusicManager] 보스 곡을 찾지 못했습니다: Resources/{MusicFolder}/{BossClipName}");
        if (game_clips.Count == 0) Debug.LogWarning($"[MusicManager] 인게임 곡({GameClipPrefix}*)을 찾지 못했습니다: Resources/{MusicFolder}");
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureSingleAudioListener();

        // 보스전 도중에 씬이 바뀌면(재시작·타이틀 복귀) 보스 곡을 그대로 끌고 가면 안 된다.
        // playlist_started를 내려서 아래 ApplyPlaylistForScene이 "같은 재생목록이니 그대로 둔다"
        // 로 빠져나가지 않고 반드시 새 곡을 고르게 한다.
        if (is_boss_battle)
        {
            is_boss_battle = false;
            playlist_started = false;
        }

        ApplyPlaylistForScene(scene.name);
    }

    /// <summary>
    /// 씬에 <see cref="AudioListener"/>가 정확히 하나만 있도록 맞춘다.
    ///
    /// 실제로 겪은 문제: `Title.unity`의 카메라에 AudioListener가 없어서 타이틀 곡이 재생은
    /// 되는데 **아무 소리도 나지 않았다**(콘솔에 "There are no audio listeners in the scene").
    /// 씬 쪽은 따로 고쳤지만, 앞으로 새 씬을 추가할 때 같은 실수로 음악이 조용히 사라지는 것을
    /// 막으려고 안전망을 둔다. 반대로 씬에 리스너가 있으면 우리가 붙였던 것은 즉시 떼어내
    /// "리스너가 2개"라는 반대쪽 경고도 나지 않게 한다.
    /// </summary>
    private void EnsureSingleAudioListener()
    {
        AudioListener own = GetComponent<AudioListener>();

        int others = 0;
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (listener != own) others++;
        }

        if (others == 0 && own == null) gameObject.AddComponent<AudioListener>();
        else if (others > 0 && own != null) DestroyImmediate(own);
    }

    /// <summary>
    /// 씬 이름을 보고 재생 목록을 정한다. 같은 목록이 이어지는 씬 전환에서는 곡을 끊지 않는다
    /// (지금은 타이틀↔인게임뿐이라 항상 바뀌지만, 인게임 씬이 늘어나도 곡이 끊기지 않는다).
    /// </summary>
    private void ApplyPlaylistForScene(string sceneName)
    {
        bool wantsTitle = sceneName == TitleSceneName;

        if (playlist_started && wantsTitle == is_title_playlist) return;

        is_title_playlist = wantsTitle;
        playlist_started = true;

        if (wantsTitle) CrossFadeTo(title_clip, loop: true);
        else CrossFadeTo(PickNextGameClip(), loop: false);
    }

    /// <summary>
    /// 방금 나온 곡을 빼고 무작위로 다음 곡을 고른다. 곡이 하나뿐이면 어쩔 수 없이 같은 곡이다.
    /// </summary>
    private AudioClip PickNextGameClip()
    {
        if (game_clips.Count == 0) return null;
        if (game_clips.Count == 1) return game_clips[0];

        AudioClip current = source != null ? source.clip : null;

        // 현재 곡을 제외한 후보 중에서 뽑는다 - "다음 곡이 현재 곡과 겹치면 안 된다"는 요구사항.
        var candidates = new List<AudioClip>(game_clips.Count);
        foreach (AudioClip clip in game_clips)
        {
            if (clip != current) candidates.Add(clip);
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    /// <summary>
    /// 지금 곡을 페이드 아웃해서 끄고, 새 곡을 페이드 인으로 시작한다.
    /// 소스가 하나라 "끝난 뒤 시작"이 보장되므로 두 곡이 동시에 들릴 수 없다.
    /// </summary>
    private void CrossFadeTo(AudioClip clip, bool loop)
    {
        if (clip == null)
        {
            source.Stop();
            source.clip = null;
            pending_clip = null;
            fade_velocity = 0f;
            return;
        }

        if (!source.isPlaying || source.clip == null)
        {
            // 재생 중이 아니면 곧바로 페이드 인
            StartClip(clip, loop);
            return;
        }

        // 재생 중이면 먼저 페이드 아웃하고, 다 내려가면 Update가 새 곡으로 갈아끼운다.
        // 현재 곡의 loop는 건드리지 않는다(페이드 도중에 지금 곡이 멈춰버리면 안 된다).
        pending_clip = clip;
        pending_loop = loop;
        fade_velocity = -Mathf.Max(source.volume, 0.0001f) / FadeSeconds;
    }

    private void StartClip(AudioClip clip, bool loop)
    {
        source.Stop();            // 겹침 방지 - 어떤 경로로 들어와도 항상 멈추고 시작한다
        source.clip = clip;
        source.loop = loop;
        source.volume = 0f;
        source.Play();

        pending_clip = null;
        fade_velocity = target_volume / FadeSeconds;
    }

    private void Update()
    {
        if (source == null) return;

        UpdateFade();

        // 인게임 곡이 끝나면(반복이 꺼져 있으므로 자연히 멈춘다) 다른 곡을 고른다.
        // 보스 곡은 반복 재생이라 끝나지 않지만, 상태가 어긋나도 곡이 바뀌지 않도록 함께 막는다.
        if (!is_title_playlist && !is_boss_battle && !source.isPlaying && source.clip != null && pending_clip == null && fade_velocity == 0f)
        {
            StartClip(PickNextGameClip(), loop: false);
        }
    }

    private void UpdateFade()
    {
        if (fade_velocity == 0f)
        {
            // 페이드가 아닐 때는 볼륨 설정 변경을 그대로 따라간다
            if (!Mathf.Approximately(source.volume, target_volume)) source.volume = target_volume;
            return;
        }

        // timeScale=0(정비/상점)에서도 페이드가 진행되도록 unscaled 사용
        source.volume = Mathf.Clamp(source.volume + fade_velocity * Time.unscaledDeltaTime, 0f, 1f);

        if (fade_velocity < 0f && source.volume <= 0f)
        {
            fade_velocity = 0f;
            if (pending_clip != null) StartClip(pending_clip, pending_loop);
            else source.Stop();
            return;
        }

        if (fade_velocity > 0f && source.volume >= target_volume)
        {
            source.volume = target_volume;
            fade_velocity = 0f;
        }
    }
}
