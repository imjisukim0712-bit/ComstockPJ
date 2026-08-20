using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 피해를 입힌/입은 위치에 잠깐 떴다가 좌우로 떨어지며 사라지는 데미지 숫자 팝업.
/// 2026-08-20 사용자 요청("적이나 아군 피격시에 데미지 몇 터지는지 잠깐 보이게") +
/// 후속 요청("좌표 1~10픽셀씩 랜덤으로 어긋나게" / "올라가면서 사라지는게 아닌 왼쪽이나
/// 오른쪽으로 떨어지면서 사라지도록") 반영판.
///
/// <see cref="DashGaugeUI"/>/<see cref="PlayerHitFeedback"/>와 같은 방식으로 동작한다 - 씬에
/// 배치하지 않고 코드로 만들며, 캔버스가 ConstantPixelSize이므로 <c>cam.WorldToScreenPoint</c>로
/// 화면 좌표를 구해 <c>anchoredPosition</c>에 그대로 넣는다(해상도가 바뀌어도 항상 정확한
/// 화면 위치에 뜬다). 팝업 하나당 GameObject 하나를 새로 만들고 수명이 끝나면 스스로 파괴한다
/// (RewardPickup과 같은 "일회용 오브젝트" 패턴).
///
/// <b>치명타는 색이 다르고(주황) 글자가 크며, 옆에 아이콘이 함께 뜬다</b>(사용자 추가 요청).
/// 아이콘은 전용 아트가 없어 <see cref="PartIconLibrary.BuildPlaceholder"/>와 같은 방식으로
/// 코드에서 흰색 실루엣을 그려 캐시해 두고, 표시할 때 색만 입힌다.
/// </summary>
public class DamageNumberUI : MonoBehaviour
{
    private const float Lifetime = 0.7f;
    private const float SideSpeed = 1.0f;      // 좌우로 흐르는 속도(월드 유닛/초)
    private const float FallGravity = 4.0f;    // 아래로 떨어지는 가속도(월드 유닛/초^2) - 낙하 느낌
    private const float PopInSeconds = 0.08f;
    private const float FadeStartRatio = 0.45f; // 이 지점까지는 완전히 보이고, 그 뒤로 페이드

    // 2026-08-20 사용자 요청 - 여러 발이 겹쳐도 완전히 포개지지 않도록 화면 좌표 기준
    // 1~10픽셀씩 랜덤한 방향으로 어긋나게 한다(월드 유닛이 아니라 픽셀 그대로).
    private const float MinPixelJitter = 1f;
    private const float MaxPixelJitter = 10f;

    private const float NormalFontSize = 30f;
    private const float CritFontSize = 40f;

    private static readonly Color DealtColor = new Color(1f, 1f, 1f, 1f);       // 플레이어가 입힌 피해(일반)
    private static readonly Color CritColor = new Color(1f, 0.69f, 0f, 1f);     // 치명타(주황, Unique 등급과 같은 색)
    private static readonly Color TakenColor = new Color(1f, 0.35f, 0.35f, 1f); // 플레이어가 입은 피해

    private static Canvas cached_canvas;
    private static Sprite crit_icon;

    private RectTransform root;
    private TextMeshProUGUI label;
    private Image icon;
    private Camera cam;

    private Vector3 world_pos;
    private Vector2 pixel_jitter;
    private float elapsed;
    private float side;         // -1 = 왼쪽으로, +1 = 오른쪽으로 떨어진다(스폰 시 무작위 확정)
    private float fall_velocity; // 매 프레임 FallGravity만큼 누적 - 갈수록 빠르게 떨어진다

    /// <summary>플레이어가 적에게 입힌 피해. isCrit이면 색이 다르고 아이콘이 함께 뜬다.</summary>
    public static void ShowDealt(Vector3 worldPosition, float damage, bool isCrit)
    {
        Spawn(worldPosition, damage, isCrit ? CritColor : DealtColor, isCrit);
    }

    /// <summary>적이 플레이어에게 입힌 피해(치명타 개념이 없어 항상 일반 표시).</summary>
    public static void ShowTaken(Vector3 worldPosition, float damage)
    {
        Spawn(worldPosition, damage, TakenColor, false);
    }

    /// <summary>씬 재시작 시 이전 판의 캔버스 참조가 남지 않도록 캐시를 비운다.</summary>
    public static void ResetCache() => cached_canvas = null;

    private static void Spawn(Vector3 worldPosition, float damage, Color color, bool isCrit)
    {
        Canvas canvas = ResolveCanvas();
        if (canvas == null) return;

        var go = new GameObject("DamageNumber", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        var popup = go.AddComponent<DamageNumberUI>();
        popup.Build((RectTransform)go.transform, worldPosition, damage, color, isCrit);
    }

    private void Build(RectTransform rootRect, Vector3 worldPosition, float damage, Color color, bool isCrit)
    {
        root = rootRect;
        cam = Camera.main;

        world_pos = worldPosition + new Vector3(0f, 0.25f, 0f); // 맞은 지점보다 살짝 위에서 시작

        // 화면 좌표 기준 1~10픽셀 랜덤 오프셋(방향도 무작위) - 세계 좌표가 아니라 화면에 그려질
        // 때의 최종 픽셀 위치에 더해지므로 카메라 줌/해상도와 무관하게 항상 1~10px만큼만 어긋난다.
        float jitter_angle = Random.Range(0f, Mathf.PI * 2f);
        float jitter_magnitude = Random.Range(MinPixelJitter, MaxPixelJitter);
        pixel_jitter = new Vector2(Mathf.Cos(jitter_angle), Mathf.Sin(jitter_angle)) * jitter_magnitude;

        // 왼쪽/오른쪽 중 한 방향으로만 떨어진다(좌우로 흔들리지 않고 한쪽으로 흐른다).
        side = Random.value < 0.5f ? -1f : 1f;
        fall_velocity = 0f;

        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(160f, 60f);

        const float iconSize = 26f;
        const float iconGap = 4f;
        float textOffsetX = 0f;

        if (isCrit)
        {
            var iconGo = new GameObject("CritIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(root, false);
            icon = iconGo.GetComponent<Image>();
            icon.sprite = ResolveCritIcon();
            icon.color = color;
            icon.raycastTarget = false;

            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(iconGap, 0f);
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);

            textOffsetX = iconGap + iconSize + iconGap;
        }

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(root, false);
        label = textGo.GetComponent<TextMeshProUGUI>();
        label.text = FormatDamage(damage);
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.alignment = isCrit ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline;
        label.raycastTarget = false;
        label.enableAutoSizing = false;
        label.fontSize = isCrit ? CritFontSize : NormalFontSize;

        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(textOffsetX, 0f);
        textRect.offsetMax = Vector2.zero;

        UpdateScreenPosition();
        SetAlpha(0f); // 첫 프레임은 팝인 전이라 투명하게 시작
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= Lifetime)
        {
            Destroy(gameObject);
            return;
        }

        // 왼쪽 또는 오른쪽으로 흐르면서 중력 가속으로 떨어진다("떨어지면서 사라지도록").
        fall_velocity += FallGravity * Time.deltaTime;
        world_pos += new Vector3(side * SideSpeed * Time.deltaTime, -fall_velocity * Time.deltaTime, 0f);
        UpdateScreenPosition();

        float scale = elapsed < PopInSeconds ? Mathf.Lerp(0.5f, 1f, elapsed / PopInSeconds) : 1f;
        root.localScale = Vector3.one * scale;

        float lifeRatio = elapsed / Lifetime;
        float alpha = lifeRatio < FadeStartRatio
            ? 1f
            : 1f - (lifeRatio - FadeStartRatio) / (1f - FadeStartRatio);
        SetAlpha(Mathf.Clamp01(alpha));
    }

    private void UpdateScreenPosition()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || root == null) return;

        Vector3 screenPoint = cam.WorldToScreenPoint(world_pos);
        root.anchoredPosition = new Vector2(screenPoint.x, screenPoint.y) + pixel_jitter;
    }

    private void SetAlpha(float alpha)
    {
        if (label != null)
        {
            Color c = label.color;
            c.a = alpha;
            label.color = c;
        }

        if (icon != null)
        {
            Color c = icon.color;
            c.a = alpha;
            icon.color = c;
        }
    }

    /// <summary>0.##로 소수점까지 보여준다(스탯 표시와 같은 관례 - 반올림하면 소수 피해가 숨는다).</summary>
    private static string FormatDamage(float damage) => damage.ToString("0.##");

    private static Canvas ResolveCanvas()
    {
        if (cached_canvas != null) return cached_canvas;
        cached_canvas = FindFirstObjectByType<Canvas>();
        return cached_canvas;
    }

    // ── 치명타 아이콘 (코드 생성, PartIconLibrary.BuildPlaceholder와 같은 방식) ─────────────

    private static Sprite ResolveCritIcon()
    {
        if (crit_icon != null) return crit_icon;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "DamageNumber_CritIcon"
        };

        var pixels = new Color32[size * size];
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float outerRadius = size * 0.46f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);

                // 4방향으로 뻗는 별(sparkle) 모양: r(θ) = R x (0.30 + 0.70 x |cos(2θ)|^0.6).
                // 극좌표 한 줄로 표현되는 꽃/별 곡선이라 Rect/Disc처럼 별도 헬퍼 없이 이 안에서 끝낸다.
                float petal = outerRadius * (0.30f + 0.70f * Mathf.Pow(Mathf.Abs(Mathf.Cos(2f * angle)), 0.6f));
                bool filled = r <= petal;

                pixels[y * size + x] = filled ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);

        crit_icon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return crit_icon;
    }
}
