using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 전체의 다국어 문자열 조회 창구(Phase 1, 2026-08-25).
/// <para>
/// <b>왜 Unity Localization 패키지를 안 쓰는가</b>: 공식 패키지는 (1) Addressables 의존성이 딸려오고,
/// (2) 씬에 배치된 <c>LocalizeStringEvent</c> 컴포넌트를 전제로 설계돼 있다. 그런데 이 프로젝트의 UI는
/// 대부분 씬이 아니라 <b>코드로 생성</b>되므로(<see cref="SettingsPanelUI"/>/<see cref="PauseMenuUI"/>/
/// <see cref="GameHUD"/> 등) 패키지의 이점을 거의 못 쓰고 무게만 는다. 또 "게임 데이터는 로컬 에셋이
/// 유일한 출처"라는 프로젝트 규칙과도 결이 다르다. 그래서 Resources 안의 JSON + static 조회로 간다.
/// </para>
/// <para>
/// <b>언어 추가 방법(중요)</b>: 언어를 하나 늘리는 비용은 <b>파일 1개 + 아래 <see cref="Supported"/>에
/// 한 줄</b>이 전부다. 언어별 <c>if</c> 분기를 코드 어디에도 만들지 말 것.
/// <list type="number">
/// <item><c>Assets/Resources/Localization/strings_&lt;코드&gt;.json</c> 을 추가한다.</item>
/// <item><see cref="Supported"/> 배열에 <see cref="LanguageInfo"/> 한 줄을 추가한다.</item>
/// <item>그 언어가 <b>라틴 문자권이 아니면</b> TMP 폰트 폴백을 추가한다(아래 폰트 주석 참고).</item>
/// </list>
/// </para>
/// <para>
/// <b>폰트 주의</b>: 현재 기본 폰트는 <c>Orbitron-Regular SDF</c>(라틴 전용, 글리프 207자)이고
/// 한글은 TMP 전역 폴백인 <c>NotoSansKR</c>이 받아준다. 일본어/중국어를 추가하면 그 언어 글리프를 가진
/// 폰트를 전역 폴백에 넣어야 하며, 동적 아틀라스 오염 이슈는 <see cref="FontAtlasMaintenance"/> 참고.
/// </para>
/// </summary>
public static class Loc
{
    /// <summary>지원 언어 하나의 메타데이터.</summary>
    public struct LanguageInfo
    {
        /// <summary>JSON 파일 이름에 쓰는 코드. 예) "ko" -&gt; strings_ko.json</summary>
        public readonly string Code;

        /// <summary>언어 선택 화면에 보여줄 이름. <b>그 언어 자신의 표기</b>로 적는다(한국어/English).</summary>
        public readonly string NativeName;

        /// <summary>첫 실행 시 자동 선택 판정에 쓰는 Unity 시스템 언어.</summary>
        public readonly SystemLanguage System;

        public LanguageInfo(string code, string nativeName, SystemLanguage system)
        {
            Code = code;
            NativeName = nativeName;
            System = system;
        }
    }

    /// <summary>
    /// 지원 언어 목록. <b>새 언어는 여기 한 줄만 추가</b>하면 언어 선택 화면에도 자동으로 나타난다.
    /// 첫 번째 항목이 기준 언어(= 번역 누락 시 최종 폴백)다.
    /// </summary>
    public static readonly LanguageInfo[] Supported =
    {
        new LanguageInfo("ko", "한국어", SystemLanguage.Korean),
        new LanguageInfo("en", "English", SystemLanguage.English),
    };

    /// <summary>기준 언어 코드. 번역이 비어 있으면 최종적으로 이 언어의 문자열을 쓴다.</summary>
    public const string BaseCode = "ko";

    private const string PrefsKey = "Comstock.Language";
    private const string ResourceFolder = "Localization/strings_";

    /// <summary>
    /// 언어가 바뀌었을 때 울린다. 구독자는 자기 화면 문구를 다시 그린다.
    /// <para>
    /// <b>절대 이 이벤트를 null로 밀지 말 것.</b> 이 프로젝트에서 <c>RunState.OnChanged</c>,
    /// <c>GameOverManager.OnGameOver</c>, <c>GameWinManager.OnGameWon</c> 세 곳이 전부 같은 버그를
    /// 겪었다 - 초기화 함수가 이벤트를 비우는데 구독자 Awake()와의 실행 순서가 보장되지 않아,
    /// 구독이 조용히 지워지고 이벤트가 안 울렸다. 구독자가 각자 OnDestroy()에서 해제한다.
    /// </para>
    /// </summary>
    public static event System.Action OnLanguageChanged;

    private static Dictionary<string, string> current_table;   // 현재 언어 문자열
    private static Dictionary<string, string> base_table;      // 기준 언어(ko) 문자열 - 폴백용
    private static string current_code;
    private static bool initialized;

    /// <summary>현재 언어 코드("ko"/"en"…). 아직 초기화 전이면 초기화하고 돌려준다.</summary>
    public static string CurrentCode
    {
        get
        {
            EnsureInitialized();
            return current_code;
        }
    }

    /// <summary>현재 언어의 메타데이터.</summary>
    public static LanguageInfo Current => Find(CurrentCode);

    /// <summary>
    /// 씬 로드 전에 한 번 초기화한다. 이렇게 해두면 어떤 스크립트의 Awake()에서 <see cref="T(string)"/>를
    /// 불러도 이미 테이블이 준비돼 있다(<c>GameDataManager</c>가 Awake에서 동기 로드를 끝내는 것과 같은 관례).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BootstrapOnLoad()
    {
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        base_table = LoadTable(BaseCode);
        string saved = PlayerPrefs.GetString(PrefsKey, string.Empty);
        string code = IsSupported(saved) ? saved : DetectSystemLanguage();

        current_code = code;
        current_table = code == BaseCode ? base_table : LoadTable(code);
    }

    /// <summary>
    /// 첫 실행일 때 OS 언어로 자동 선택한다. 지원 목록에 없는 OS 언어면 기준 언어로 떨어진다.
    /// </summary>
    private static string DetectSystemLanguage()
    {
        SystemLanguage sys = Application.systemLanguage;
        for (int i = 0; i < Supported.Length; i++)
        {
            if (Supported[i].System == sys) return Supported[i].Code;
        }
        return BaseCode;
    }

    /// <summary>
    /// 언어를 바꾸고 <see cref="OnLanguageChanged"/>를 울린다. 선택값은 PlayerPrefs에 저장돼
    /// 다음 실행에도 유지된다(<see cref="MusicManager.Volume"/>과 같은 패턴).
    /// 이미 그 언어면 아무 일도 하지 않는다.
    /// </summary>
    public static void SetLanguage(string code)
    {
        EnsureInitialized();
        if (!IsSupported(code) || code == current_code) return;

        current_code = code;
        current_table = code == BaseCode ? base_table : LoadTable(code);

        PlayerPrefs.SetString(PrefsKey, code);
        PlayerPrefs.Save();

        if (OnLanguageChanged != null) OnLanguageChanged();
    }

    /// <summary>
    /// 키에 해당하는 문자열을 돌려준다.
    /// <para>폴백 순서: <b>현재 언어 → 기준 언어(ko) → 키 문자열 그대로</b>.
    /// 마지막 단계에서 키가 화면에 그대로 노출되는 것은 의도한 것이다 - 번역 누락을
    /// 조용히 빈칸으로 넘기지 않고 즉시 눈에 띄게 하려는 목적이다.</para>
    /// </summary>
    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        EnsureInitialized();

        string value;
        if (current_table != null && current_table.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            return value;
        if (base_table != null && base_table.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            return value;
        return key;
    }

    /// <summary>
    /// 서식 인자가 있는 문자열. 예) <c>Loc.T("hud.wave", 5)</c> → "웨이브 5".
    /// <para>번역문의 <c>{0}</c> 자리 수가 인자 수와 안 맞으면 서식이 실패하는데, 그때는 예외를
    /// 터뜨리지 않고 서식 전 원문을 돌려준다 - 번역 오타 하나가 게임을 멈추면 안 되기 때문이다.</para>
    /// </summary>
    public static string T(string key, params object[] args)
    {
        string format = T(key);
        if (args == null || args.Length == 0) return format;

        try
        {
            return string.Format(format, args);
        }
        catch (System.FormatException)
        {
            Debug.LogWarning($"[Loc] 서식 실패 - 키 '{key}', 원문 '{format}', 인자 {args.Length}개");
            return format;
        }
    }

    /// <summary>현재 언어 또는 기준 언어에 그 키가 실제로 있는지. 진단·검증용.</summary>
    public static bool Has(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        EnsureInitialized();
        return (current_table != null && current_table.ContainsKey(key))
            || (base_table != null && base_table.ContainsKey(key));
    }

    /// <summary>현재 언어 테이블에 실제로 담긴 문자열 수. 로딩이 됐는지 확인하는 용도.</summary>
    public static int LoadedCount => current_table == null ? 0 : current_table.Count;

    public static bool IsSupported(string code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        for (int i = 0; i < Supported.Length; i++)
        {
            if (Supported[i].Code == code) return true;
        }
        return false;
    }

    private static LanguageInfo Find(string code)
    {
        for (int i = 0; i < Supported.Length; i++)
        {
            if (Supported[i].Code == code) return Supported[i];
        }
        return Supported[0];
    }

    /// <summary>
    /// <c>Assets/Resources/Localization/strings_&lt;코드&gt;.json</c> 을 읽어 사전으로 만든다.
    /// 파일이 없거나 깨져도 게임이 멈추면 안 되므로 빈 사전을 돌려주고 경고만 남긴다
    /// (그러면 <see cref="T(string)"/>이 기준 언어로, 그것도 없으면 키로 폴백한다).
    /// </summary>
    private static Dictionary<string, string> LoadTable(string code)
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourceFolder + code);
        if (asset == null)
        {
            Debug.LogWarning($"[Loc] 언어 파일 없음: Resources/{ResourceFolder}{code}.json");
            return new Dictionary<string, string>();
        }

        try
        {
            var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(asset.text);
            return parsed ?? new Dictionary<string, string>();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Loc] 언어 파일 파싱 실패 ({code}): {e.Message}");
            return new Dictionary<string, string>();
        }
    }
}
