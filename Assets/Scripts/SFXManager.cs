using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 짧은 효과음(SFX) 재생을 전담하는 전역 매니저. <see cref="MusicManager"/>와 같은 방식
/// (부트스트랩 + 싱글톤 + DontDestroyOnLoad)으로 스스로 살아남되, 크로스페이드/재생목록 없이
/// PlayOneShot만 한다(여러 효과음이 겹쳐도 서로 끊지 않고 믹싱된다). AudioListener는
/// MusicManager.EnsureSingleAudioListener()가 이미 씬마다 정확히 하나로 맞춰주므로 여기서
/// 따로 신경 쓰지 않는다.
///
/// <b>2026-08-14</b>: 프로젝트에 SFX 오디오 파일이 아직 없어서, <c>Resources/SFX</c> 폴더에서
/// 찾지 못한 클립 이름은 절차적으로 생성한 짧은 비프음으로 대신 채운다. 나중에 같은 이름의
/// 실제 오디오 파일을 그 폴더에 넣기만 하면(코드 수정 없이) 자동으로 그 파일이 우선 쓰인다 -
/// 폴더 스캔이 먼저 실행되고, 그때 채워지지 않은 빈 키만 절차 생성으로 채우기 때문이다.
/// </summary>
public class SFXManager : MonoBehaviour
{
    /// <summary>Resources 안의 효과음 폴더. 폴더가 없어도 Resources.LoadAll은 빈 배열을 돌려줄 뿐 예외를 던지지 않는다.</summary>
    private const string SfxFolder = "SFX";

    /// <summary>플레이어(로봇) 피격음. <b>2026-08-27부터 실제 파일이 있다</b>(사용자 제공
    /// <c>로봇_피격_효과음.wav</c>). 그전까지는 파일이 없어 <see cref="EnsurePlaceholderClip"/>가
    /// 만드는 880Hz 비프음이 대신 나고 있었다.</summary>
    private const string PlayerHitClipName = "Player_Hit";

    /// <summary>UI 버튼 클릭음(2026-08-25 사용자 제공 <c>23_ui_click.wav</c>).
    /// 개별 버튼마다 리스너를 다는 대신 <see cref="PlayUiClickIfButtonPressed"/>가 전역으로
    /// 감지해 재생한다 - 이 프로젝트 UI는 대부분 코드로 만들어지고 버튼이 화면마다 새로
    /// 생성되므로, 생성부마다 붙이면 반드시 빠뜨리는 곳이 생긴다.</summary>
    public const string UiClickClipName = "UI_Click";

    /// <summary>AI 코어 레벨업 효과음(2026-08-25 사용자 제공 <c>26_core_upgrade.wav</c>).
    /// <see cref="AiCoreManager"/>가 레벨업 이펙트와 같은 지점에서 한 번 재생한다.</summary>
    public const string LevelUpClipName = "LevelUp";

    // ── 2026-08-26 사용자 제공 전투 효과음 ────────────────────────────────
    // 파일은 전부 Resources/SFX에 있고 <b>이름으로만</b> 연결된다(이 클래스의 폴더 스캔 규칙).
    // 다른 소리로 바꾸고 싶으면 같은 이름의 파일을 덮어쓰면 코드 수정 없이 교체된다.
    //
    // 원본 파일명 → 이 프로젝트 이름:
    //   01_weapon_melee_attack     → Weapon_Melee
    //   02_weapon_rapid_fire       → Weapon_RapidFire   (연사화기 전용. 정밀화기는 2026-08-27부터 자기 소리가 있다)
    //   03_weapon_shotgun_fire     → Weapon_Shotgun
    //   06_weapon_explosive_launch → Weapon_Explosive
    //   Laser_pistol               → Weapon_LaserPistol (에너지 계열 중 투사체 무기)
    //   플라즈마캐논               → Weapon_PlasmaCannon(빔 무기)
    //   18/19_enemy_hit_a,b + zombiehit → Enemy_Hit_A/B/C (피격마다 무작위)
    //   01_cartoon_zombie_classic_splat → Enemy_Death
    //   보스 사운드/02~04          → Boss_Hit_A/B/C
    //   보스 사운드/05_heavy_burst → Boss_Death
    //
    // ── 2026-08-27 사용자 제공 교체분(`새사운드/` 폴더) ──────────────────
    //   좀비_사망음               → Enemy_Death        (교체. 0.66초 → 0.58초)
    //   플라즈마캐논_발사_효과음  → Weapon_PlasmaCannon(교체. 4.61초 → 2.10초 - 아래 빔 간격 주석 참고)
    //   로봇_피격_효과음          → Player_Hit         (신규. 비프음 placeholder를 대체)
    //   정밀형화기_발사_효과음    → Weapon_Precision   (신규. 그전까지 연사 소리를 빌려 썼다)
    //   디스럽터_폭발음           → Enemy_DisruptorExplode(신규. 자폭 적 전용 - 아래 주석 참고)
    // 나머지 7개(근접/레이저피스톨/산탄/연사/폭발/좀비피격/보스BGM)는 이미 적용된 것과
    // 바이트 단위로 같아서 할 일이 없었다(해시 대조로 확인).
    public const string WeaponMeleeClipName = "Weapon_Melee";
    public const string WeaponRapidFireClipName = "Weapon_RapidFire";
    public const string WeaponShotgunClipName = "Weapon_Shotgun";
    public const string WeaponExplosiveClipName = "Weapon_Explosive";
    public const string WeaponLaserPistolClipName = "Weapon_LaserPistol";
    public const string WeaponPlasmaCannonClipName = "Weapon_PlasmaCannon";
    /// <summary>정밀화기(대물저격총·지정사수소총) 전용 발사음(2026-08-27 신규).
    /// 2.10초로 다른 발사음(0.14~0.62초)보다 길지만, 앞 0.6초만 실제 소리이고 나머지는 잔향이라
    /// 발사 간격(<c>1/atsp</c> = 1.8~4초)과 겹치지 않는다.</summary>
    public const string WeaponPrecisionClipName = "Weapon_Precision";

    /// <summary>디스럽터(자폭하는 적)의 폭발음(2026-08-27 신규).
    /// <see cref="DisruptorUnit"/>이 <c>PlayDeathSfx</c>를 오버라이드해 일반 사망음 대신 낸다.
    /// <para><b>참고</b>: 사용자가 준 이 파일은 <c>Boss_Death.wav</c>와 바이트 단위로 같은 소리다 -
    /// 별도 에셋으로 둔 이유는 이 클래스의 "이름으로 교체" 규칙을 살려서, 나중에 둘 중 하나만
    /// 다른 소리로 바꿀 수 있게 하기 위해서다.</para></summary>
    public const string EnemyDisruptorExplodeClipName = "Enemy_DisruptorExplode";

    public const string EnemyDeathClipName = "Enemy_Death";
    public const string BossDeathClipName = "Boss_Death";

    /// <summary>일반 적 피격음 3종. 같은 소리가 연달아 나면 기계적으로 들리므로 무작위로 고른다.</summary>
    private static readonly string[] EnemyHitClipNames = { "Enemy_Hit_A", "Enemy_Hit_B", "Enemy_Hit_C" };

    /// <summary>보스 전용 피격음 3종(사용자가 "보스 사운드" 폴더에 넣어준 묵직한 파열음).</summary>
    private static readonly string[] BossHitClipNames = { "Boss_Hit_A", "Boss_Hit_B", "Boss_Hit_C" };

    private const string VolumePrefsKey = "comstock_sfx_volume";
    private const float DefaultVolume = 0.7f;

    public static SFXManager Instance { get; private set; }

    /// <summary>0~1 효과음 전역 볼륨(2026-08-18 환경설정 화면 추가). PlayerPrefs에 저장되며,
    /// 설정하는 즉시 반영된다(배경음 <see cref="MusicManager.Volume"/>과 같은 방식).
    /// 실제 효과음 재생(<see cref="Play"/>)이 이 값을 곱해서 쓴다.</summary>
    public static float Volume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePrefsKey, DefaultVolume));
        set
        {
            PlayerPrefs.SetFloat(VolumePrefsKey, Mathf.Clamp01(value));
            OnVolumeChanged?.Invoke(Mathf.Clamp01(value));
        }
    }

    /// <summary>볼륨이 바뀌면 알린다(설정 화면의 슬라이더가 다른 화면에서 바뀐 값을 따라가도록).</summary>
    public static event System.Action<float> OnVolumeChanged;

    private AudioSource source;
    private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

    /// <summary>클립(또는 무작위 그룹)별 마지막 재생 시각. <see cref="PlayThrottled"/> 전용.</summary>
    private readonly Dictionary<string, float> last_played_time = new Dictionary<string, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject("SFXManager");
        go.AddComponent<SFXManager>();
    }

    private void Awake()
    {
        // 두 번째 인스턴스는 즉시 자살한다(MusicManager와 동일 정책).
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D
        source.ignoreListenerPause = true; // 정비/상점 화면(Time.timeScale=0)에서도 재생 가능하도록

        foreach (AudioClip clip in Resources.LoadAll<AudioClip>(SfxFolder))
        {
            if (clip != null) clips[clip.name] = clip;
        }

        // 실제 파일이 없는 키만 절차적 비프음으로 채운다 - 폴더 스캔이 이미 채운 키는 건드리지 않는다.
        // 2026-08-27에 Player_Hit.wav가 들어와서 지금은 이 줄이 아무것도 하지 않는다.
        // 그래도 남겨둔다 - 파일이 빠지면 소리가 조용히 사라지는 대신 비프음으로 티가 난다.
        EnsurePlaceholderClip(PlayerHitClipName, frequency: 880f, duration: 0.08f);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        PlayUiClickIfButtonPressed();
    }

    /// <summary>
    /// 이번 프레임에 마우스 왼쪽 버튼이 <b>UI 버튼 위에서</b> 눌렸으면 클릭음을 재생한다.
    ///
    /// 전역 감지를 쓰는 이유는 <see cref="UiClickClipName"/> 주석 참고. 판정 대상을
    /// <see cref="Button"/>으로 한정해서 슬라이더 드래그나 빈 패널 클릭에는 소리가 나지 않는다.
    /// 입력은 프로젝트 관례대로 새 Input System(<see cref="Mouse.current"/>)을 직접 폴링하고,
    /// <c>Time.timeScale = 0</c>인 정비·상점 화면에서도 Update는 계속 돌기 때문에 그대로 동작한다.
    /// (<c>비활성 버튼</c>은 클릭돼도 아무 일이 없으므로 소리도 내지 않는다.)
    /// </summary>
    private void PlayUiClickIfButtonPressed()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

        EventSystem events = EventSystem.current;
        if (events == null) return;

        var pointer = new PointerEventData(events) { position = Mouse.current.position.ReadValue() };
        ui_raycast_results.Clear();
        events.RaycastAll(pointer, ui_raycast_results);

        for (int i = 0; i < ui_raycast_results.Count; i++)
        {
            GameObject hit = ui_raycast_results[i].gameObject;
            if (hit == null) continue;

            // 라벨/아이콘을 눌러도 부모 버튼이 받으므로 부모까지 거슬러 올라가 찾는다.
            var button = hit.GetComponentInParent<Button>();
            if (button == null || !button.IsInteractable()) continue;

            Play(UiClickClipName);
            return;
        }
    }

    // RaycastAll은 결과 리스트를 재사용할 수 있다(매 클릭마다 새로 할당하지 않는다).
    private readonly List<RaycastResult> ui_raycast_results = new List<RaycastResult>();

    /// <summary>클립이 없으면(에셋 미준비) 조용히 스킵한다 - 어떤 호출부도 null 체크를 할 필요가 없다.</summary>
    public static void Play(string clipName, float volumeScale = 1f)
    {
        if (Instance == null || string.IsNullOrEmpty(clipName)) return;
        if (!Instance.clips.TryGetValue(clipName, out AudioClip clip) || clip == null) return;

        Instance.source.PlayOneShot(clip, Mathf.Clamp01(volumeScale) * Volume);
    }

    /// <summary>
    /// 같은 클립이 <paramref name="minInterval"/>초 안에 다시 요청되면 <b>무시</b>한다.
    ///
    /// 전투 효과음(발사·피격)은 호출 빈도가 프레임 단위다 - 소켓 4개짜리 연사 무기면 초당 40번,
    /// 화면에 적이 20마리면 피격음도 그만큼 겹친다. <see cref="AudioSource.PlayOneShot"/>은
    /// 요청을 전부 믹싱하므로 그대로 두면 소리가 뭉개지고 볼륨이 치솟는다. 재생 자체를 막는
    /// 이 방식이 볼륨을 낮추는 것보다 확실하다(볼륨을 낮추면 겹침 자체는 그대로다).
    ///
    /// 시간은 <see cref="Time.unscaledTime"/>으로 잰다 - 정비/상점(timeScale=0)에서도 UI 소리가
    /// 나야 하고, 배속 연출 중에 간격이 늘었다 줄었다 하면 안 된다.
    /// </summary>
    public static void PlayThrottled(string clipName, float minInterval, float volumeScale = 1f)
    {
        if (Instance == null || string.IsNullOrEmpty(clipName)) return;

        float now = Time.unscaledTime;
        if (Instance.last_played_time.TryGetValue(clipName, out float last) && now - last < minInterval) return;

        Instance.last_played_time[clipName] = now;
        Play(clipName, volumeScale);
    }

    /// <summary>일반 적 피격음(3종 중 무작위). 간격 제한은 <see cref="PlayThrottled"/> 참고.</summary>
    public static void PlayEnemyHit(float volumeScale = 1f) => PlayRandom(EnemyHitClipNames, 0.05f, volumeScale);

    /// <summary>보스 피격음(3종 중 무작위).</summary>
    public static void PlayBossHit(float volumeScale = 1f) => PlayRandom(BossHitClipNames, 0.12f, volumeScale);

    /// <summary>
    /// 후보 중 하나를 무작위로 재생한다. <b>간격 제한은 후보 전체를 하나로 묶어서 건다</b> -
    /// 클립마다 따로 걸면 3종이 동시에 울려 제한이 사실상 없어진다.
    /// </summary>
    private static void PlayRandom(string[] clipNames, float minInterval, float volumeScale)
    {
        if (Instance == null || clipNames == null || clipNames.Length == 0) return;

        float now = Time.unscaledTime;
        // 그룹 전체의 마지막 재생 시각은 첫 번째 이름 자리에 함께 기록한다(별도 키를 만들 필요가 없다).
        string group_key = clipNames[0];
        if (Instance.last_played_time.TryGetValue(group_key, out float last) && now - last < minInterval) return;

        Instance.last_played_time[group_key] = now;
        Play(clipNames[Random.Range(0, clipNames.Length)], volumeScale);
    }

    private void EnsurePlaceholderClip(string clipName, float frequency, float duration)
    {
        if (clips.ContainsKey(clipName)) return;

        clips[clipName] = CreateBeepClip(clipName, frequency, duration);
    }

    /// <summary>정식 효과음이 생기기 전까지 쓸 임시 사인파 비프음을 코드로 생성한다.</summary>
    private static AudioClip CreateBeepClip(string clipName, float frequency, float duration)
    {
        int sampleRate = AudioSettings.outputSampleRate;
        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));

        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            // 끝에서 뚝 끊기는 클릭 노이즈가 나지 않도록 마지막 20% 구간을 선형으로 페이드아웃한다.
            float fadeWindow = Mathf.Max(1f, sampleCount * 0.2f);
            float fade = Mathf.Clamp01((sampleCount - i) / fadeWindow);
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.25f * fade;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
