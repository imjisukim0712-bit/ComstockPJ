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
/// <b>이 셰이더는 반드시 URP(HLSL)용이어야 한다.</b> 빌트인 렌더 파이프라인용 스프라이트
/// 셰이더(CGPROGRAM + UnitySprites.cginc)로 바꾸면, 이 아래 <see cref="CollectTargets"/>가
/// 머티리얼을 갈아끼우는 순간부터 <b>해당 렌더러들이 엉뚱한 텍스처로 그려진다</b>
/// (머리에 신발, 다리에 좀비 이미지 등). URP 2D Renderer가 빌트인 RP 스프라이트 셰이더에는
/// SpriteRenderer의 렌더러별 데이터(_MainTex 등)를 넘겨주지 않기 때문이다. 첫 피격 때
/// <see cref="SetFlashAmount"/>가 MaterialPropertyBlock을 설정하면 그 데이터가 강제로
/// 업로드되어 정상으로 돌아오는데, 이것이 "첫 피격 전까지만 파츠가 깨져 보이던" 증상의
/// 정체였다(2026-08-10 수정, 상세는 작업.md).
///
/// <b>빌드에서는 반드시 `Resources/` 밑의 머티리얼 애셋을 통해 셰이더를 로드해야 한다.</b>
/// 에디터에서는 프로젝트의 모든 셰이더가 컴파일돼 있어 `Shader.Find`가 항상 성공하지만,
/// 실제 빌드는 씬/프리팹/`Resources` 애셋이 참조하지 않는 셰이더를 스트리핑한다. 이 셰이더는
/// 코드에서만 `new Material(shader)`로 동적 생성되고 어떤 씬 에셋도 참조하지 않으므로,
/// `Shader.Find`로 직접 찾으면 빌드에서 null이 되어 피격 연출이 조용히 사라진다(2026-08-12,
/// "빌드에서 피격 이펙트가 없어짐" 리포트로 발견). `Assets/Resources/Materials/
/// SpriteFlashBase.mat`(이 셰이더를 쓰는 더미 머티리얼)을 만들어 `Resources.Load`로 불러오면
/// 그 머티리얼이 빌드에 강제로 포함되면서 셰이더도 함께 포함된다.
///
/// 머티리얼 인스턴스를 유닛마다 새로 만들면(= sr.material 접근) 드로우콜이 늘고 GC가 생기므로,
/// <see cref="MaterialPropertyBlock"/>으로 렌더러별 값만 덮어쓴다.
///
/// 대상 SpriteRenderer는 자신과 모든 자식에서 자동으로 찾는다. 단, 체력바처럼 코드로 나중에
/// 만들어 붙이는 장식용 렌더러까지 같이 하얘지면 곤란하므로 <see cref="excludedNames"/>로 걸러낸다.
/// </summary>
public class HitFlash : MonoBehaviour
{
    private const string BaseMaterialResourcePath = "Materials/SpriteFlashBase";
    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    [Tooltip("피격 시 물드는 시간(초)")]
    [SerializeField] private float flashDuration = 0.25f;

    [Tooltip("피격 시 물드는 색")]
    [SerializeField] private Color flashColor = Color.white;

    [Tooltip("이 이름을 가진 오브젝트의 SpriteRenderer는 물들이지 않는다.\n" +
             "- HealthBar/Background/Fill: 코드로 만든 머리 위 체력바(장식용)\n" +
             "- RightWp_img/LeftWp_img: 플레이어가 손에 든 무기 이미지. 무기는 로봇 몸이 아니라 " +
             "장착물이라 피격 연출 대상이 아니다(2026-08-10 사용자 지정: \"무기 말고 로봇이 하얘져야 한다\")")]
    [SerializeField] private string[] excludedNames = { "HealthBar", "Background", "Fill", "RightWp_img", "LeftWp_img" };

    private static Material shared_flash_material;
    private static bool warned_missing_shader;

    private readonly List<SpriteRenderer> targets = new List<SpriteRenderer>();
    private MaterialPropertyBlock block;
    private float flash_time_left;
    private bool flashing;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        CollectTargets();
    }

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
    /// 대상 렌더러 목록을 다시 수집한다. 절차적 리그처럼 <b>런타임에 스프라이트를 만들어
    /// 붙이는 경우</b> Awake 시점에는 아직 자식이 없을 수 있으므로, 그런 유닛은 첫 피격 때
    /// 다시 한 번 수집한다.
    /// </summary>
    private void CollectTargets()
    {
        targets.Clear();

        Material flashMaterial = GetFlashMaterial();
        if (flashMaterial == null) return;

        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (IsExcluded(sr.gameObject.name)) continue;

            // 셰이더가 _FlashAmount를 가지고 있어야 MaterialPropertyBlock이 먹는다.
            if (sr.sharedMaterial == null || sr.sharedMaterial.shader != flashMaterial.shader)
            {
                sr.sharedMaterial = flashMaterial;
            }

            targets.Add(sr);
        }
    }

    private bool IsExcluded(string objectName)
    {
        foreach (string excluded in excludedNames)
        {
            if (!string.IsNullOrEmpty(excluded) && objectName == excluded) return true;
        }
        return false;
    }

    private static Material GetFlashMaterial()
    {
        if (shared_flash_material != null) return shared_flash_material;

        Material baseMaterial = Resources.Load<Material>(BaseMaterialResourcePath);
        if (baseMaterial == null || baseMaterial.shader == null || !baseMaterial.shader.isSupported)
        {
            if (!warned_missing_shader)
            {
                warned_missing_shader = true;
                Debug.LogWarning($"HitFlash: '{BaseMaterialResourcePath}' 머티리얼을 찾을 수 없거나 셰이더가 지원되지 않아 피격 연출이 표시되지 않습니다.");
            }
            return null;
        }

        shared_flash_material = new Material(baseMaterial) { name = "SpriteFlash (shared)" };
        return shared_flash_material;
    }

    /// <summary>씬을 다시 시작할 때 이전 판의 머티리얼 참조가 남지 않도록 비운다.</summary>
    public static void ResetStaticCaches()
    {
        shared_flash_material = null;
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

        // 정비 화면 진입으로 timeScale이 0이 되면 flash_time_left가 줄지 않아 하얗게 물든
        // 채로 얼어붙는다(주로 플레이어 - 적은 대부분 웨이브 종료와 함께 파괴된다). 다른
        // 런타임 생성 이펙트(DamageNumberUI 등)와 같은 관례로 즉시 꺼서 정리한다(2026-08-21).
        if (GameFlowManager.IsIntermission)
        {
            flashing = false;
            SetFlashAmount(0f);
            return;
        }

        flash_time_left -= Time.deltaTime;
        if (flash_time_left > 0f) return;

        flashing = false;
        SetFlashAmount(0f);
    }

    // 0.25초 내내 완전한 흰색을 유지한다(사용자 지정: "0.25초 동안 하얀색").
    // 서서히 빠지게 하고 싶으면 여기서 flash_time_left / flashDuration 비율을 넘기면 된다.
    private void SetFlashAmount(float amount)
    {
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            SpriteRenderer sr = targets[i];
            if (sr == null) { targets.RemoveAt(i); continue; }

            sr.GetPropertyBlock(block);
            block.SetFloat(FlashAmountId, amount);
            block.SetColor(FlashColorId, flashColor);
            sr.SetPropertyBlock(block);
        }
    }
}
