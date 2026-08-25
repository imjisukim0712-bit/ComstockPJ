using TMPro;
using UnityEngine;

/// <summary>
/// 씬에 배치된 고정 문구 하나를 번역과 연결하는 컴포넌트(Phase 1, 2026-08-25).
/// <para>
/// <b>이 프로젝트에서 이게 필요한 대상은 소수다.</b> UI 대부분은 코드로 만들어지므로(그쪽은
/// <see cref="Loc.T(string)"/>를 직접 부르고 <see cref="Loc.OnLanguageChanged"/>에 다시 그리기를 물린다),
/// 이 컴포넌트는 <c>Ground01</c>/<c>Title</c> 씬에 <b>미리 글자가 박혀 있는 정적 라벨</b>에만 붙인다.
/// 예: "품목", "잠금", "정비 완료", "타이틀로", "등급 · 분류".
/// </para>
/// <para>
/// <b>런타임에 코드가 덮어쓰는 텍스트에는 붙이면 안 된다.</b> 씬의 <c>WAVE 00 / 20</c>,
/// <c>레벨 1  0/10</c> 같은 값은 매 프레임 코드가 다시 채우는 placeholder라, 여기에 이 컴포넌트를
/// 붙이면 서로 덮어쓰며 깜빡인다. 그런 문구는 값을 채우는 코드 쪽에서 <see cref="Loc.T(string)"/>를 쓴다.
/// </para>
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("번역 키. Resources/Localization/strings_*.json 의 키와 같아야 한다. " +
             "번역이 없으면 한국어로, 한국어도 없으면 키 문자열이 그대로 화면에 보인다(누락을 눈에 띄게 하려는 의도).")]
    [SerializeField] private string key;

    private TMP_Text label;

    /// <summary>코드에서 키를 바꿔 끼우고 싶을 때. 설정하면 즉시 다시 그린다.</summary>
    public string Key
    {
        get => key;
        set
        {
            key = value;
            Apply();
        }
    }

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        Loc.OnLanguageChanged += Apply;
        Apply();
    }

    /// <summary>
    /// 구독 해제는 반드시 자기가 한다. <see cref="Loc.OnLanguageChanged"/>는 static 이벤트라
    /// 해제하지 않으면 파괴된 오브젝트가 계속 붙잡혀 있는다(프로젝트 규칙 - static 이벤트를
    /// 관리자 쪽에서 null로 미는 대신 구독자가 스스로 해제한다).
    /// </summary>
    private void OnDisable()
    {
        Loc.OnLanguageChanged -= Apply;
    }

    private void Apply()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (label == null || string.IsNullOrEmpty(key)) return;
        label.text = Loc.T(key);
    }
}
