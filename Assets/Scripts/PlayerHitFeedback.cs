using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 피격 시 카메라 흔들림·화면 비네트(빨간 테두리 플래시)·효과음을 트리거하는 컴포넌트.
/// <see cref="HitFlash"/>/<see cref="DiscEffectRuntime"/>과 동일하게
/// <see cref="PlayerRobotController.Awake"/>에서 자동 부착된다(2026-08-14, "피격당했는지
/// 알기 어렵다"는 사용자 피드백 대응).
///
/// <b>넉백은 여기 넣지 않는다.</b> <see cref="PlayerRobotController.FixedUpdate"/>가 매 프레임
/// <c>rb.linearVelocity</c>를 통째로 덮어쓰므로, 넉백 성분은 반드시 그 직후(같은 클래스 안)에서
/// 더해져야 한다. 별도 컴포넌트의 <c>FixedUpdate</c>로 분리하면 같은 GameObject 위 두
/// MonoBehaviour의 실행 순서가 Unity에 의해 보장되지 않아 특정 프레임의 넉백이 조용히 씹힐 수
/// 있다 - 그래서 넉백 필드/소비는 <see cref="PlayerRobotController"/> 안에 그대로 둔다.
/// </summary>
public class PlayerHitFeedback : MonoBehaviour
{
    [Header("카메라 흔들림")]
    [SerializeField] private float shakeDuration = 0.15f;
    [SerializeField] private float shakeMagnitude = 0.15f;

    [Header("화면 비네트")]
    [SerializeField] private Color vignetteColor = new Color(0.8f, 0f, 0f, 1f);
    [SerializeField] private float vignetteMaxAlpha = 0.35f;
    [SerializeField] private float vignetteFadeSeconds = 0.25f;

    [Header("효과음")]
    [SerializeField] private string hitSfxName = "Player_Hit";

    private CameraFollow cameraFollow;
    private Image vignetteImage;
    private float vignetteTimeLeft;

    private void Awake()
    {
        Camera mainCamera = Camera.main;
        cameraFollow = mainCamera != null ? mainCamera.GetComponent<CameraFollow>() : null;
        if (cameraFollow == null) cameraFollow = FindFirstObjectByType<CameraFollow>();

        vignetteImage = CreateVignetteImage();
    }

    /// <summary>공격을 받았을 때 <see cref="PlayerRobotController.TakeDamage"/>가 호출한다.</summary>
    public void OnHit(int damage, Vector3? attackerPosition)
    {
        cameraFollow?.Shake(shakeDuration, shakeMagnitude);
        FlashVignette();
        SFXManager.Play(hitSfxName);
    }

    private void Update()
    {
        if (vignetteImage == null || vignetteTimeLeft <= 0f) return;

        // 정비/상점 전환 중엔 즉시 숨긴다. combatHudObjects는 씬에 미리 존재하는 오브젝트만
        // 인스펙터에서 등록 가능한 배열이라, 런타임 생성 오브젝트는 스스로 체크해야 한다
        // (PlayerRobotController.Update()가 IsIntermission을 직접 체크하는 것과 동일한 관례).
        if (GameFlowManager.IsIntermission)
        {
            vignetteTimeLeft = 0f;
            SetVignetteAlpha(0f);
            return;
        }

        vignetteTimeLeft -= Time.deltaTime;
        float ratio = vignetteFadeSeconds > 0f ? Mathf.Clamp01(vignetteTimeLeft / vignetteFadeSeconds) : 0f;
        SetVignetteAlpha(vignetteMaxAlpha * ratio);
    }

    private void FlashVignette()
    {
        if (vignetteImage == null) return;

        vignetteTimeLeft = vignetteFadeSeconds;
        SetVignetteAlpha(vignetteMaxAlpha);
    }

    private void SetVignetteAlpha(float alpha)
    {
        Color c = vignetteColor;
        c.a = alpha;
        vignetteImage.color = c;
    }

    /// <summary>
    /// 캔버스 최상단에 풀스크린 이미지를 만든다(EquipmentDetailPopup.Create와 동일하게 앵커
    /// Stretch만 사용 - 이 프로젝트 캔버스는 ConstantPixelSize라 절대 픽셀을 쓰면 해상도마다
    /// 크기가 달라진다). uGUI 기본 셰이더/머티리얼은 항상 빌드에 포함되므로 HitFlash가 겪은
    /// 빌드 스트리핑 문제(Resources.Load 우회 필요)는 여기 해당하지 않는다.
    /// </summary>
    private Image CreateVignetteImage()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return null;

        var go = new GameObject("PlayerHitVignette", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(canvas.transform, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, 0f); // 평상시 완전 투명
        image.raycastTarget = false; // 입력을 막지 않음

        go.transform.SetAsLastSibling(); // 다른 HUD 위에 그려지도록
        return image;
    }
}
