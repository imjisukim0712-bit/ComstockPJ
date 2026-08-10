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
/// - weapon_atk        : 투사체 1발당 데미지. <b>등급별 상승분이 데이터에 이미 반영</b>되어 있다
///                       (무기는 등급마다 별도 행을 갖는다 - 배율을 곱하지 않는다)
/// - weapon_atsp       : 공격속도 → 대기시간 = 1 / atsp 초.
///                       <b>대기시간은 발사 동작이 끝난 뒤부터 흐른다</b>(사용자 확정 사항).
///                       빔처럼 지속시간이 있는 무기는 (지속시간 + 대기시간)이 한 주기가 된다
/// - weapon_range      : 탄이 날아가는 최대 거리. 빔은 빔 길이, 근접은 스윙 반경
/// - weapon_detect     : 적을 감지해 발사를 시작하는 거리. 사거리와 <b>별개 필드</b>이며 사거리로 잘린다
///                       (둘 다 무기 소켓 파츠 등급의 배율이 곱해진다 - ModdingManager.GetWeaponSocketModifiers)
/// - weapon_speed      : 투사체 이동 속도
/// - weapon_rotspeed   : 조준 방향으로 돌아가는 속도(도/초)
/// - weapon_atsize     : 투사체 크기(스케일). 빔에서는 빔의 반폭
/// - weapon_aim        : 탄퍼짐 반각(도) → <b>투사체 하나하나가 개별로</b> 흔들린다
/// - weapon_rebound    : 다중탄이 부채꼴로 벌어지는 탄 사이 각도 간격(도)
/// - weapon_projectiles: 한 번에 발사되는 투사체 개수
/// - weapon_pierce     : 관통 횟수 (0 = 없음 / N = N회 / -1 = 무제한)
/// - weapon_pierce_chance : 관통 발동 확률 → 투사체마다 따로 굴린다(산탄 8발이 각각 판정)
/// - weapon_splash     : 착탄 시 범위 피해 반경. 0보다 크면 폭발 무기가 된다
/// - weapon_defignore  : 적 방어력을 무시하는 비율(0~1)
/// - weapon_knockback  : 적중한 적을 밀어내는 초기 속도
/// - weapon_duration   : 빔의 지속 시간(초)
/// - weapon_firemode   : 투사체 / 빔 / 근접 스윙
/// - weapon_imgscale   : 손 이미지 크기 배율
/// - weapon_imgangle   : 이미지에 그려진 총구 방향과 조준각의 차이를 메우는 보정각
/// - weapon_tanhwan    : 발사할 투사체 프리팹 이름 → projectile_prefabs 목록에서 같은 이름을 찾아 사용
///
/// PlayerRobotController(로봇 데이터테이블)의 스탯도 함께 반영한다:
/// - robot_atk   : <b>발사 1회당</b> 더해지는 값을 투사체 개수로 나눠 균등 배분한다.
///                 투사체마다 통째로 더하면 산탄총(8발)만 8배로 이득을 보기 때문이다
/// - robot_cc/cd : 0~100 랜덤값이 robot_cc 이하면 치명타 → 데미지 = 데미지 + 데미지 * robot_cd
///
/// 소켓 개수는 현재 인스펙터에 등록된 weapon_slots 그대로 사용한다. 소켓 개수·타입을
/// 머리 파츠가 강제하는 규칙은 로봇 모딩 시스템(Phase 4)에서 연결한다.
///
/// 전제: 게임플레이는 X-Y 평면만 사용 (Z축 미사용) → PlayerRobotController와 동일한 규칙
/// </summary>
[DefaultExecutionOrder(10000)]
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

        [Tooltip("무기 데이터의 weapon_speed가 0일 때만 쓰이는 예비 투사체 속도")]
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
        [Tooltip("무기 데이터의 weapon_rotspeed가 0일 때만 쓰이는 예비 회전 속도(초당 도). " +
                 "권장 540. 0 이하로 두면 즉시 회전한다")]
        public float rotation_speed_degrees_per_second;

        [Tooltip("조준이 타겟 방향과 이 각도(도) 안으로 들어와야 발사한다. 회전이 느린 무기가 " +
                 "엉뚱한 방향으로 쏘는 것을 막는다. 권장 25. 0 이하로 두면 각도와 무관하게 항상 발사한다")]
        public float fire_angle_tolerance_degrees;
    }

    [Serializable]
    public struct ProjectilePrefabEntry
    {
        [Tooltip("데이터테이블 weapon_tanhwan 컬럼에 적는 이름 (예: Bullets, Energy)")]
        public string prefab_name;

        [Tooltip("Assets/Prefebs 안의 투사체 프리팹")]
        public GameObject prefab;
    }

    // 무기 슬롯 하나가 가지는 실시간 발사 상태(쿨다운만 추적 - 탄약/재장전 없음)
    private class WeaponRuntimeState
    {
        // 다음 발사가 가능해지는 시각. "발사 동작 종료 시각 + 대기시간"으로 계산한다.
        public float next_fire_time;
    }

    [Header("장착 무기 소켓 (머리 파츠가 개수/타입을 정하는 규칙은 Phase 4에서 연결)")]
    [SerializeField] private List<WeaponSlot> weapon_slots = new List<WeaponSlot>();

    [Header("투사체 프리팹 목록 (데이터테이블 weapon_tanhwan 이름 ↔ 프리팹)")]
    [Tooltip("Assets/Prefebs 안의 투사체 프리팹을 이름과 함께 등록. 여기 없는 이름은 Resources 폴더에서도 찾아본다")]
    [SerializeField] private List<ProjectilePrefabEntry> projectile_prefabs = new List<ProjectilePrefabEntry>();

    [Header("폭발 연출")]
    [Tooltip("폭발(weapon_splash > 0) 범위를 화면에 잠깐 보여주는 시간(초). 0이면 연출 없이 즉시 사라짐")]
    [SerializeField] private float blast_visual_duration = 0.08f;

    [Header("감지거리 상한")]
    [Tooltip("소켓 파츠 배율까지 곱한 뒤에도 이 거리(유닛)를 넘겨 적을 조준하지 않는다. " +
             "화면 밖의 보이지 않는 적과 교전하는 것을 막기 위한 값으로, 직교 카메라 세로 가시 반경에 맞춘다.\n" +
             "2026-08-10 카메라를 FHD 기준(orthographicSize=5.4, 1유닛=100px)으로 맞추면서 " +
             "세로 가시 반경이 8.66 → 5.4가 되어 8.5 → 5.3으로 함께 낮췄다")]
    [SerializeField] private float max_detect_range = 5.3f;

    [Header("빔 연출용 스프라이트")]
    [Tooltip("빔 무기(weapon_firemode=Beam)가 늘려서 사용할 Resources 폴더의 스프라이트 이름")]
    [SerializeField] private string beam_sprite_name = "Energy";

    [Header("구르기(대시) 중 무기 자세")]
    [Tooltip("구르는 동안 무기 리그 포인트를 옮길 위치(Player 로컬 기준, 머리 위)")]
    [SerializeField] private Vector3 roll_rig_local_position = new Vector3(0f, 4.3f, 0f);
    [Tooltip("두 무기가 완전히 겹쳐 보이지 않도록, 원래 좌/우 위치 부호(원래 x가 음수/양수였는지)에 " +
             "따라 이 값만큼 좌우로 벌려서 배치한다")]
    [SerializeField] private float roll_rig_lateral_spread = 0.5f;

    private PlayerRobotController player_stats; // 로봇 공격력/치명타 보정치를 가져오는 용도
    private readonly Dictionary<int, Vector3> roll_home_local_position = new Dictionary<int, Vector3>();
    private bool was_rolling;
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

    // 슬롯별로 마지막에 정상 확인한 무기 이미지. 다른 공격 연출이나 잘못된 런타임 참조가
    // 손 렌더러의 sprite를 바꾸더라도 렌더 직전에 이 기준으로 되돌린다.
    private readonly Dictionary<int, Sprite> expected_weapon_sprite_by_slot = new Dictionary<int, Sprite>();

    // 무기 이미지 원본 픽셀 크기가 이미지마다 제각각이라(예: SMG 1080px vs 기관총류 480px)
    // 같은 Transform 스케일을 써도 화면에 보이는 크기가 크게 달라지는 문제가 있었다.
    // 그래서 스프라이트를 바꿔 낄 때마다 "화면에 보이는 실제 크기"가 항상 이 값(월드 단위)이
    // 되도록 스케일을 자동 보정한다. 값은 기존에 잘 보이던 기관총 이미지 기준
    // (스프라이트 4.8유닛 크기 x 기존 지정 스케일 0.6)으로 역산한 것.
    private const float TargetHandImageSize = 4.8f * 0.6f;

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

        // 데이터 매니저가 아직 준비되지 않은 첫 프레임에도 씬에 저장된 정상 기본 무기를
        // 보호할 수 있도록 현재 이미지를 먼저 기준값으로 잡는다.
        for (int i = 0; i < weapon_slots.Count; i++)
        {
            SpriteRenderer renderer = weapon_slots[i].hand_sprite_renderer;
            if (renderer != null && renderer.sprite != null)
                expected_weapon_sprite_by_slot[i] = renderer.sprite;
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
        CacheRollHomePositions();
    }

    // 구르는 동안 리그 포인트를 머리 위로 옮겼다가 되돌리려면 원래 위치를 기억해둬야 한다.
    // 로테이션은 평소에도 매 프레임 새로 계산되므로(UpdateSlot) 따로 저장할 필요가 없다.
    private void CacheRollHomePositions()
    {
        roll_home_local_position.Clear();
        for (int i = 0; i < weapon_slots.Count; i++)
        {
            Transform pivot = weapon_slots[i].rig_point != null ? weapon_slots[i].rig_point : weapon_slots[i].muzzle_point;
            if (pivot != null) roll_home_local_position[i] = pivot.localPosition;
        }
    }

    /// <summary>
    /// 런 시작 시점의 장착 상태를 RunState에 반영한다. 등급은 무기 데이터 행이 직접 들고 있으므로
    /// (등급마다 별도 행이 존재한다) weapon_grade를 그대로 읽어온다.
    /// (RunState.Reset()은 PlayerRobotController.Awake에서 호출되므로 Start 시점엔 이미 비워져 있다)
    /// </summary>
    private void SyncRunStateFromInspectorSlots()
    {
        RunState.EquippedWeapons.Clear();

        for (int i = 0; i < weapon_slots.Count; i++)
        {
            int weapon_id = weapon_slots[i].weapon_id;

            ItemGrade grade = ItemGrade.Normal;
            if (weapon_data_by_slot.TryGetValue(i, out WeaponData data)) grade = data.weapon_grade;

            RunState.EquippedWeapons.Add(new RunState.EquippedWeapon
            {
                WeaponId = weapon_id,
                Grade = grade
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
        // 데이터 오타나 누락으로 로드에 실패했을 때 기존 정상 무기 이미지까지 지우지 않는다.
        // 씬 기본 이미지 또는 직전에 정상 장착된 이미지가 그대로 남아 복구 가능한 상태를 유지한다.
        if (sprite == null) return;

        expected_weapon_sprite_by_slot[slot_index] = sprite;
        slot.hand_sprite_renderer.sprite = sprite;

        // 원본 이미지 크기가 제각각이라(예: 1536px 총기류 vs 250px 근접무기) 매번
        // 화면에 보이는 크기가 TargetHandImageSize로 일정해지도록 스케일을 다시 계산한다.
        // 그 위에 무기별 개별 배율(weapon_imgscale)을 추가로 곱한다.
        float max_dim = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y, 0.0001f);
        float normalized_scale = TargetHandImageSize / max_dim;
        slot.hand_sprite_renderer.transform.localScale = Vector3.one * (normalized_scale * data.ImageScale);
    }

    private void LateUpdate()
    {
        // 조준 회전·좌우 반전·구르기 자세는 건드리지 않고 이미지 참조만 확정한다.
        // 특히 Enemy_zombie/ZombieAttack 또는 다른 로봇 파츠가 손 렌더러에 잘못 들어가도
        // 실제 카메라가 그리기 전 마지막 단계에서 장착 무기 원본으로 복구된다.
        for (int i = 0; i < weapon_slots.Count; i++)
        {
            SpriteRenderer renderer = weapon_slots[i].hand_sprite_renderer;
            if (renderer == null) continue;
            if (!expected_weapon_sprite_by_slot.TryGetValue(i, out Sprite expected) || expected == null) continue;

            if (renderer.sprite != expected) renderer.sprite = expected;
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
    /// 등급은 무기 데이터 행이 직접 들고 있으므로(등급마다 별도 행) 따로 받지 않는다.
    /// </summary>
    /// <returns>장착에 성공하면 true</returns>
    public bool EquipWeapon(int socketIndex, int weaponId)
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
            RunState.EquippedWeapons.Add(new RunState.EquippedWeapon { WeaponId = 0, Grade = ItemGrade.Normal });
        }

        RunState.EquippedWeapons[socketIndex] = new RunState.EquippedWeapon
        {
            WeaponId = weaponId,
            Grade = data.weapon_grade
        };

        RefreshWeaponVisual(socketIndex);

        // 교체 직후 이전 무기의 남은 쿨다운이 그대로 이어지지 않도록 초기화한다.
        GetOrCreateRuntimeState(socketIndex).next_fire_time = 0f;

        return true;
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

    // 무기 소켓 파츠(등급)가 주는 사거리/감지거리/회전속도 배율. 매 슬롯마다 조회하지 않도록
    // 프레임당 한 번만 읽어둔다(소켓 파츠는 현재 모든 소켓에 공통 적용된다).
    private ModdingManager.SocketModifiers socket_modifiers = ModdingManager.SocketModifiers.Identity;

    private void Update()
    {
        // 게임오버/승리 이후, 그리고 정비 화면(AI 코어/로봇 정비/상점)이 열려 있는 동안에는
        // 조준/발사 모두 정지 - 정비 중에는 인게임이 완전히 멈춰 있어야 한다(사용자 확정 사항).
        if (GameOverManager.IsGameOver || GameWinManager.IsGameWon || GameFlowManager.IsIntermission) return;

        // 구르는 동안에는 무기가 머리 위로 올라가 캐릭터와 함께 돌 뿐, 조준·발사는 완전히
        // 멈춘다(빠르게 이동하는 대신 잠깐 공격을 못 하는 패널티 - 사용자 확정 사항).
        bool rolling = player_stats != null && player_stats.IsDashing;
        if (rolling)
        {
            ApplyRollPoseToAllSlots();
            was_rolling = true;
            return;
        }

        if (was_rolling)
        {
            RestoreRollHomePositions();
            was_rolling = false;
        }

        socket_modifiers = ModdingManager.Instance != null
            ? ModdingManager.Instance.GetWeaponSocketModifiers()
            : ModdingManager.SocketModifiers.Identity;

        for (int i = 0; i < weapon_slots.Count; i++)
        {
            UpdateSlot(i);
        }
    }

    /// <summary>구르는 동안 모든 소켓의 리그 포인트를 머리 위로 옮기고, 캐릭터와 같은 각도로 돌린다.
    /// 원래 좌/우 위치(x 부호)만큼 살짝 벌려서 두 무기가 완전히 겹쳐 보이지 않게 한다.</summary>
    private void ApplyRollPoseToAllSlots()
    {
        float spin = player_stats.DashSpinDegrees;
        for (int i = 0; i < weapon_slots.Count; i++)
        {
            Transform pivot = weapon_slots[i].rig_point != null ? weapon_slots[i].rig_point : weapon_slots[i].muzzle_point;
            if (pivot == null) continue;

            float side = roll_home_local_position.TryGetValue(i, out Vector3 home) ? Mathf.Sign(home.x) : 0f;
            pivot.localPosition = roll_rig_local_position + new Vector3(side * roll_rig_lateral_spread, 0f, 0f);
            pivot.rotation = Quaternion.Euler(0f, 0f, spin);
        }
    }

    /// <summary>구르기가 끝난 직후 리그 포인트를 원래 위치로 되돌린다(회전은 다음 프레임 UpdateSlot이 알아서 다시 계산한다).</summary>
    private void RestoreRollHomePositions()
    {
        for (int i = 0; i < weapon_slots.Count; i++)
        {
            Transform pivot = weapon_slots[i].rig_point != null ? weapon_slots[i].rig_point : weapon_slots[i].muzzle_point;
            if (pivot != null && roll_home_local_position.TryGetValue(i, out Vector3 home)) pivot.localPosition = home;
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

        // 감지거리 = 적을 감지해 발사를 시작하는 거리. 탄이 날아가는 최대 거리(사거리)와는
        // 별개이며, 둘 다 무기 기본값(weapon_range)에 소켓 등급 배율을 곱해서 얻는다.
        float detect_range = GetDetectRange(weapon);
        EnemyUnit target = FindNearestEnemyInRange(pivot.position, detect_range);

        if (target == null)
        {
            float rest_angle = RotatePivotTowards(slot, weapon, pivot, slot.rest_rotation_degrees);
            ApplyAngleFlip(slot, rest_angle, false);
            return;
        }

        Vector3 direction = target.transform.position - pivot.position;
        direction.z = 0f; // X-Y 평면만 사용

        if (direction.sqrMagnitude > 0.0001f)
        {
            // weapon_imgangle: 무기 그림마다 총구가 그려진 각도가 달라서, 무기를 바꾸면
            // 슬롯 보정각(rotation_offset_degrees)만으로는 총구가 타겟을 향하지 않는다.
            float target_angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
                                 + slot.rotation_offset_degrees + weapon.weapon_imgangle;
            float current_angle = RotatePivotTowards(slot, weapon, pivot, target_angle);
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

    /// <summary>탄이 실제로 날아가는 최대 거리 = 무기 사거리 x 소켓 등급의 사거리 배율.</summary>
    private float GetTravelRange(WeaponData weapon)
    {
        return weapon.TravelRange * socket_modifiers.Range;
    }

    /// <summary>
    /// 적을 감지해 발사를 시작하는 거리 = 무기 감지거리(weapon_detect) x 소켓 등급의 감지거리 배율.
    /// 두 가지로 한 번 더 잘린다:
    /// 1) 사거리 - 감지한 적에게 탄이 닿아야 의미가 있다
    /// 2) max_detect_range - 화면 밖의 보이지 않는 적과 교전하지 않도록 하는 상한
    /// </summary>
    private float GetDetectRange(WeaponData weapon)
    {
        float detect = weapon.DetectRange * socket_modifiers.DetectRange;
        detect = Mathf.Min(detect, GetTravelRange(weapon));

        if (max_detect_range > 0f) detect = Mathf.Min(detect, max_detect_range);
        return detect;
    }

    /// <summary>
    /// 무기 피벗을 목표 각도 쪽으로 무기의 회전 속도(x 소켓 등급 배율)만큼만 돌리고,
    /// 이번 프레임에 실제로 적용된 각도를 돌려준다.
    /// 무기 데이터에 회전 속도가 없으면 슬롯 값으로 폴백하고, 그것도 0 이하면 즉시 스냅한다.
    /// </summary>
    private float RotatePivotTowards(WeaponSlot slot, WeaponData weapon, Transform pivot, float target_angle)
    {
        float base_speed = weapon.weapon_rotspeed > 0f
            ? weapon.weapon_rotspeed
            : slot.rotation_speed_degrees_per_second;

        float applied_angle;

        if (base_speed <= 0f)
        {
            applied_angle = target_angle;
        }
        else
        {
            float current_angle = pivot.eulerAngles.z;
            float speed = base_speed * socket_modifiers.RotationSpeed;
            applied_angle = Mathf.MoveTowardsAngle(current_angle, target_angle, speed * Time.deltaTime);
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
        if (Time.time < state.next_fire_time) return; // 대기시간 중

        Vector3 fire_origin = slot.muzzle_point.position;
        Vector3 to_target = target.transform.position - fire_origin;
        to_target.z = 0f; // Z축 미사용 규칙 - 방향은 X-Y 평면 안에서만 계산
        float target_distance = to_target.magnitude; // 폭발 무기가 조기 폭발할지 판단하는 데 사용

        Vector3 aim_direction = to_target.sqrMagnitude > 0.0001f ? to_target.normalized : Vector3.right;

        // 최종 데미지 = weapon_atk + (robot_atk를 투사체 수로 나눈 값), 그리고 robot_cc/cd(치명타) 적용.
        // 여러 발이 나가는 무기는 발사 1회에 한 번만 치명타를 굴려 모든 탄에 동일하게 적용한다.
        int damage = ComputeDamage(weapon);

        // 발사 동작이 지속되는 시간. 빔만 0보다 크고 나머지는 즉발이다.
        float attack_duration = 0f;

        switch (weapon.weapon_firemode)
        {
            case WeaponFireMode.Beam:
                FireBeam(weapon, fire_origin, aim_direction, damage);
                attack_duration = Mathf.Max(0f, weapon.weapon_duration);
                break;

            case WeaponFireMode.MeleeSwing:
                // 근접은 투사체를 만들지 않고 총구 앞 부채꼴을 즉시 판정한다
                MeleeSwing.Execute(fire_origin, aim_direction, GetTravelRange(weapon), damage,
                                   weapon.weapon_defignore, weapon.weapon_knockback);
                break;

            default:
                // 데이터테이블의 weapon_tanhwan(발사 탄환) 이름으로 투사체 프리팹 결정
                GameObject projectile_prefab = ResolveProjectilePrefab(weapon, slot);
                if (projectile_prefab == null) return;

                FireProjectiles(projectile_prefab, slot, weapon, fire_origin, aim_direction, target_distance, damage);
                break;
        }

        // 대기시간은 <b>발사 동작이 끝난 뒤부터</b> 흐른다(사용자 확정 사항).
        // 덕분에 3초짜리 빔은 3초 + 대기시간이 한 주기가 되어 빔이 여러 개 겹치지 않는다.
        float cooldown = weapon.weapon_atsp > 0f ? 1f / weapon.weapon_atsp : 1f;
        state.next_fire_time = Time.time + attack_duration + cooldown;
    }

    /// <summary>
    /// 최종 투사체 데미지 = weapon_atk + (robot_atk / 투사체 개수).
    /// robot_cc(치명타 확률, 0~100) 판정에 성공하면 데미지 = 데미지 + 데미지 * robot_cd.
    ///
    /// robot_atk를 투사체 개수로 나누는 이유: 예전처럼 투사체마다 통째로 더하면
    /// 8발이 나가는 산탄총만 robot_atk를 8배로 받아가고, 1발짜리 저격총은 거의 이득이 없다.
    /// 무기 등급 배율은 곱하지 않는다 - 등급별 공격력이 데이터 행에 이미 반영되어 있다.
    /// </summary>
    private int ComputeDamage(WeaponData weapon)
    {
        float robot_atk = player_stats != null ? player_stats.Atk : 0f;
        float damage = weapon.weapon_atk + robot_atk / weapon.ProjectileCount;

        if (player_stats != null && player_stats.Cc > 0f)
        {
            float crit_roll = UnityEngine.Random.Range(0f, 100f);
            if (crit_roll <= player_stats.Cc)
            {
                damage += damage * player_stats.Cd;
            }
        }

        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    /// <summary>지속시간 동안 직선 범위를 태우는 빔을 만든다(플라즈마 캐논).</summary>
    private void FireBeam(WeaponData weapon, Vector3 origin, Vector3 direction, int total_damage)
    {
        Sprite visual = ResolveWeaponSprite(beam_sprite_name, weapon);

        BeamProjectile.Fire(visual, origin, direction, GetTravelRange(weapon), weapon.ProjectileSize,
                            total_damage, weapon.weapon_duration, weapon.weapon_defignore, weapon.weapon_knockback);
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

    /// <summary>
    /// weapon_projectiles(발사 개수)만큼 투사체를 생성한다. 2발 이상이면 <b>부채꼴로</b> 벌어진다
    /// (weapon_rebound = 탄 사이 각도 간격). 예전에는 같은 방향으로 평행하게 나가서
    /// 산탄총 8발이 "흩어지는 산탄"이 아니라 "벽처럼 나란한 줄"로 보였다.
    ///
    /// 탄퍼짐(weapon_aim)과 확률 관통(weapon_pierce_chance)은 <b>탄마다 따로</b> 굴린다.
    /// 그래야 "탄마다 60% 확률로 1번 관통" 같은 원안 스펙이 그대로 재현된다.
    /// </summary>
    private void FireProjectiles(GameObject projectile_prefab, WeaponSlot slot, WeaponData weapon, Vector3 origin, Vector3 direction, float target_distance, int damage)
    {
        int projectile_count = weapon.ProjectileCount;

        float speed = weapon.weapon_speed > 0f
            ? weapon.weapon_speed
            : (slot.projectile_speed > 0f ? slot.projectile_speed : WeaponData.DefaultProjectileSpeed);

        // 폭발 무기는 기본적으로 최대 사거리에서 터지지만,
        // 타겟이 사거리 안쪽에 있다면 그 지점에서 조기 폭발한다.
        float travel_range = GetTravelRange(weapon);
        if (weapon.weapon_splash > 0f && target_distance > 0f && target_distance < travel_range)
        {
            travel_range = target_distance;
        }

        float base_angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        for (int i = 0; i < projectile_count; i++)
        {
            // 부채꼴 배치: 가운데를 기준으로 좌우 대칭으로 벌린다
            float angle_offset = projectile_count > 1
                ? weapon.weapon_rebound * (i - (projectile_count - 1) / 2f)
                : 0f;

            // 탄퍼짐은 탄 하나하나가 개별로 흔들린다
            if (weapon.weapon_aim > 0f)
            {
                angle_offset += UnityEngine.Random.Range(-weapon.weapon_aim, weapon.weapon_aim);
            }

            float shot_angle = base_angle + angle_offset;
            float radians = shot_angle * Mathf.Deg2Rad;
            Vector3 shot_direction = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);

            // 프리팹이 기본적으로 오른쪽(Vector3.right)을 바라본다고 가정하고 방향에 맞게 회전
            GameObject obj = Instantiate(projectile_prefab, origin, Quaternion.Euler(0f, 0f, shot_angle));
            Projectile projectile = obj.GetComponent<Projectile>();
            if (projectile == null) projectile = obj.AddComponent<Projectile>();

            projectile.Launch(new Projectile.Spec
            {
                Direction = shot_direction,
                Speed = speed,
                Damage = damage,
                MaxRange = travel_range,
                Size = weapon.ProjectileSize,
                PierceCount = weapon.RollPierceCount(), // 탄마다 따로 확률 관통 판정
                SplashRadius = weapon.weapon_splash,
                DefIgnore = weapon.weapon_defignore,
                Knockback = weapon.weapon_knockback,
                BlastVisualDuration = blast_visual_duration
            });
        }
    }
}
