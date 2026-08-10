using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 피해를 입은 유닛을 잠깐 단색(기본 흰색)으로 물들이는 피격 연출.
/// 좀비/차저/보스(<see cref="EnemyUnit"/>)와 플레이어(<see cref="PlayerRobotController"/>)가
/// 피해를 받는 순간 <see cref="Play"/>를 호출한다.
///
/// <b>SpriteRenderer.color로는 흰색을 만들 수 없다</b> - 색은 곱셈이라 밝아지기만 하고
/// 원래 색상이 그대로 비친다. 그래서 전용 셰이더(`Comstock/SpriteFlash`)를 쓰고
/// `_FlashAmount`만 0↔1로 움직인다. 이 값이 0이면 기본 스프라이트와 완전히 동일하게
/// 보이므로 평상시 화면에는 아무 영향이 없다.
///
/// <b>머티리얼은 반드시 렌더러마다 따로 만든다 — 이것이 "파츠마다 다른 이미지가 그려지는"
/// 버그의 원인이었다(2026-08-10).</b>
/// 예전에는 드로우콜을 아끼려고 static 머티리얼 <b>하나</b>를 로봇 파츠 7개와 모든 좀비가
/// 공유했다. SpriteRenderer는 자기 스프라이트의 텍스처를 "렌더러별 데이터"
/// (`[PerRendererData] _MainTex`)로 셰이더에 넘기는데, <b>같은 머티리얼 인스턴스를 쓰는
/// 렌더러들은 하나의 배치(batch)로 묶이고 그 배치에는 텍스처가 한 장만 바인딩된다.</b>
/// 그래서 머리에 신발이 그려지거나(=배치가 Foot 텍스처를 물었을 때) 좀비 공격 프레임이
/// 로봇 몸에 나타났다(좀비도 같은 머티리얼을 공유했다).
///
/// 이 버그가 세 번이나 잡히지 않은 이유:
/// - <b>`SpriteRenderer.sprite`는 처음부터 끝까지 정상이었다</b> - 잘못된 텍스처는 렌더 시점에
///   바인딩되므로, 스프라이트 필드/에셋 GUID를 검사하는 방식으로는 절대 보이지 않는다.
/// - <b>첫 피격 뒤에 정상으로 보이던 것도 이 구조 때문이다</b> - 예전 코드는 피격 때
///   `SetPropertyBlock`을 렌더러마다 호출했고, MaterialPropertyBlock이 붙은 렌더러는 배치에서
///   빠지면서 자기 텍스처를 되찾았다. 블록은 그 뒤로도 남으므로 한 번 맞으면 계속 정상이었다.
///
/// 그래서 지금은 (1) 렌더러마다 <b>전용 머티리얼 인스턴스</b>를 주고, (2) 그 머티리얼의
/// `_MainTex`에 자기 스프라이트의 텍스처를 <b>직접</b> 넣는다. 머티리얼이 다르면 배치로 묶일
/// 수 없고, 렌더러별 데이터 경로가 어떻게 동작하든 자기 텍스처가 그려지므로 교차 적용이
/// 구조적으로 불가능하다. <b>MaterialPropertyBlock은 더 이상 쓰지 않는다</b> - 블록에 캡처된
/// `_MainTex`가 그대로 굳어 버려서, 스프라이트를 바꿔도 옛 텍스처가 계속 그려지는 반대 방향
/// 함정이 있다(좀비 공격 프레임처럼 매 프레임 스프라이트가 바뀌는 경우).
///
/// 대상 SpriteRenderer는 자신과 모든 자식에서 자동으로 찾는다.
/// - <see cref="ignoredNames"/>: 아예 손대지 않는다(코드로 만들어 붙이는 체력바 등 장식용).
/// - <see cref="flashExcludedNames"/>: 이미지 관리는 받지만 하얘지지는 않는다(손에 든 무기).
/// </summary>
// 파츠 복구(ProceduralCharacterRig)·무기 교체(PlayerShootManager)가 10000이므로 그보다 뒤에서
// 돌아야 "그 프레임에 실제로 그려질" 최종 스프라이트를 머티리얼에 확정할 수 있다.
[DefaultExecutionOrder(11000)]
public class HitFlash : MonoBehaviour
{
    private const string ShaderName = "Comstock/SpriteFlash";
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    [Tooltip("피격 시 물드는 시간(초)")]
    [SerializeField] private float flashDuration = 0.25f;

    [Tooltip("피격 시 물드는 색")]
    [SerializeField] private Color flashColor = Color.white;

    [Tooltip("이 이름을 가진 오브젝트는 HitFlash가 전혀 건드리지 않는다.\n" +
             "HealthBar/Background/Fill: 코드로 만든 머리 위 체력바(장식용 단색 막대)")]
    [SerializeField] private string[] ignoredNames = { "HealthBar", "Background", "Fill" };

    [Tooltip("이 이름을 가진 오브젝트는 하얘지지 않는다(전용 머티리얼로 이미지 보호는 받는다).\n" +
             "RightWp_img/LeftWp_img: 플레이어가 손에 든 무기 이미지. 무기는 로봇 몸이 아니라 " +
             "장착물이라 피격 연출 대상이 아니다(2026-08-10 사용자 지정: \"무기 말고 로봇이 하얘져야 한다\")")]
    [SerializeField] private string[] flashExcludedNames = { "RightWp_img", "LeftWp_img" };

    private static Shader flash_shader;
    private static bool shader_lookup_done;
    private static bool warned_missing_shader;

    /// <summary>렌더러 하나와, 그 렌더러만 쓰는 전용 머티리얼 한 장.</summary>
    private sealed class Target
    {
        public SpriteRenderer renderer;
        public Material material;     // 이 렌더러 전용 인스턴스(절대 공유하지 않는다)
        public Sprite bound_sprite;   // material의 _MainTex에 반영해 둔 스프라이트
        public bool can_flash;        // false면 흰색 연출에서 제외(이미지 보호는 그대로)
    }

    private readonly List<Target> targets = new List<Target>();
    private float flash_time_left;
    private bool flashing;

    private void Awake() => CollectTargets();

    /// <summary>
    /// Awake가 전부 끝난 뒤 대상을 한 번 더 수집한다.
    ///
    /// <b>플레이어에게는 이 재수집이 필수다.</b> 로봇의 몸통·다리 스프라이트는 씬에 미리
    /// 있는 게 아니라 <see cref="ProceduralCharacterRig"/>가 자신의 Awake에서 코드로 만들어
    /// 붙인다. Unity는 오브젝트별 Awake 순서를 보장하지 않으므로, HitFlash.Awake가 먼저
    /// 돌면 그 시점엔 리그 스프라이트가 아직 없어 <b>씬에 미리 존재하는 무기 이미지만</b>
    /// 대상으로 잡혔다 - 실제로 "피격 시 로봇이 아니라 무기가 하얘지는" 증상으로 나타났다
    /// (2026-08-10). Start는 모든 Awake 이후에 실행되므로 여기서 반드시 다시 잡는다.
    /// </summary>
    private void Start() => CollectTargets();

    /// <summary>런타임에 스프라이트 구성이 바뀌었을 때(리그 재생성 등) 외부에서 대상을 다시 잡게 한다.</summary>
    public void RefreshTargets() => CollectTargets();

    /// <summary>
    /// 대상 렌더러 목록을 수집한다. 이미 관리 중인 렌더러는 그대로 두고 <b>새로 생긴 것만</b>
    /// 추가하므로(파괴된 것은 정리) 여러 번 불러도 머티리얼이 낭비되지 않는다.
    /// 절차적 리그처럼 런타임에 스프라이트를 만들어 붙이는 경우 Awake 시점에는 아직 자식이
    /// 없을 수 있어 Start·첫 피격·리그 재조립 시점에 다시 불린다.
    /// </summary>
    private void CollectTargets()
    {
        // 리그를 다시 조립하면(ProceduralCharacterRig.Build) 옛 렌더러가 파괴된다 - 먼저 정리.
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (targets[i].renderer != null) continue;
            DestroyOwnedMaterial(targets[i]);
            targets.RemoveAt(i);
        }

        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (IsNamed(ignoredNames, sr.gameObject.name)) continue;
            if (FindTarget(sr) != null) continue; // 이미 관리 중

            Material own = CreateOwnMaterial(sr);
            if (own == null) continue; // 기준으로 삼을 머티리얼이 아예 없다(정상적으로는 발생하지 않음)

            Target target = new Target
            {
                renderer = sr,
                material = own,
                can_flash = !IsNamed(flashExcludedNames, sr.gameObject.name),
            };

            sr.sharedMaterial = target.material;
            BindTexture(target);
            ApplyFlashAmount(target, flashing && target.can_flash ? 1f : 0f);
            targets.Add(target);
        }
    }

    /// <summary>
    /// 이 렌더러만 쓰는 머티리얼 인스턴스를 만든다.
    ///
    /// <b>"렌더러마다 자기 머티리얼"은 피격 연출과 무관하게 항상 지켜져야 하는 조건이다.</b>
    /// 그래서 피격 셰이더를 찾지 못하는 경우(빌드에서 스트리핑되는 등)에도 현재 머티리얼을
    /// 복제해서 렌더러별로 분리한다 - 그러지 않으면 런타임에 만들어진 SpriteRenderer들이
    /// 다시 같은 기본 머티리얼(`Sprites-Default`)을 공유하면서 파츠 이미지 교차 적용이
    /// 그대로 재발한다. 이 경우 연출(`_FlashAmount`)만 빠지고 이미지는 정상으로 나온다.
    /// </summary>
    private static Material CreateOwnMaterial(SpriteRenderer sr)
    {
        Shader shader = GetFlashShader();
        if (shader != null) return new Material(shader) { name = $"SpriteFlash ({sr.gameObject.name})" };

        Material current = sr.sharedMaterial;
        return current != null ? new Material(current) { name = $"{current.name} ({sr.gameObject.name})" } : null;
    }

    private Target FindTarget(SpriteRenderer sr)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].renderer == sr) return targets[i];
        }
        return null;
    }

    private static bool IsNamed(string[] names, string objectName)
    {
        if (names == null) return false;
        foreach (string candidate in names)
        {
            if (!string.IsNullOrEmpty(candidate) && objectName == candidate) return true;
        }
        return false;
    }

    /// <summary>
    /// 렌더러가 그릴 텍스처를 전용 머티리얼에 <b>직접</b> 넣는다. 이 한 줄이 파츠 교차 적용을
    /// 구조적으로 막는다 - 렌더러별 데이터(`[PerRendererData] _MainTex`) 경로가 배치 때문에
    /// 어긋나더라도, 머티리얼 자체가 자기 텍스처를 들고 있으므로 다른 파츠나 좀비 이미지가
    /// 그려질 수 없다(클래스 주석 참고).
    /// </summary>
    private static void BindTexture(Target target)
    {
        Sprite sprite = target.renderer.sprite;
        target.bound_sprite = sprite;
        target.material.SetTexture(MainTexId, sprite != null ? sprite.texture : null);
    }

    private static Shader GetFlashShader()
    {
        if (shader_lookup_done) return flash_shader;

        shader_lookup_done = true;
        Shader shader = Shader.Find(ShaderName);
        flash_shader = shader != null && shader.isSupported ? shader : null;

        if (flash_shader == null && !warned_missing_shader)
        {
            warned_missing_shader = true;
            Debug.LogWarning($"HitFlash: 셰이더 '{ShaderName}'을(를) 찾을 수 없거나 지원되지 않아 피격 연출이 표시되지 않습니다.");
        }
        return flash_shader;
    }

    /// <summary>씬을 다시 시작할 때 이전 판의 셰이더 조회 결과가 남지 않도록 비운다.</summary>
    public static void ResetStaticCaches()
    {
        flash_shader = null;
        shader_lookup_done = false;
        warned_missing_shader = false;
    }

    /// <summary>피격 연출을 시작한다(이미 진행 중이면 시간을 처음부터 다시 잰다).</summary>
    public void Play()
    {
        // 절차적 리그처럼 자식 스프라이트가 나중에 생기는 유닛을 위한 재수집
        if (targets.Count == 0) CollectTargets();
        if (targets.Count == 0) return;

        flash_time_left = flashDuration;
        flashing = true;
        SetFlashAmount(1f);
    }

    private void Update()
    {
        if (!flashing) return;

        flash_time_left -= Time.deltaTime;
        if (flash_time_left > 0f) return;

        flashing = false;
        SetFlashAmount(0f);
    }

    /// <summary>
    /// 실제 렌더 직전에 각 머티리얼의 텍스처를 그 프레임의 최종 스프라이트로 확정한다.
    /// 좀비 공격 프레임처럼 매 프레임 스프라이트가 바뀌는 경우까지 따라간다(실행 순서 11000 -
    /// 파츠·무기 복구 코드가 모두 끝난 뒤).
    /// </summary>
    private void LateUpdate()
    {
        bool renderer_lost = false;

        for (int i = 0; i < targets.Count; i++)
        {
            Target target = targets[i];
            if (target.renderer == null) { renderer_lost = true; continue; }

            // 다른 코드가 머티리얼을 갈아끼웠다면(공유 머티리얼로 되돌리는 등) 전용 인스턴스로 복구
            if (target.renderer.sharedMaterial != target.material) target.renderer.sharedMaterial = target.material;
            if (target.renderer.sprite != target.bound_sprite) BindTexture(target);
        }

        if (renderer_lost) CollectTargets(); // 리그 재조립 등으로 렌더러가 교체된 경우
    }

    // 0.25초 내내 완전한 흰색을 유지한다(사용자 지정: "0.25초 동안 하얀색").
    // 서서히 빠지게 하고 싶으면 여기서 flash_time_left / flashDuration 비율을 넘기면 된다.
    private void SetFlashAmount(float amount)
    {
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            Target target = targets[i];
            if (target.renderer == null)
            {
                DestroyOwnedMaterial(target);
                targets.RemoveAt(i);
                continue;
            }

            ApplyFlashAmount(target, target.can_flash ? amount : 0f);
        }
    }

    private void ApplyFlashAmount(Target target, float amount)
    {
        if (target.material == null) return;
        target.material.SetFloat(FlashAmountId, amount);
        target.material.SetColor(FlashColorId, flashColor);
    }

    /// <summary>유닛이 사라질 때 전용 머티리얼도 함께 정리한다(좀비는 매 웨이브 수십 마리가 죽는다).</summary>
    private void OnDestroy()
    {
        for (int i = 0; i < targets.Count; i++) DestroyOwnedMaterial(targets[i]);
        targets.Clear();
    }

    private static void DestroyOwnedMaterial(Target target)
    {
        if (target.material == null) return;

        if (Application.isPlaying) Destroy(target.material);
        else DestroyImmediate(target.material);

        target.material = null;
    }
}
