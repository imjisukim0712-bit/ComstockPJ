using UnityEngine;

/// <summary>
/// 1080p 기준으로 authored된 HUD 묶음을 <b>통째로 균등 축소</b>해 어떤 해상도에서도 1080p와
/// 같은 모습으로 보이게 한다(2026-08-25 사용자 요청: "인게임 상단 웨이브 및 남은 초 표시 UI
/// 또한 반응형 UI로 적용").
///
/// <b>왜 필요한가</b> — 이 프로젝트의 Canvas는 <c>ConstantPixelSize</c>다. 앵커가 정규화(0~1)인
/// 요소는 <b>위치</b>만 화면 비율을 따라가고, <c>sizeDelta</c>·<c>anchoredPosition</c>에 들어간
/// 절대 픽셀과 TMP 자동 크기의 하한은 화면이 작아져도 그대로 남는다. 실측(720x405)에서 상단
/// 웨이브 배경이 288x103px, 즉 화면 폭의 40%·높이의 25%를 차지했다(1080p 설계에서는 15%·9.5%).
///
/// <b>왜 요소별로 줄이지 않는가</b> — 실제로 그렇게 만들어 봤더니 배경과 글자가 각각 다른 앵커·
/// 오프셋으로 맞춰져 있어서 개별로 줄이는 순간 정렬이 깨졌다(시간 글자가 배경 밖으로 빠져나오고
/// 웨이브 글자가 사라졌다). AI 코어 카드 화면이 <c>DesignRoot_1080p</c>로 "1080p 설계를 균등
/// 축소"하는 것과 같은 이유이며(프로젝트 안내 참고), 여기서도 같은 방법을 쓴다 - 묶음 안의
/// 비율·간격·글자 크기가 전부 그대로 유지된다.
///
/// <b>왜 Canvas Scaler를 바꾸지 않는가</b> — 모든 화면(정비/상점/AI 코어/타이틀)이
/// ConstantPixelSize를 전제로 픽셀 단위 튜닝돼 있어 스케일 모드를 바꾸면 전 화면이 한꺼번에 틀어진다.
///
/// <b>붙이는 방법</b> — 씬을 고치지 않고 <see cref="Wrap"/>로 런타임에 래퍼를 만들어 감싼다
/// (<see cref="GameHUD"/>가 Awake에서 호출).
/// </summary>
[DisallowMultipleComponent]
public class ResponsiveHudScaler : MonoBehaviour
{
    /// <summary>설계 기준 해상도의 세로 픽셀. 이 프로젝트 UI는 전부 1080p 기준으로 잡혀 있다.</summary>
    public const float DesignHeight = 1080f;

    /// <summary>너무 작은 창에서 글자가 읽을 수 없게 되지 않도록 두는 배율 하한.</summary>
    private const float MinScale = 0.45f;

    /// <summary>4K(2160p)까지는 1080p와 같은 화면 비율을 유지한다. 그 위로는 더 키우지 않는다.</summary>
    private const float MaxScale = 2f;

    private RectTransform rect;
    private int applied_height = -1;

    /// <summary>
    /// 대상들을 하나의 래퍼 아래로 옮기고, 그 래퍼에 화면 높이 비례 배율을 준다.
    /// 래퍼는 부모와 같은 영역을 덮고 pivot만 <paramref name="pivot"/>으로 두므로,
    /// 축소해도 그 기준점(상단 중앙 등)에 붙어 있는다.
    ///
    /// 이미 감싸져 있으면(같은 이름의 래퍼가 있으면) 그 아래로 합류시키기만 한다 - 여러 번 불려도
    /// 래퍼가 중첩되지 않는다.
    /// </summary>
    public static void Wrap(string wrapperName, Vector2 pivot, params Component[] targets)
    {
        if (targets == null || targets.Length == 0) return;

        Transform parent = null;
        foreach (Component target in targets)
        {
            if (target == null) continue;
            parent = target.transform.parent;
            if (parent != null) break;
        }
        if (parent == null) return;

        Transform existing = parent.Find(wrapperName);
        RectTransform wrapper = existing != null ? existing as RectTransform : null;

        if (wrapper == null)
        {
            var go = new GameObject(wrapperName, typeof(RectTransform));
            wrapper = (RectTransform)go.transform;
            wrapper.SetParent(parent, false);

            // <b>래퍼는 1920x1080 고정 크기</b>여야 한다(AI 코어 카드의 DesignRoot_1080p와 같다).
            // 부모 영역(= 화면 크기)을 그대로 덮게 두면 자식의 정규화 앵커가 화면과 함께 커지고
            // 거기에 래퍼 배율까지 곱해져 <b>이중 확대</b>가 된다 - 실측에서 1080p → 1440p일 때
            // 글자가 1.33배가 아니라 2.05배가 됐다. 고정 크기로 두면 자식들은 1080p 설계 좌표
            // 그대로 배치되고, 화면 적응은 오로지 localScale이 담당한다.
            wrapper.anchorMin = pivot;
            wrapper.anchorMax = pivot;
            wrapper.pivot = pivot;
            wrapper.sizeDelta = new Vector2(1920f, DesignHeight);
            wrapper.anchoredPosition = Vector2.zero;

            go.AddComponent<ResponsiveHudScaler>();
        }

        // <b>원래 형제 순서를 유지해야 한다.</b> UI는 형제 순서가 곧 그리기 순서라서, 넘겨받은
        // 배열 순서대로 옮기면 9-slice 배경이 글자보다 뒤(= 더 나중에, 즉 위에) 놓여 글자를 통째로
        // 덮어버린다 - 실제로 1080p에서 배너만 남고 웨이브·시간 글자가 전부 사라졌다.
        var ordered = new System.Collections.Generic.List<Transform>();
        foreach (Component target in targets)
        {
            if (target == null || target.transform.parent == wrapper) continue;
            ordered.Add(target.transform);
        }
        ordered.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));

        foreach (Transform target in ordered)
        {
            // <b>worldPositionStays = false</b>여야 한다. true로 옮기면 RectTransform의
            // anchoredPosition이 월드 좌표 기준으로 다시 계산되는데, 배경과 글자의 앵커가 서로
            // 달라서(배경 0.45~0.56 / 글자 0.43~0.58) 값이 제각각 틀어진다. 래퍼가 부모와 완전히
            // 같은 영역이므로 앵커·오프셋을 그대로 들고 오면 보이던 자리가 그대로 유지된다.
            target.SetParent(wrapper, false);
            target.SetAsLastSibling(); // 정렬해 둔 순서대로 차례로 쌓는다
        }
    }

    private void Awake() => rect = GetComponent<RectTransform>();

    private void LateUpdate()
    {
        int height = Screen.height;
        if (height == applied_height) return; // 해상도가 바뀐 프레임에만 일한다
        applied_height = height;

        if (rect == null) return;

        float scale = Mathf.Clamp(height / DesignHeight, MinScale, MaxScale);
        rect.localScale = new Vector3(scale, scale, 1f);
    }
}
