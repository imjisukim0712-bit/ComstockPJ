using UnityEngine;

/// <summary>
/// 카메라가 대상(플레이어)을 따라다닌다.
/// 카메라를 플레이어의 자식으로 두면 플레이어의 Scale(좌우 반전 포함)이
/// 카메라에까지 곱해져서 화면이 확대되거나 뒤집힌다. 그래서 부모-자식 관계를
/// 끊고 이 스크립트로 위치만 따라가게 한다.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Tooltip("따라갈 대상. 비어 있으면 Player 태그를 가진 오브젝트를 자동으로 찾는다")]
    [SerializeField] private Transform target;

    [Tooltip("대상 기준 카메라 오프셋 (Z는 카메라 거리)")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -15f);

    [Tooltip("0이면 즉시 따라감. 값이 클수록 부드럽게 따라간다")]
    [SerializeField] private float smoothTime = 0f;

    [Tooltip("Z 위치를 오프셋 값으로 고정 (2D에서 카메라 거리 유지)")]
    [SerializeField] private bool lockZ = true;

    [Tooltip("카메라가 맵(배경 스프라이트) 밖을 비추지 않도록 위치를 제한한다. " +
             "끄면 플레이어가 맵 가장자리로 갔을 때 배경이 없는 빈 공간이 보인다")]
    [SerializeField] private bool clampToMap = true;

    private Vector3 velocity;
    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
            else Debug.LogWarning("CameraFollow: 따라갈 대상을 찾을 수 없습니다. Target을 직접 지정하세요.");
        }
    }

    private void Start()
    {
        if (target != null) transform.position = DesiredPosition();
    }

    // 이동 처리가 모두 끝난 뒤에 카메라를 옮겨야 한 프레임 밀리지 않는다
    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = DesiredPosition();
        transform.position = smoothTime > 0f
            ? Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime)
            : desired;
    }

    private Vector3 DesiredPosition()
    {
        // target.position은 월드 좌표라 대상의 Scale에 영향받지 않는다
        Vector3 p = target.position + offset;
        if (lockZ) p.z = offset.z;

        // 플레이어는 맵 가장자리까지 갈 수 있지만 카메라는 거기서 멈춘다(그래야 배경 밖
        // 빈 공간이 화면에 들어오지 않는다). 이때는 플레이어가 화면 중앙에서 벗어나 보이는데,
        // 2D 게임의 정석적인 처리라 의도된 동작이다.
        if (clampToMap && TryGetViewHalfExtents(out float halfWidth, out float halfHeight))
        {
            Vector3 clamped = MapBounds.ClampCameraCenter(p, halfWidth, halfHeight);
            p.x = clamped.x;
            p.y = clamped.y;
        }

        return p;
    }

    /// <summary>
    /// 이 카메라가 z=0 평면에서 보여주는 범위의 절반 크기.
    /// (EnemySpawner.TryGetCameraHalfExtents와 같은 계산이지만, 저쪽은 Camera.main 기준이라
    ///  이 컴포넌트가 붙은 카메라를 쓰도록 따로 둔다.)
    /// </summary>
    private bool TryGetViewHalfExtents(out float halfWidth, out float halfHeight)
    {
        halfWidth = 0f;
        halfHeight = 0f;
        if (cam == null) return false;

        if (cam.orthographic)
        {
            halfHeight = cam.orthographicSize;
        }
        else
        {
            float distanceToPlane = Mathf.Abs(transform.position.z); // 게임 평면은 z=0
            if (distanceToPlane <= 0.01f) return false;
            halfHeight = distanceToPlane * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        halfWidth = halfHeight * cam.aspect;
        return halfHeight > 0.01f;
    }
}
