using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어가 장착한 모든 무기 소켓을 자동공격으로 관리한다.
/// 각 소켓은 서로 독립적으로 동작한다: 자기 사거리(weapon_range) 안에 있는 가장 가까운
/// 적을 찾아 그 방향으로 회전하고, 자기 쿨다운(weapon_atsp)이 끝나면 자동으로 발사한다.
/// 마우스 조준/클릭, 무기 전환 키, 탄약·재장전 개념은 없다(자동공격 뱀서라이크 전환 결정).
///
/// 무기 데이터테이블(WeaponData) 각 필드 사용 방식:
/// - weapon_atk        : 명중 시 데미지
/// - weapon_atsp       : 공격속도 → 다음 발사까지의 쿨다운 (1 / atsp 초)
/// - weapon_range      : 투사체 최대 사거리이자 자동 타겟팅 탐지 반경
/// - weapon_atsize     : 투사체 크기(스케일)
/// - weapon_aim        : 조준 정확도 → 타겟 방향이 최대 이 각도(도)만큼 무작위로 벗어남
/// - weapon_rebound    : 반동 → 한 번의 발사에서 여러 발이 나갈 때, 발마다 진행 방향은 그대로 두고 옆으로 벌어지는 간격(평행 발사)
/// - weapon_projectiles: 한 번에 발사되는 투사체 개수
/// - weapon_penetration: 관통 여부 (0 = 첫 충돌 시 소멸 / 1 = 관통, 충돌해도 유지)
/// - weapon_tanhwan    : 발사할 투사체 프리팹 이름 → projectile_prefabs 목록에서 같은 이름을 찾아 사용
/// - weapon_capacity/weapon_reload : 더 이상 사용하지 않음(탄약·재장전 제거 결정, 값은 데이터에만 남아있음)
///
/// PlayerRobotController(로봇 데이터테이블)의 스탯도 함께 반영한다:
/// - robot_atk   : 최종 데미지 = weapon_atk + robot_atk
/// - robot_cc/cd : 0~100 랜덤값이 robot_cc 이하면 치명타 → 데미지 = 데미지 + 데미지 * robot_cd
///
/// delayed_blast_weapon_ids 범위(기본 300400~300499, 수류탄류)에 속한 무기는
/// weapon_atsize를 접촉 판정 크기로 쓰지 않고, 작은 크기로 날아가다가
/// weapon_range(최대 사거리) 또는 타겟 지점에서 weapon_atsize 범위에 한 번에 데미지를 준다.
///
/// 투사체 속도는 데이터테이블에 없는 값이라 무기 슬롯별로 인스펙터에서 직접 지정한다.
/// 소켓 개수는 현재 인스펙터에 등록된 weapon_slots 그대로 사용한다. 소켓 개수·타입을
/// 머리 파츠가 강제하는 규칙은 로봇 모딩 시스템(Phase 4)에서 연결한다.
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

        [Tooltip("자동 조준 중에만 사용되는 보정각(도). 원본 이미지의 총구가 그려진 각도가 " +
                 "atan2로 계산한 방향과 다를 때 이 값을 더해서 실제 총구가 타겟을 정확히 향하도록 보정한다. " +
                 "대기 자세(rest_rotation_degrees)와는 별개의 값이다")]
        public float rotation_offset_degrees;

        [Tooltip("사거리 안에 타겟이 없을 때 고정으로 보여줄 절대 회전각(도). " +
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

        // WeaponSlot은 struct라 필드 초기화식을 쓸 수 없다(C# 9). 기본값은 인스펙터에서 넣는다.
        [Tooltip("무기가 조준 방향으로 돌아가는 속도(초당 도). 즉시 홱 돌지 않고 이 속도로 서서히 돌린다. " +
                 "권장 540. 0 이하로 두면 예전처럼 즉시 회전한다")]
        public float rotation_speed_degrees_per_second;

        [Tooltip("조준이 타겟 방향과 이 각도(도) 안으로 들어와야 발사한다. 회전이 느린 무기가 " +
                 "엉뚱한 방향으로 쏘는 것을 막는다. 권장 25. 0 이하로 두면 각도와 무관하게 항상 발사한다")]
        public float fire_angle_tolerance_degrees;
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

    // 무기 슬롯 하나가 가지는 실시간 발사 상태(쿨다운만 추적 - 탄약/재장전 없음)
    private class WeaponRuntimeState
    {
        public float next_fire_time;
    }

    [Header("장착 무기 소켓 (머리 파츠가 개수/타입을 정하는 규칙은 Phase 4에서 연결)")]
    [SerializeField] private List<WeaponSlot> weapon_slots = new List<WeaponSlot>();

    [Header("투사체 프리팹 목록 (데이터테이블 weapon_tanhwan 이름 ↔ 프리팹)")]
    [Tooltip("Assets/Prefebs 안의 투사체 프리팹을 이름과 함께 등록. 여기 없는 이름은 Resources 폴더에서도 찾아본다")]
    [SerializeField] private List<ProjectilePrefabEntry> projectile_prefabs = new List<ProjectilePrefabEntry>();

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

    private PlayerRobotController player_stats; // 로봇 공격력/치명타 보정치를 가져오는 용도
    private readonly Dictionary<int, WeaponData> weapon_data_by_slot = new Dictionary<int, WeaponData>();
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
    private const float DefaultWeaponRange = 20f;

    private void Awake()
    {
        // ShootManager는 Player와 별개 오브젝트라 GetComponent가 아니라 태그로 찾는다.
        GameObject player_obj = GameObject.FindGameObjectWithTag("Player");
        if (player_obj != null) player_stats = player_obj.GetComponent<PlayerRobotController>();
        if (player_stats == null)
        {
            Debug.LogWarning("PlayerRobotController를 찾을 수 없습니다. 로봇 공격력/치명타 보정 없이 무기 기본 수치로만 발사합니다.");
        }

        foreach (var entry in projectile_prefabs)
        {
            if (string.IsNullOrWhiteSpace(entry.prefab_name) || entry.prefab == null) continue;
            prefab_by_name[entry.prefab_name.Trim()] = entry.prefab;
        }
    }

    private void Start()
    {
        // GameDataManager는 로컬 에셋을 Awake에서 동기 로드하므로 보통 이 시점엔 이미 IsLoaded지만,
        // 실행 순서가 어긋나는 경우를 대비해 이벤트 폴백도 유지한다.
        if (GameDataManager.Instance.IsLoaded)
        {
            RefreshAllWeaponData();
            RefreshAllWeaponVisuals();
        }
        else
        {
            GameDataManager.Instance.OnLoaded += HandleGameDataLoaded;
        }

        SyncRunStateFromInspectorSlots();
    }

    /// <summary>
    /// 런 시작 시점의 장착 상태를 RunState에 반영한다. 인스펙터에 미리 넣어둔 시작 무기는
    /// 상점을 거치지 않았으므로 전부 일반 등급(배율 1)으로 취급한다.
    /// (RunState.Reset()은 PlayerRobotController.Awake에서 호출되므로 Start 시점엔 이미 비워져 있다)
    /// </summary>
    private void SyncRunStateFromInspectorSlots()
    {
        RunState.EquippedWeapons.Clear();

        for (int i = 0; i < weapon_slots.Count; i++)
        {
            RunState.EquippedWeapons.Add(new RunState.EquippedWeapon
            {
                WeaponId = weapon_slots[i].weapon_id,
                Grade = ItemGrade.Normal,
                StatMultiplier = 1f
            });
        }
    }

    private void OnDestroy()
    {
        if (GameDataManager.Instance != null) GameDataManager.Instance.OnLoaded -= HandleGameDataLoaded;
    }

    private void HandleGameDataLoaded()
    {
        RefreshAllWeaponData();
        RefreshAllWeaponVisuals();
    }

    // 모든 소켓의 weapon_id로 GameDataManager에서 실제 스탯(WeaponData)을 다시 가져온다.
    private void RefreshAllWeaponData()
    {
        weapon_data_by_slot.Clear();

        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("GameDataManager.Instance가 없습니다. 씬에 DataManager 오브젝트가 있는지 확인하세요.");
            return;
        }

        for (int i = 0; i < weapon_slots.Count; i++)
        {
            int weapon_id = weapon_slots[i].weapon_id;
            if (GameDataManager.Instance.Weapons.TryGetValue(weapon_id, out WeaponData data))
            {
                weapon_data_by_slot[i] = data;
            }
            else
            {
                Debug.LogWarning($"무기ID {weapon_id}(소켓 {i})의 데이터를 찾을 수 없습니다. GameDataManager.Weapons에 해당 ID가 로드되었는지 확인하세요.");
            }
        }
    }

    /// <summary>
    /// 모든 소켓(선택된 소켓 개념 없음 - 전부 동시에 보이고 발사됨)의 이미지를
    /// 데이터테이블(weapon_lfwpimg/weapon_rgwpimg)에 맞춰 갱신한다.
    /// </summary>
    private void RefreshAllWeaponVisuals()
    {
        if (GameDataManager.Instance == null) return;

        for (int i = 0; i < weapon_slots.Count; i++)
        {
            RefreshWeaponVisual(i);
        }
    }

    // 소켓 하나의 무기 이미지를 갱신한다(상점에서 무기를 교체했을 때도 이것만 다시 부르면 된다).
    private void RefreshWeaponVisual(int slot_index)
    {
        if (GameDataManager.Instance == null) return;
        if (slot_index < 0 || slot_index >= weapon_slots.Count) return;

        WeaponSlot slot = weapon_slots[slot_index];
        if (slot.hand_sprite_renderer == null) return;

        if (!GameDataManager.Instance.Weapons.TryGetValue(slot.weapon_id, out WeaponData data)) return;

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

    /// <summary>현재 무기 소켓 개수(상점 UI가 "어느 소켓에 장착할지" 목록을 만들 때 사용).</summary>
    public int SocketCount => weapon_slots.Count;

    /// <summary>소켓에 현재 장착된 무기 정보를 가져온다(상점 UI 표시용).</summary>
    public bool TryGetSocketInfo(int socketIndex, out WeaponData weapon, out ItemGrade grade)
    {
        weapon = default;
        grade = ItemGrade.Normal;

        if (socketIndex < 0 || socketIndex >= weapon_slots.Count) return false;
        if (!weapon_data_by_slot.TryGetValue(socketIndex, out weapon)) return false;

        if (socketIndex < RunState.EquippedWeapons.Count) grade = RunState.EquippedWeapons[socketIndex].Grade;
        return true;
    }

    /// <summary>
    /// 상점에서 산 무기를 소켓에 즉시 장착(교체)한다. 기획서의 "무기 구매 = 소켓 즉시 교체"에 해당한다.
    /// 등급 배율(statMultiplier)은 이후 발사 계산에서 공격력/공격속도에 곱해진다.
    /// </summary>
    /// <returns>장착에 성공하면 true</returns>
    public bool EquipWeapon(int socketIndex, int weaponId, ItemGrade grade, float statMultiplier)
    {
        if (socketIndex < 0 || socketIndex >= weapon_slots.Count)
        {
            Debug.LogWarning($"무기 소켓 인덱스 {socketIndex}가 범위를 벗어났습니다(소켓 {weapon_slots.Count}개).");
            return false;
        }

        if (GameDataManager.Instance == null ||
            !GameDataManager.Instance.Weapons.TryGetValue(weaponId, out WeaponData data))
        {
            Debug.LogWarning($"무기ID {weaponId}의 데이터를 찾을 수 없어 장착하지 못했습니다.");
            return false;
        }

        // WeaponSlot은 struct라 List에서 꺼내 수정한 뒤 다시 넣어야 반영된다.
        WeaponSlot slot = weapon_slots[socketIndex];
        slot.weapon_id = weaponId;
        weapon_slots[socketIndex] = slot;

        weapon_data_by_slot[socketIndex] = data;

        while (RunState.EquippedWeapons.Count <= socketIndex)
        {
            RunState.EquippedWeapons.Add(new RunState.EquippedWeapon { WeaponId = 0, Grade = ItemGrade.Normal, StatMultiplier = 1f });
        }

        RunState.EquippedWeapons[socketIndex] = new RunState.EquippedWeapon
        {
            WeaponId = weaponId,
            Grade = grade,
            StatMultiplier = statMultiplier <= 0f ? 1f : statMultiplier
        };

        RefreshWeaponVisual(socketIndex);

        // 교체 직후 이전 무기의 남은 쿨다운이 그대로 이어지지 않도록 초기화한다.
        GetOrCreateRuntimeState(socketIndex).next_fire_time = 0f;

        return true;
    }

    // 소켓에 적용할 등급 스탯 배율. 상점을 거치지 않은 시작 무기는 1(일반 등급).
    private float GetStatMultiplier(int slot_index)
    {
        if (slot_index < 0 || slot_index >= RunState.EquippedWeapons.Count) return 1f;

        float multiplier = RunState.EquippedWeapons[slot_index].StatMultiplier;
        return multiplier <= 0f ? 1f : multiplier;
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
        // 게임오버/승리 이후, 그리고 정비 화면(AI 코어/로봇 정비/상점)이 열려 있는 동안에는
        // 조준/발사 모두 정지 - 정비 중에는 인게임이 완전히 멈춰 있어야 한다(사용자 확정 사항).
        if (GameOverManager.IsGameOver || GameWinManager.IsGameWon || GameFlowManager.IsIntermission) return;

        for (int i = 0; i < weapon_slots.Count; i++)
        {
            UpdateSlot(i);
        }
    }

    /// <summary>
    /// 소켓 하나를 처리한다: 사거리 내 최근접 적을 찾아 조준하고, 쿨다운이 끝났으면 발사한다.
    /// 타겟이 없으면 대기 자세(rest_rotation_degrees)로 되돌아간다.
    /// </summary>
    private void UpdateSlot(int slot_index)
    {
        if (!weapon_data_by_slot.TryGetValue(slot_index, out WeaponData weapon)) return;

        WeaponSlot slot = weapon_slots[slot_index];
        Transform pivot = slot.rig_point != null ? slot.rig_point : slot.muzzle_point;
        if (pivot == null) return;

        float range = weapon.weapon_range > 0f ? weapon.weapon_range : DefaultWeaponRange;
        EnemyUnit target = FindNearestEnemyInRange(pivot.position, range);

        if (target == null)
        {
            float rest_angle = RotatePivotTowards(slot, pivot, slot.rest_rotation_degrees);
            ApplyAngleFlip(slot, rest_angle, false);
            return;
        }

        Vector3 direction = target.transform.position - pivot.position;
        direction.z = 0f; // X-Y 평면만 사용

        if (direction.sqrMagnitude > 0.0001f)
        {
            float target_angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + slot.rotation_offset_degrees;
            float current_angle = RotatePivotTowards(slot, pivot, target_angle);
            ApplyAngleFlip(slot, current_angle, true);

            // 아직 타겟 쪽으로 다 돌지 못했으면 발사를 미룬다 - 그래야 "무기가 돌아가는 시간"이
            // 눈속임이 아니라 실제 사격 타이밍에도 반영된다.
            if (slot.fire_angle_tolerance_degrees > 0f &&
                Mathf.Abs(Mathf.DeltaAngle(current_angle, target_angle)) > slot.fire_angle_tolerance_degrees)
            {
                return;
            }
        }

        TryFireSlot(slot_index, slot, weapon, target);
    }

    /// <summary>
    /// 무기 피벗을 목표 각도 쪽으로 rotation_speed_degrees_per_second 속도만큼만 돌리고,
    /// 이번 프레임에 실제로 적용된 각도를 돌려준다.
    /// 속도가 0 이하면 예전처럼 즉시 목표 각도로 스냅한다.
    /// </summary>
    private static float RotatePivotTowards(WeaponSlot slot, Transform pivot, float target_angle)
    {
        float applied_angle;

        if (slot.rotation_speed_degrees_per_second <= 0f)
        {
            applied_angle = target_angle;
        }
        else
        {
            float current_angle = pivot.eulerAngles.z;
            applied_angle = Mathf.MoveTowardsAngle(current_angle, target_angle,
                                                   slot.rotation_speed_degrees_per_second * Time.deltaTime);
        }

        pivot.rotation = Quaternion.Euler(0f, 0f, applied_angle);
        return applied_angle;
    }

    // weapon_slots.Count가 커져도(최대 6소켓 예정, Phase 4에서 머리 파츠가 실제 개수를 정함)
    // EnemyUnit.Alive를 슬롯마다 순회하는 정도는 이 데모 규모에서 비용이 미미하다.
    private static EnemyUnit FindNearestEnemyInRange(Vector3 origin, float range)
    {
        EnemyUnit nearest = null;
        float nearest_sqr_dist = range * range;

        foreach (EnemyUnit enemy in EnemyUnit.Alive)
        {
            if (enemy == null || enemy.IsDead) continue;

            Vector3 diff = enemy.transform.position - origin;
            diff.z = 0f;
            float sqr_dist = diff.sqrMagnitude;

            if (sqr_dist <= nearest_sqr_dist)
            {
                nearest_sqr_dist = sqr_dist;
                nearest = enemy;
            }
        }

        return nearest;
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

    private WeaponRuntimeState GetOrCreateRuntimeState(int slot_index)
    {
        if (!runtime_state_by_slot.TryGetValue(slot_index, out WeaponRuntimeState state))
        {
            state = new WeaponRuntimeState();
            runtime_state_by_slot[slot_index] = state;
        }
        return state;
    }

    private void TryFireSlot(int slot_index, WeaponSlot slot, WeaponData weapon, EnemyUnit target)
    {
        if (slot.muzzle_point == null)
        {
            Debug.LogWarning("무기 소켓의 muzzle_point가 비어있습니다. 인스펙터에서 연결해주세요.");
            return;
        }

        WeaponRuntimeState state = GetOrCreateRuntimeState(slot_index);
        if (Time.time < state.next_fire_time) return; // weapon_atsp(공격속도) 쿨다운 중

        // 데이터테이블의 weapon_tanhwan(발사 탄환) 이름으로 투사체 프리팹 결정
        GameObject projectile_prefab = ResolveProjectilePrefab(weapon, slot);
        if (projectile_prefab == null) return;

        Vector3 fire_origin = slot.muzzle_point.position;
        Vector3 to_target = target.transform.position - fire_origin;
        to_target.z = 0f; // Z축 미사용 규칙 - 방향은 X-Y 평면 안에서만 계산
        float target_distance = to_target.magnitude; // 지연 폭발 무기가 조기 폭발할지 판단하는 데 사용

        Vector3 aim_direction = to_target.sqrMagnitude > 0.0001f ? to_target.normalized : Vector3.right;

        // weapon_aim(조준 정확도) → 타겟 방향을 좌우로 무작위로 흐트러뜨림
        if (weapon.weapon_aim > 0f)
        {
            float random_deviation = UnityEngine.Random.Range(-weapon.weapon_aim, weapon.weapon_aim);
            aim_direction = Quaternion.Euler(0f, 0f, random_deviation) * aim_direction;
        }

        // 상점에서 산 무기의 등급(수직 강화)은 공격력과 공격속도에 배율로 반영된다.
        float grade_multiplier = GetStatMultiplier(slot_index);

        // 최종 데미지 = weapon_atk + robot_atk, 그리고 robot_cc/cd(치명타) 적용.
        // 여러 발이 나가는 무기(weapon_projectiles > 1)는 발사 1회에 한 번만 치명타를 굴려 모든 탄에 동일하게 적용한다.
        int damage = ComputeDamage(weapon, grade_multiplier);

        FireProjectiles(projectile_prefab, slot, weapon, fire_origin, aim_direction, target_distance, damage);

        // weapon_atsp(공격속도)가 높을수록 다음 발사까지 대기시간이 짧아짐
        float attack_speed = weapon.weapon_atsp * grade_multiplier;
        float cooldown = attack_speed > 0f ? 1f / attack_speed : 1f;
        state.next_fire_time = Time.time + cooldown;
    }

    /// <summary>
    /// 최종 투사체 데미지 = (weapon_atk x 등급 배율) + robot_atk.
    /// robot_cc(치명타 확률, 0~100) 판정에 성공하면 데미지 = 데미지 + 데미지 * robot_cd.
    /// 등급 배율은 무기 자체의 성능에만 곱하고, 로봇 스탯(robot_atk)에는 곱하지 않는다.
    /// </summary>
    private int ComputeDamage(WeaponData weapon, float grade_multiplier)
    {
        float damage = weapon.weapon_atk * grade_multiplier + (player_stats != null ? player_stats.Atk : 0);

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

    // weapon_projectiles(발사 개수)만큼 투사체를 생성. 2발 이상이면 진행 방향은 그대로 두고,
    // weapon_rebound를 좌우 간격으로 사용해 옆으로 나란히(평행하게) 추가 발사한다.
    private void FireProjectiles(GameObject projectile_prefab, WeaponSlot slot, WeaponData weapon, Vector3 origin, Vector3 direction, float target_distance, int damage)
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
        // 타겟이 사거리 안쪽에 있다면 그 지점에서 조기 폭발한다.
        float travel_range = weapon.weapon_range > 0f ? weapon.weapon_range : DefaultWeaponRange;
        if (delayed_blast && target_distance > 0f && target_distance < travel_range)
        {
            travel_range = target_distance;
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
                // 발마다 반동(weapon_rebound)만큼 옆으로 위치가 벌어짐 (이 발사 1번에 한해서만 적용, 다음 발사엔 다시 가운데부터 계산)
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
}
