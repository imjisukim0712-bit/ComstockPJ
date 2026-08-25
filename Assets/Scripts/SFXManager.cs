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

    private const string PlayerHitClipName = "Player_Hit";

    /// <summary>UI 버튼 클릭음(2026-08-25 사용자 제공 <c>23_ui_click.wav</c>).
    /// 개별 버튼마다 리스너를 다는 대신 <see cref="PlayUiClickIfButtonPressed"/>가 전역으로
    /// 감지해 재생한다 - 이 프로젝트 UI는 대부분 코드로 만들어지고 버튼이 화면마다 새로
    /// 생성되므로, 생성부마다 붙이면 반드시 빠뜨리는 곳이 생긴다.</summary>
    public const string UiClickClipName = "UI_Click";

    /// <summary>AI 코어 레벨업 효과음(2026-08-25 사용자 제공 <c>26_core_upgrade.wav</c>).
    /// <see cref="AiCoreManager"/>가 레벨업 이펙트와 같은 지점에서 한 번 재생한다.</summary>
    public const string LevelUpClipName = "LevelUp";

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
