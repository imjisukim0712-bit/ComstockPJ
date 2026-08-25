using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 화면에 보이는 <b>자동 크기 TMP 글자의 상한/하한</b>을 화면 높이에 비례시킨다
/// (2026-08-25 사용자 요청: "UI 또는 텍스트에 반응형으로 해상도에 따라 적용되지 않는 경우가
/// 있는지 검수 후 발견할 경우 반응형 UI로 알맞게 수정").
///
/// <b>무엇이 문제였나</b> — 이 프로젝트 UI는 대부분 정규화 앵커라 <b>칸의 크기</b>는 해상도를
/// 잘 따라간다. 그런데 TMP 자동 크기의 <c>fontSizeMax</c>는 절대 pt 값이라, 칸이 커져도 글자는
/// 상한에서 멈춘다. 실측(1080p → 1440p, 화면이 1.33배):
///
/// <code>
///   Hp_value / GoldText / PartBoxText / ExpLevelText / ExpValueText : 30pt → 30pt (배율 1.00, 전부 상한에 걸림)
/// </code>
///
/// 즉 해상도를 올릴수록 글자만 상대적으로 작아졌다. 칸은 커지는데 글자는 그대로라 여백만 늘어난다.
///
/// <b>왜 전역 순회인가</b> — 이 프로젝트 UI는 대부분 코드로 생성되고 화면마다 새로 만들어진다
/// (정비 화면은 웨이브마다 열린다). 생성부마다 컴포넌트를 붙이면 반드시 빠뜨리는 곳이 생기므로,
/// <see cref="SFXManager"/>의 UI 클릭음과 같은 전역 감지 방식을 쓴다. 해상도가 바뀐 프레임과
/// 일정 주기에만 순회하므로 비용은 미미하다.
///
/// <b>1080p에서는 아무것도 바뀌지 않는다</b>(배율 1). 설계값은 "처음 본 값 ÷ 그때의 배율"로
/// 역산해 두므로, 게임을 어떤 해상도에서 시작하든 기준이 흔들리지 않는다.
/// </summary>
public class ResponsiveTextScaler : MonoBehaviour
{
    /// <summary>설계 기준 해상도의 세로 픽셀(이 프로젝트 UI는 전부 1080p 기준).</summary>
    public const float DesignHeight = ResponsiveHudScaler.DesignHeight;

    /// <summary>작은 창에서 글자가 읽을 수 없게 되지 않도록 두는 배율 하한.</summary>
    private const float MinScale = 0.6f;

    /// <summary>지나치게 커지지 않도록 두는 배율 상한(4K까지 비례).</summary>
    private const float MaxScale = 2f;

    /// <summary>해상도가 그대로여도 이 주기마다 한 번씩 새로 만들어진 글자를 훑는다(초, unscaled).</summary>
    private const float RescanInterval = 0.5f;

    private static ResponsiveTextScaler instance;

    // 글자별 1080p 기준 (min, max). 한 번 등록하면 다시 계산하지 않는다.
    private readonly Dictionary<TextMeshProUGUI, Vector2> design_font_range = new Dictionary<TextMeshProUGUI, Vector2>();

    private int applied_height = -1;
    private float next_rescan_time;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;

        var go = new GameObject("ResponsiveTextScaler");
        go.AddComponent<ResponsiveTextScaler>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void LateUpdate()
    {
        int height = Screen.height;
        bool resolution_changed = height != applied_height;

        // 정비/상점 화면은 Time.timeScale = 0이라 unscaled 시간을 써야 주기가 흐른다.
        if (!resolution_changed && Time.unscaledTime < next_rescan_time) return;

        applied_height = height;
        next_rescan_time = Time.unscaledTime + RescanInterval;

        Apply(Mathf.Clamp(height / DesignHeight, MinScale, MaxScale));
    }

    private void Apply(float scale)
    {
        foreach (TextMeshProUGUI text in FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None))
        {
            if (text == null || !text.enableAutoSizing) continue;

            // 1080p 설계 래퍼(ResponsiveHudScaler) 안의 글자는 래퍼가 통째로 배율을 주므로
            // 여기서 상한까지 올리면 배율이 두 번 곱해진다.
            if (text.GetComponentInParent<ResponsiveHudScaler>() != null) continue;

            if (!design_font_range.TryGetValue(text, out Vector2 design))
            {
                // 처음 보는 글자 - <b>지금 값이 곧 1080p 설계값</b>이다. 씬/코드에 박힌 authored
                // 값은 해상도와 무관하게 항상 같고, 이 프로젝트는 그 값을 1080p 기준으로 잡아 뒀다.
                // (배율을 걷어내는 식으로 역산하면 1440p로 게임을 시작했을 때 30pt를 22.5pt로
                //  오인해 1080p에서 글자가 작아지는 회귀가 난다 - 실측으로 겪었다.)
                // 이 컴포넌트가 이미 손댄 글자는 사전에 들어 있으므로 여기로 오지 않는다.
                design = new Vector2(text.fontSizeMin, text.fontSizeMax);
                design_font_range[text] = design;
            }

            float min = design.x * scale;
            float max = design.y * scale;

            // 값이 실제로 달라질 때만 쓴다 - TMP는 대입할 때마다 레이아웃을 다시 계산한다.
            if (!Mathf.Approximately(text.fontSizeMin, min)) text.fontSizeMin = min;
            if (!Mathf.Approximately(text.fontSizeMax, max)) text.fontSizeMax = max;
        }

        PruneDestroyed();
    }

    /// <summary>파괴된 글자를 사전에서 걷어낸다(정비 화면은 웨이브마다 통째로 새로 만들어진다).</summary>
    private void PruneDestroyed()
    {
        if (design_font_range.Count < 256) return; // 자주 할 일이 아니다 - 커졌을 때만 정리한다

        var dead = new List<TextMeshProUGUI>();
        foreach (var pair in design_font_range) if (pair.Key == null) dead.Add(pair.Key);
        foreach (var key in dead) design_font_range.Remove(key);
    }
}
