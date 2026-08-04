using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어가 보유한 2개의 무기 슬롯을 관리하고,
/// 마우스 클릭 시 현재 선택된 무기의 muzzle_point 위치에서
/// 클릭한 지점을 향해 투사체를 발사한다.
///
/// 무기 데이터테이블(WeaponData) 각 필드 사용 방식:
/// - weapon_atk        : 명중 시 데미지
/// - weapon_atsp       : 공격속도 → 다음 발사까지의 쿨다운 (1 / atsp 초)
/// - weapon_range      : 투사체 최대 사거리
/// - weapon_atsize     : 투사체 크기(스케일)
/// - weapon_aim        : 조준 정확도 → 클릭 지점 기준 방향이 최대 이 각도(도)만큼 무작위로 벗어남
/// - weapon_rebound    : 반동 → 한 번의 발사(TryFire 1회)에서 여러 발이 나갈 때, 발마다 진행 방향은 그대로 두고 옆으로 벌어지는 간격(평행 발사)
/// - weapon_projectiles: 한 번에 발사되는 투사체 개수
/// - weapon_capacity   : 장탄수 → 이만큼 발사하면 자동으로 재장전 시작
/// - weapon_reload     : 재장전 소요 시간(초)
/// - weapon_penetration: 관통 여부 (0 = 첫 충돌 시 소멸 / 1 = 관통, 충돌해도 유지)
/// - weapon_tanhwan    : 발사할 투사체 프리팹 이름 → projectile_prefabs 목록에서 같은 이름을 찾아 사용
///
/// PlayerRobotController(로봇 데이터테이블)의 스탯도 함께 반영한다:
/// - robot_atk     : 최종 데미지 = weapon_atk + robot_atk
/// - robot_cc/cd   : 0~100 랜덤값이 robot_cc 이하면 치명타 → 데미지 = 데미지 + 데미지 * robot_cd
/// - robot_capacity: 무기 장탄수 = weapon_capacity + weapon_capacity * robot_capacity
///
/// delayed_blast_weapon_ids 범위(기본 300400~300499, 수류탄류)에 속한 무기는
/// weapon_atsize를 접촉 판정 크기로 쓰지 않고, 작은 크기로 날아가다가
/// weapon_range(최대 사거리) 지점에서 weapon_atsize 범위에 한 번에 데미지를 준다.
/// 단, 사거리 안쪽을 클릭했다면 최대 사거리까지 가지 않고 그 클릭 지점에서 바로 폭발한다.
///
/// 투사체 속도는 데이터테이블에 없는 값이라 무기 슬롯별로 인스펙터에서 직접 지정한다.
///
/// 전제: 게임플레이는 X-Y 평면만 사용 (Z축 미사용) → PlayerRobotController와 동일한 규칙
/// </summary>
public class PlayerShootManager : MonoBehaviour
{
    [Serializable]
    public struct WeaponSlot
    {
        [Tooltip("GameDataManager.Weapons 조회에 사용되는 weapon_id (CSV 기준)")]
        public int weapon_id;

        [Tooltip("투사체가 실제로 발사되는 위치/콜라이더 (무기 오브젝트의 Transform)")]
        public Transform muzzle_point;

        [Tooltip("무기 이미지를 회전시킬 기준점(리깅 포인트). 비어있으면 muzzle_point를 기준으로 회전한다")]
        public Transform rig_point;

        [Tooltip("데이터테이블의 weapon_tanhwan(발사 탄환) 이름을 못 찾았을 때 사용할 예비 프리팹")]
        public GameObject projectile_prefab;

        [Tooltip("이 무기 투사체의 이동 속도 - 데이터테이블에 없는 값이라 여기서 직접 지정")]
        public float projectile_speed;

        [Tooltip("이 슬롯의 무기 이미지를 보여줄 SpriteRenderer (예: LeftWp_img, RightWp_img 자식의 SpriteRenderer)")]
        public SpriteRenderer hand_sprite_renderer;

        [Tooltip("체크하면 데이터테이블의 무기 왼손 이미지(weapon_lfwpimg)를, 체크 해제하면 무기 오른손 이미지(weapon_rgwpimg)를 사용")]
        public bool use_left_hand_image;

        [Tooltip("마우스 조준 중(활성 슬롯)에만 사용되는 보정각(도). 원본 이미지의 총구가 그려진 각도가 " +
                 "atan2로 계산한 방향과 다를 때 이 값을 더해서 실제 총구가 마우스를 정확히 향하도록 보정한다. " +
                 "대기 자세(rest_rotation_degrees)와는 별개의 값이다")]
        public float rotation_offset_degrees;

        [Tooltip("비활성 슬롯이거나 무기 교체 직후(대기 상태)일 때 고정으로 보여줄 절대 회전각(도). " +
                 "인스펙터 상의 RigingPoint Transform Rotation Z 값을 그대로 넣으면 된다. " +
                 "조준 중 보정값(rotation_offset_degrees)과 별개로 동작한다")]
        public float rest_rotation_degrees;

        [Tooltip("체크하면, RigingPoint의 현재 회전각(도, -180~180 정규화)이 " +
                 "flip_angle_min/max_degrees 범위를 벗어날 때 이미지를 Y축 기준으로 좌우 반전(FlipX)한다")]
        public bool use_angle_flip;

        [Tooltip("반전 없이 정상으로 보이는 각도 범위의 최소값(도). 회전각이 이보다 작아지면 반전")]
        public float flip_angle_min_degrees;

        [Tooltip("반전 없이 정상으로 보이는 각도 범위의 최대값(도). 회전각이 이보다 커지면 반전")]
        public float flip_angle_max_degrees;

        [Tooltip("반전(FlipY)될 때 이미지에 추가로 더해줄 Z축 회전각(도). 슬롯(왼손/오른손)마다 따로 지정")]
        public float flip_extra_rotation_degrees;
    }

    [Serializable]
    public struct WeaponIdRange
    {
        [Tooltip("범위 시작 weapon_id (포함)")]
        public int min_id;

        [Tooltip("범위 끝 weapon_id (포함)")]
        public int max_id;
    }

    [Serializable]
    public struct ProjectilePrefabEntry
    {
        [Tooltip("데이터테이블 weapon_tanhwan 컬럼에 적는 이름 (예: Bullets, Energy)")]
        public string prefab_name;

        [Tooltip("Assets/Prefebs 안의 투사체 프리팹")]
        public GameObject prefab;
    }

    [Serializable]
    public struct WeaponImageSizeOverride
    {
        [Tooltip("크기를 보정할 무기ID (weapon_id)")]
        public int weapon_id;

        [Tooltip("자동 정규화된 크기(TargetHandImageSize)에 추가로 곱할 배율. 1 = 보정 없음")]
        public float size_multiplier;
    }

    // 무기 슬롯 하나가 가지는 실시간 탄약/재장전 상태
    private class WeaponRuntimeState
    {
        public int ammo_remaining = -1; // -1 = 아직 초기화 안 됨
        public bool is_reloading = false;
        public float reload_end_time = 0f;
    }

    [Header("장착 무기 슬롯 (0번, 1번 = 무기1, 무기2)")]
    [SerializeField] private List<WeaponSlot> weapon_slots = new List<WeaponSlot>();

    [Header("투사체 프리팹 목록 (데이터테이블 weapon_tanhwan 이름 ↔ 프리팹)")]
    [Tooltip("Assets/Prefebs 안의 투사체 프리팹을 이름과 함께 등록. 여기 없는 이름은 Resources 폴더에서도 찾아본다")]
    [SerializeField] private List<ProjectilePrefabEntry> projectile_prefabs = new List<ProjectilePrefabEntry>();

    [Header("무기 전환 키")]
    [SerializeField] private Key weapon1_key = Key.Digit1;
    [SerializeField] private Key weapon2_key = Key.Digit2;

    [Header("지연 폭발 무기 (수류탄류) - 사거리 끝에서 범위 데미지")]
    [Tooltip("이 ID 범위에 속한 무기는 작게 날아가다가 사거리 끝에서 weapon_atsize 범위에 데미지를 준다")]
    [SerializeField]
    private List<WeaponIdRange> delayed_blast_weapon_ids = new List<WeaponIdRange>
    {
        new WeaponIdRange { min_id = 300400, max_id = 300499 }
    };

    [Tooltip("날아가는 동안의 투사체 크기. 폭발 전까지는 이 크기로만 이동한다")]
    [SerializeField] private float delayed_blast_travel_size = 0.1f;

    [Tooltip("날아가는 중 적과 닿으면 사거리 끝까지 가지 않고 그 자리에서 즉시 폭발")]
    [SerializeField] private bool delayed_blast_explode_on_contact = true;

    [Tooltip("폭발 범위를 화면에 잠깐 보여주는 시간(초). 0이면 연출 없이 즉시 사라짐")]
    [SerializeField] private float delayed_blast_visual_duration = 0.08f;

    [Header("투사체 간 좌우 간격 배율")]
    [Tooltip("weapon_rebound(반경 간격) 값에 곱해지는 배율. 투사체끼리 더 멀리/가깝게 벌리고 싶을 때 조절")]
    [SerializeField] private float side_spacing_multiplier = 2f;

    [Header("무기별 이미지 크기 보정")]
    [Tooltip("특정 무기ID의 손 이미지를 자동 정규화 크기보다 더 크게/작게 보이고 싶을 때 배율 지정")]
    [SerializeField]
    private List<WeaponImageSizeOverride> weapon_image_size_overrides = new List<WeaponImageSizeOverride>
    {
        new WeaponImageSizeOverride { weapon_id = 300001, size_multiplier = 1.3f } // 기관단총(SMG) - 조금 더 크게
    };

    private Camera main_camera;
    private PlayerRobotController player_stats; // 로봇 공격력/치명타/장탄 보정치를 가져오는 용도
    private int current_slot_index = 0;
    private float next_fire_time = 0f;
    private WeaponData? current_weapon_data;
    private readonly Dictionary<int, WeaponRuntimeState> runtime_state_by_slot = new Dictionary<int, WeaponRuntimeState>();

    // weapon_tanhwan(프리팹 이름) → 프리팹. 대소문자 구분 없이 조회
    private readonly Dictionary<string, GameObject> prefab_by_name =
        new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

    // 이름을 못 찾았을 때 매 발사마다 경고가 도배되지 않도록 기록
    private readonly HashSet<string> warned_prefab_names = new HashSet<string>();

    // weapon_lfwpimg/weapon_rgwpimg(이미지 이름) → 스프라이트. Resources.Load 결과를 캐시
    private readonly Dictionary<string, Sprite> sprite_by_name = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> warned_missing_sprite_names = new HashSet<string>();

    // 무기 이미지 원본 픽셀 크기가 이미지마다 제각각이라(예: SMG 1080px vs 기관총류 480px)
    // 같은 Transform 스케일을 써도 화면에 보이는 크기가 크게 달라지는 문제가 있었다.
    // 그래서 스프라이트를 바꿔 낄 때마다 "화면에 보이는 실제 크기"가 항상 이 값(월드 단위)이
    // 되도록 스케일을 자동 보정한다. 값은 기존에 잘 보이던 기관총 이미지 기준
    // (스프라이트 4.8유닛 크기 x 기존 지정 스케일 0.6)으로 역산한 것.
    private const float TargetHandImageSize = 4.8f * 0.6f;

    private void Awake()
    {
        main_camera = Camera.main;

        // ShootManager는 Player와 별개 오브젝트라 GetComponent가 아니라 태그로 찾는다.
        GameObject player_obj = GameObject.FindGameObjectWithTag("Player");
        if (player_obj != null) player_stats = player_obj.GetComponent<PlayerRobotController>();
        if (player_stats == null)
        {
            Debug.LogWarning("PlayerRobotController를 찾을 수 없습니다. 로봇 공격력/치명타/장탄 보정 없이 무기 기본 수치로만 발사합니다.");
        }

        foreach (var entry in projectile_prefabs)
        {
            if (string.IsNullOrWhiteSpace(entry.prefab_name) || entry.prefab == null) continue;
            prefab_by_name[entry.prefab_name.Trim()] = entry.prefab;
        }
    }

    private void Start()
    {
        // GameDataManager는 코루틴으로 CSV를 비동기 로드하므로, Start() 시점엔
        // 무기 데이터가 아직 없을 수 있다. 그 상태에서 RefreshCurrentWeaponData()를
        // 한 번만 부르면 "데이터 없음" 경고만 뜨고 끝나서, 게임 시작 시 1번 슬롯
        // 무기가 기본으로 장착되지 않는 문제가 있었다. 로드 완료 이벤트를 받아
        // 그 시점에 다시 한번 확실히 반영되도록 한다.
        current_slot_index = 0; // 1번 슬롯(무기1)을 기본 무기로 사용

        if (GameDataManager.Instance.IsLoaded)
        {
            RefreshCurrentWeaponData();
            RefreshAllWeaponVisuals();
        }
        else
        {
            GameDataManager.Instance.OnLoaded += HandleGameDataLoaded;
        }
    }

    private void OnDestroy()
    {
        if (GameDataManager.Instance != null) GameDataManager.Instance.OnLoaded -= HandleGameDataLoaded;
    }

    private void HandleGameDataLoaded()
    {
        RefreshCurrentWeaponData();
        RefreshAllWeaponVisuals();
    }

    /// <summary>
    /// 양손에 장착된 무기 슬롯 전부(선택된 슬롯뿐 아니라 왼손/오른손 두 슬롯 모두)의 이미지를
    /// 데이터테이블(weapon_lfwpimg/weapon_rgwpimg)에 맞춰 갱신한다. 두 무기는 항상 동시에
    /// 양손에 들려 보이므로, "현재 선택된 무기"와 무관하게 매 슬롯을 각자의 무기ID로 갱신한다.
    /// </summary>
    private void RefreshAllWeaponVisuals()
    {
        if (GameDataManager.Instance == null) return;

        for (int i = 0; i < weapon_slots.Count; i++)
        {
            WeaponSlot slot = weapon_slots[i];
            if (slot.hand_sprite_renderer == null) continue;

            if (!GameDataManager.Instance.Weapons.TryGetValue(slot.weapon_id, out WeaponData data)) continue;

            string sprite_name = slot.use_left_hand_image ? data.weapon_lfwpimg : data.weapon_rgwpimg;
            Sprite sprite = ResolveWeaponSprite(sprite_name, data);
            slot.hand_sprite_renderer.sprite = sprite;

            // 원본 이미지 크기가 제각각이라(예: SMG가 기관총류보다 훨씬 큰 픽셀 크기) 매번
            // 화면에 보이는 크기가 TargetHandImageSize로 일정해지도록 스케일을 다시 계산한다.
            // 그 위에 무기별 개별 배율(weapon_image_size_overrides)을 추가로 곱한다.
            if (sprite != null)
            {
                float max_dim = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y, 0.0001f);
                float normalized_scale = TargetHandImageSize / max_dim;
                float size_multiplier = GetSizeMultiplier(slot.weapon_id);
                slot.hand_sprite_renderer.transform.localScale = Vector3.one * (normalized_scale * size_multiplier);
            }
        }
    }

    // weapon_image_size_overrides에서 해당 무기ID의 배율을 찾는다. 없으면 1(보정 없음)
    private float GetSizeMultiplier(int weapon_id)
    {
        foreach (WeaponImageSizeOverride entry in weapon_image_size_overrides)
        {
            if (entry.weapon_id == weapon_id) return entry.size_multiplier;
        }
        return 1f;
    }

    // 데이터테이블에 적힌 이미지 이름으로 Resources 폴더에서 스프라이트를 찾아온다 (캐시 사용)
    private Sprite ResolveWeaponSprite(string sprite_name, WeaponData weapon)
    {
        if (string.IsNullOrWhiteSpace(sprite_name)) return null;
        sprite_name = sprite_name.Trim();

        if (sprite_by_name.TryGetValue(sprite_name, out Sprite cached)) return cached;

        Sprite loaded = Resources.Load<Sprite>(sprite_name);
        if (loaded != null)
        {
            sprite_by_name[sprite_name] = loaded;
            return loaded;
        }

        if (warned_missing_sprite_names.Add(sprite_name))
        {
            Debug.LogWarning($"무기ID {weapon.weapon_id}({weapon.weapon_name})의 이미지 '{sprite_name}'을(를) Resources 폴더에서 찾을 수 없습니다.");
        }

        return null;
    }

    private void Update()
    {
        if (GameOverManager.IsGameOver) return; // 게임오버 이후에는 무기 전환/재장전/발사 모두 정지

        HandleWeaponSwitch();
        HandleReloadProgress();
        AimActiveWeaponAtMouse();

        if (Mouse.current == null) return;

        // 클릭 순간(wasPressedThisFrame)만이 아니라 누르고 있는 동안(isPressed) 계속 발사 시도.
        // 실제 발사 간격은 TryFire() 안의 next_fire_time(weapon_atsp 쿨다운)이 그대로 제어하므로
        // 여기서 매 프레임 호출해도 공격속도보다 빠르게 나가지는 않는다.
        if (Mouse.current.leftButton.isPressed)
        {
            TryFire();
        }
    }

    /// <summary>
    /// 현재 사용 중인(current_slot_index) 무기 슬롯을 마우스 커서 방향으로 회전시킨다.
    ///
    /// 구조: 리깅 포인트(rig_point)는 위치가 고정된 회전축이고, 무기 이미지(hand_sprite_renderer)와
    /// 총구(muzzle_point) 둘 다 그 자식으로 붙어있다. 그래서 여기서 rig_point의 "회전"만 바꾸면
    /// 리깅 포인트 자신은 제자리에 그대로 있고, 이미지와 총구가 함께 그 지점을 중심으로 스윙하며
    /// 마우스를 향한다 - 총이 회전하면 투사체가 나가는 위치(총구)도 같이 움직인다.
    /// rig_point가 비어있으면 muzzle_point를 기준으로 회전한다(폴백).
    ///
    /// 슬롯별 rotation_offset_degrees는 무기 이미지 원본이 실제로 그려진 각도(예: 회전 0에서
    /// 정면이 아니라 대각선을 보고 있는 경우)를 보정한다. 사용 중이 아닌 슬롯도 이 보정값을
    /// 그대로 적용한 자세로 쉬게 하여(마냥 회전 0이 아니라), 원본 그림이 어색한 방향을 보고
    /// 있지 않도록 한다.
    /// </summary>
    private void AimActiveWeaponAtMouse()
    {
        if (Mouse.current == null || main_camera == null) return;

        for (int i = 0; i < weapon_slots.Count; i++)
        {
            WeaponSlot slot = weapon_slots[i];
            Transform pivot = slot.rig_point != null ? slot.rig_point : slot.muzzle_point;
            if (pivot == null) continue;

            if (i != current_slot_index)
            {
                // 비활성 슬롯: 마우스를 따라가지 않고, 고정된 대기 자세 각도로 쉰다.
                pivot.rotation = Quaternion.Euler(0f, 0f, slot.rest_rotation_degrees);
                ApplyAngleFlip(slot, slot.rest_rotation_degrees, false);
                continue;
            }

            Vector3 target_point = GetMouseWorldPointOnPlane(pivot.position.z);
            Vector3 direction = target_point - pivot.position;
            direction.z = 0f; // X-Y 평면만 사용

            if (direction.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + slot.rotation_offset_degrees;
                pivot.rotation = Quaternion.Euler(0f, 0f, angle);
                ApplyAngleFlip(slot, angle, true);
            }
        }
    }

    // 회전각이 정상 범위(flip_angle_min~max_degrees)를 벗어나면 이미지를 Y축 기준으로 좌우 반전한다.
    // 예: 왼손 무기가 회전하다가 위/아래로 너무 많이 돌아가면(예: 132.596도 초과, -60.262도 미만)
    // 그림이 뒤집혀 보이는 것을 막기 위해 스프라이트를 X축 기준(flipY, 상하 반전) 처리한다.
    private void ApplyAngleFlip(WeaponSlot slot, float angle, bool is_active_slot)
    {
        if (!slot.use_angle_flip || slot.hand_sprite_renderer == null) return;

        float normalized = NormalizeAngleDegrees(angle);
        bool should_flip = normalized > slot.flip_angle_max_degrees || normalized < slot.flip_angle_min_degrees;
        slot.hand_sprite_renderer.flipY = should_flip;

        // 반전될 때, 이미지 자체(총구/리깅 포인트에는 영향 없음)에 추가로 회전을 얹어준다.
        slot.hand_sprite_renderer.transform.localRotation = Quaternion.Euler(0f, 0f, should_flip ? slot.flip_extra_rotation_degrees : 0f);
    }

    // 각도를 -180 ~ 180 범위로 정규화 (atan2 + offset 계산 결과가 이 범위를 벗어날 수 있어서 필요)
    private static float NormalizeAngleDegrees(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    private void HandleWeaponSwitch()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[weapon1_key].wasPressedThisFrame && weapon_slots.Count > 0)
        {
            current_slot_index = 0;
            RefreshCurrentWeaponData();
            ResetAllWeaponRotationsToRest();
        }
        else if (Keyboard.current[weapon2_key].wasPressedThisFrame && weapon_slots.Count > 1)
        {
            current_slot_index = 1;
            RefreshCurrentWeaponData();
            ResetAllWeaponRotationsToRest();
        }
    }

    // 1/2키로 무기를 바꾸는 순간, 두 무기 모두 각자의 대기 자세 각도(rest_rotation_degrees)로
    // 즉시 스냅시킨다. 새로 활성화된 무기는 바로 다음 줄의 AimActiveWeaponAtMouse()가 같은 프레임
    // 안에 다시 마우스 방향으로 회전시키지만, 전환 순간에는 항상 정해진 초기 자세에서 시작하도록
    // 한 번 확실히 고정해서 이전 각도가 남아있는 경우가 없도록 한다.
    private void ResetAllWeaponRotationsToRest()
    {
        foreach (WeaponSlot slot in weapon_slots)
        {
            Transform pivot = slot.rig_point != null ? slot.rig_point : slot.muzzle_point;
            if (pivot != null) pivot.rotation = Quaternion.Euler(0f, 0f, slot.rest_rotation_degrees);
            ApplyAngleFlip(slot, slot.rest_rotation_degrees, false);
        }
    }

    // 선택된 슬롯의 weapon_id로 GameDataManager에서 실제 스탯(WeaponData)을 다시 가져온다.
    private void RefreshCurrentWeaponData()
    {
        if (weapon_slots.Count == 0)
        {
            current_weapon_data = null;
            return;
        }

        // GameDataManager.Instance가 null이면 (씬에 DataManager가 없거나, 플레이 도중 스크립트가
        // 재컴파일되며 static Instance가 초기화된 경우 등) 여기서 NullReferenceException이 나지 않도록 방어
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("GameDataManager.Instance가 없습니다. 씬에 DataManager 오브젝트가 있는지, " +
                              "혹은 플레이 도중 스크립트가 재컴파일되어 Instance가 초기화되지 않았는지 확인하세요.");
            current_weapon_data = null;
            return;
        }

        int weapon_id = weapon_slots[current_slot_index].weapon_id;

        if (GameDataManager.Instance.Weapons.TryGetValue(weapon_id, out WeaponData data))
        {
            current_weapon_data = data;
            EnsureRuntimeState(current_slot_index, data);
        }
        else
        {
            Debug.LogWarning($"무기ID {weapon_id}의 데이터를 찾을 수 없습니다. GameDataManager.Weapons에 해당 ID가 로드되었는지 확인하세요.");
            current_weapon_data = null;
        }
    }

    // 슬롯별 탄약 상태가 없으면 해당 무기의 weapon_capacity로 처음 채워서 만든다.
    private void EnsureRuntimeState(int slot_index, WeaponData data)
    {
        if (!runtime_state_by_slot.TryGetValue(slot_index, out WeaponRuntimeState state))
        {
            state = new WeaponRuntimeState();
            runtime_state_by_slot[slot_index] = state;
        }

        if (state.ammo_remaining < 0)
        {
            state.ammo_remaining = EffectiveCapacity(data);
        }
    }

    // robot_capacity(로봇 장탄증감 수치)를 반영한 실제 장탄수.
    // 무기 장탄수 + 무기 장탄수 * robot_capacity. weapon_capacity가 0/미설정이면 원래대로 무제한 취급.
    private int EffectiveCapacity(WeaponData data)
    {
        if (data.weapon_capacity <= 0) return int.MaxValue;

        float modifier = player_stats != null ? player_stats.Capacity : 0f;
        return Mathf.Max(1, Mathf.RoundToInt(data.weapon_capacity + data.weapon_capacity * modifier));
    }

    // 재장전 중인 슬롯이 있으면 시간 경과를 체크해서 재장전을 끝내준다.
    private void HandleReloadProgress()
    {
        if (GameDataManager.Instance == null) return; // 아래 RefreshCurrentWeaponData와 동일한 이유의 방어 코드

        foreach (var kv in runtime_state_by_slot)
        {
            WeaponRuntimeState state = kv.Value;
            if (state.is_reloading && Time.time >= state.reload_end_time)
            {
                state.is_reloading = false;

                int weapon_id = weapon_slots[kv.Key].weapon_id;
                if (GameDataManager.Instance.Weapons.TryGetValue(weapon_id, out WeaponData data))
                {
                    state.ammo_remaining = EffectiveCapacity(data);
                    Debug.Log($"{data.weapon_name} 재장전 완료 (장탄수 {state.ammo_remaining})");
                }
            }
        }
    }

    private void TryFire()
    {
        if (current_weapon_data == null)
        {
            Debug.LogWarning("현재 선택된 무기의 데이터가 없어 발사할 수 없습니다.");
            return;
        }

        if (Time.time < next_fire_time) return; // weapon_atsp(공격속도) 쿨다운 중

        WeaponData weapon = current_weapon_data.Value;
        WeaponSlot slot = weapon_slots[current_slot_index];

        if (slot.muzzle_point == null)
        {
            Debug.LogWarning("무기 슬롯의 muzzle_point가 비어있습니다. 인스펙터에서 연결해주세요.");
            return;
        }

        // 데이터테이블의 weapon_tanhwan(발사 탄환) 이름으로 투사체 프리팹 결정
        GameObject projectile_prefab = ResolveProjectilePrefab(weapon, slot);
        if (projectile_prefab == null) return;

        if (!runtime_state_by_slot.TryGetValue(current_slot_index, out WeaponRuntimeState state))
        {
            EnsureRuntimeState(current_slot_index, weapon);
            state = runtime_state_by_slot[current_slot_index];
        }

        if (state.is_reloading)
        {
            // weapon_reload(재장전 시간) 중에는 공격 입력을 완전히 무시한다.
            float remaining = Mathf.Max(0f, state.reload_end_time - Time.time);
            Debug.LogWarning($"{weapon.weapon_name} 재장전 중이라 발사할 수 없습니다. (남은 시간: {remaining:F1}초)");
            return;
        }

        Vector3 fire_origin = slot.muzzle_point.position;
        Vector3 target_point = GetMouseWorldPointOnPlane(fire_origin.z);

        Vector3 to_target = target_point - fire_origin;
        to_target.z = 0f; // Z축 미사용 규칙 - 방향은 X-Y 평면 안에서만 계산
        // 클릭 지점까지의 거리. 지연 폭발 무기(폭발화기)가 사거리 안 클릭에서 조기 폭발할지 판단하는 데 사용
        float click_distance = to_target.magnitude;

        Vector3 aim_direction = to_target;
        if (aim_direction.sqrMagnitude < 0.0001f) aim_direction = Vector3.right;
        aim_direction.Normalize();

        // weapon_aim(조준 정확도) → 클릭 지점 기준 방향을 좌우로 무작위로 흐트러뜨림
        if (weapon.weapon_aim > 0f)
        {
            float random_deviation = UnityEngine.Random.Range(-weapon.weapon_aim, weapon.weapon_aim);
            aim_direction = Quaternion.Euler(0f, 0f, random_deviation) * aim_direction;
        }

        // 최종 데미지 = weapon_atk + robot_atk, 그리고 robot_cc/cd(치명타) 적용.
        // 여러 발이 나가는 무기(weapon_projectiles > 1)는 발사 1회에 한 번만 치명타를 굴려 모든 탄에 동일하게 적용한다.
        int damage = ComputeDamage(weapon);

        FireProjectiles(projectile_prefab, slot, weapon, fire_origin, aim_direction, click_distance, damage);

        // weapon_capacity(장탄수, robot_capacity 보정 반영)만큼 투사체를 소모했는지 체크
        // → 다 소모했으면 그 순간부터 weapon_reload(재장전 시간) 동안 공격 입력을 막는다.
        int shots_consumed = Mathf.Max(1, weapon.weapon_projectiles);
        if (weapon.weapon_capacity > 0)
        {
            state.ammo_remaining -= shots_consumed;
            if (state.ammo_remaining <= 0)
            {
                state.is_reloading = true;
                state.reload_end_time = Time.time + Mathf.Max(0f, weapon.weapon_reload);
                Debug.Log($"{weapon.weapon_name} 장탄 소진 → 재장전 시작 (소요시간 {weapon.weapon_reload}초)");
            }
        }

        // weapon_atsp(공격속도)가 높을수록 다음 발사까지 대기시간이 짧아짐
        float cooldown = weapon.weapon_atsp > 0f ? 1f / weapon.weapon_atsp : 1f;
        next_fire_time = Time.time + cooldown;
    }

    /// <summary>
    /// 최종 투사체 데미지 = weapon_atk + robot_atk.
    /// robot_cc(치명타 확률, 0~100) 판정에 성공하면 데미지 = 데미지 + 데미지 * robot_cd.
    /// </summary>
    private int ComputeDamage(WeaponData weapon)
    {
        float damage = weapon.weapon_atk + (player_stats != null ? player_stats.Atk : 0);

        if (player_stats != null && player_stats.Cc > 0f)
        {
            float crit_roll = UnityEngine.Random.Range(0f, 100f);
            if (crit_roll <= player_stats.Cc)
            {
                damage += damage * player_stats.Cd;
            }
        }

        return Mathf.Max(0, Mathf.RoundToInt(damage));
    }

    // weapon_projectiles(발사 개수)만큼 투사체를 생성. 2발 이상이면 진행 방향은 그대로 두고,
    // weapon_rebound를 좌우 간격으로 사용해 옆으로 나란히(평행하게) 추가 발사한다.
    /// <summary>
    /// 데이터테이블의 weapon_tanhwan(발사 탄환) 이름으로 투사체 프리팹을 찾는다.
    /// 1) 인스펙터의 projectile_prefabs 목록  2) Resources 폴더  3) 슬롯의 예비 프리팹
    /// </summary>
    private GameObject ResolveProjectilePrefab(WeaponData weapon, WeaponSlot slot)
    {
        string prefab_name = weapon.weapon_tanhwan;

        if (!string.IsNullOrWhiteSpace(prefab_name))
        {
            prefab_name = prefab_name.Trim();

            if (prefab_by_name.TryGetValue(prefab_name, out GameObject found) && found != null) return found;

            // 프리팹이 Resources 폴더(또는 Resources/Prefebs) 안에 있으면 이름만으로도 찾아진다
            GameObject loaded = Resources.Load<GameObject>(prefab_name)
                             ?? Resources.Load<GameObject>("Prefebs/" + prefab_name);
            if (loaded != null)
            {
                prefab_by_name[prefab_name] = loaded; // 다음 발사부터는 캐시 사용
                return loaded;
            }

            if (warned_prefab_names.Add(prefab_name))
            {
                Debug.LogWarning($"무기ID {weapon.weapon_id}({weapon.weapon_name})의 발사 탄환 '{prefab_name}' 프리팹을 찾을 수 없습니다. " +
                                 "PlayerShootManager의 Projectile Prefabs 목록에 같은 이름으로 등록해주세요. 일단 슬롯의 예비 프리팹으로 발사합니다.");
            }
        }

        if (slot.projectile_prefab != null) return slot.projectile_prefab;

        Debug.LogWarning($"무기ID {weapon.weapon_id}에 사용할 투사체 프리팹이 없습니다. 데이터테이블의 발사 탄환 이름 또는 슬롯의 예비 프리팹을 확인해주세요.");
        return null;
    }

    private void FireProjectiles(GameObject projectile_prefab, WeaponSlot slot, WeaponData weapon, Vector3 origin, Vector3 direction, float click_distance, int damage)
    {
        int projectile_count = Mathf.Max(1, weapon.weapon_projectiles);
        float side_spacing = weapon.weapon_rebound * side_spacing_multiplier;
        float speed = slot.projectile_speed > 0f ? slot.projectile_speed : 15f; // 슬롯에 값이 없으면 기본 속도로 폴백
        float size = weapon.weapon_atsize > 0f ? weapon.weapon_atsize : 1f;
        bool can_penetrate = weapon.weapon_penetration;

        // 지연 폭발 무기는 날아가는 동안만 작은 크기를 쓰고, 원래 공격범위는 폭발 시점에 적용
        bool delayed_blast = IsDelayedBlastWeapon(weapon.weapon_id);
        float travel_size = delayed_blast ? Mathf.Max(0.01f, delayed_blast_travel_size) : size;

        // 폭발화기는 기본적으로 최대 사거리(weapon_range)에서 터지지만,
        // 사거리 안쪽을 클릭했다면 그 클릭 지점에서 조기 폭발한다.
        float travel_range = weapon.weapon_range > 0f ? weapon.weapon_range : 20f;
        if (delayed_blast && click_distance > 0f && click_distance < travel_range)
        {
            travel_range = click_distance;
        }

        // 진행 방향(direction) 기준으로 옆(좌우)을 가리키는 벡터. X-Y 평면에서 90도 회전.
        Vector3 side_axis = new Vector3(-direction.y, direction.x, 0f);

        // 프리팹이 기본적으로 오른쪽(Vector3.right)을 바라본다고 가정하고 방향에 맞게 회전
        // 프리팹이 다른 축을 정면으로 쓴다면 Vector3.right 부분만 바꾸면 됨
        Quaternion shot_rotation = Quaternion.FromToRotation(Vector3.right, direction);

        for (int i = 0; i < projectile_count; i++)
        {
            float side_offset = 0f;
            if (projectile_count > 1)
            {
                // 발마다 반동(weapon_rebound)만큼 옆으로 위치가 벌어짐 (이 발사 1번에 한해서만 적용, 다음 클릭엔 다시 가운데부터 계산)
                side_offset = side_spacing * (i - (projectile_count - 1) / 2f);
            }

            Vector3 spawn_position = origin + side_axis * side_offset;

            GameObject obj = Instantiate(projectile_prefab, spawn_position, shot_rotation);
            Projectile projectile = obj.GetComponent<Projectile>();
            if (projectile == null) projectile = obj.AddComponent<Projectile>();

            projectile.Launch(direction, speed, damage, travel_range, travel_size, can_penetrate);

            if (delayed_blast)
            {
                projectile.SetDelayedBlast(size, delayed_blast_explode_on_contact, delayed_blast_visual_duration);
            }
        }
    }

    // weapon_id가 지연 폭발 무기 ID 범위에 들어가는지 확인
    private bool IsDelayedBlastWeapon(int weapon_id)
    {
        foreach (WeaponIdRange range in delayed_blast_weapon_ids)
        {
            if (weapon_id >= range.min_id && weapon_id <= range.max_id) return true;
        }
        return false;
    }

    // 마우스 스크린 좌표를 Z = plane_z 평면 위의 월드 좌표로 변환
    private Vector3 GetMouseWorldPointOnPlane(float plane_z)
    {
        Vector2 mouse_screen_pos = Mouse.current.position.ReadValue();
        Ray ray = main_camera.ScreenPointToRay(mouse_screen_pos);

        Plane plane = new Plane(Vector3.forward, new Vector3(0f, 0f, plane_z));
        if (plane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }

        return ray.origin; // 평면과 교차하지 않는 예외 상황 폴백
    }
}
