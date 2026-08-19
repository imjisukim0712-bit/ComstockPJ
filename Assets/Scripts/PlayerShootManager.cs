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
/// - weapon_range      : 탄이 날아가는 최대 거리. 빔은 빔 길이. <b>근접무기는 판정에 쓰이지 않는다</b>
///                       (2026-08-13 - 판정은 찌르는 칼 그림의 실제 범위로 한다. 근접의 range/detect는
///                        "이 거리에 적이 오면 찌르기를 시작한다"는 감지 거리 의미만 남았다)
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

        // 근접무기 "찌르기" 상태(StartMeleeThrustVisual/UpdateMeleeThrustVisual 참고).
        // 2026-08-13부터 데미지 판정도 이 연출에 묶여 있다 - 칼이 나가는 프레임마다 칼 그림이
        // 차지한 범위를 판정하므로, 아래 값들은 연출용이 아니라 판정 파라미터이기도 하다.
        public bool melee_thrust_active;
        public float melee_thrust_start_time;
        public float melee_thrust_duration;
        public float melee_thrust_distance;

        // 찌르는 동안 매 프레임 같은 값으로 판정하기 위해 발사 시점의 데미지 계산 결과를 들고 있는다.
        public int melee_damage;
        public float melee_def_ignore;
        public float melee_knockback;

        // 한 번의 찌르기에서 같은 적이 여러 프레임에 걸쳐 중복으로 맞지 않게 하는 집합.
        // 찌르기를 새로 시작할 때마다 비운다.
        public readonly HashSet<EnemyUnit> melee_hit_targets = new HashSet<EnemyUnit>();
        // 소켓의 "복귀 지점"을 부모 기준 로컬 좌표로 저장한다(2026-08-12 수정). 예전에는 월드
        // 좌표를 한 번 찍어서 그대로 썼는데, 찌르기가 재생되는 동안 캐릭터가 이동하면 그 월드
        // 좌표가 캐릭터를 따라가지 못해 "복귀 지점이 몸에서 떨어져 있는" 것처럼 보였다.
        public Vector3 melee_thrust_home_local;
        public Vector3 melee_thrust_direction;
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
        // 픽시 효과("모든 소켓이 근접이면 사거리 x2")가 소켓 장착 상태를 물어볼 수 있도록 등록한다.
        // HeadEffects가 스스로 FindObjectOfType을 돌리지 않게 하려는 것(매 발사마다 씬을 뒤지지 않는다).
        HeadEffects.RegisterShootManager(this);

        // 머리(로봇)가 정한 기본 장착 무기를 씬 값 위에 덮어쓴다. RefreshAllWeaponData보다
        // 먼저 와야 그 다음 줄이 새 weapon_id로 데이터를 읽어온다.
        ApplyHeadDefaultWeapons();

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

    /// <summary>
    /// 선택한 머리의 <see cref="PartsCatalog.HeadModdingInfo.defaultWeaponIds"/>를 소켓 0번부터
    /// 순서대로 장착하고, 남는 소켓은 비운다.
    ///
    /// 2026-08-19 이전에는 시작 무기가 <b>씬에 박혀</b> 있었다(Ground01의 weapon_slots[0] =
    /// 300901 기관단총). 머리마다 기본 무기가 다른 기획서를 반영하려면 데이터가 정해야 하므로
    /// 여기서 덮어쓴다. 데이터가 없는 머리(디버그 로봇 등)는 씬 값을 그대로 쓴다.
    ///
    /// <b>활성 소켓 수를 넘는 자리는 비운다</b> - 프라이빗 컴스톡(1소켓)처럼 소켓이 적은 머리로
    /// 시작할 때 씬에 남아 있던 무기가 그대로 발사되면 안 된다.
    /// </summary>
    private void ApplyHeadDefaultWeapons()
    {
        PartsCatalog catalog = ModdingManager.Instance != null ? ModdingManager.Instance.Catalog : null;
        if (catalog == null) return;

        int robotId = PlayerSession.SelectedRobotId;
        PartsCatalog.HeadModdingInfo info = catalog.GetHeadModdingInfo(robotId);

        int[] defaults = info.defaultWeaponIds;
        if (defaults == null || defaults.Length == 0) return;

        // 이 머리가 실제로 쓸 수 있는 소켓 수(씬 리깅 개수와 머리 값 중 작은 쪽).
        int active = Mathf.Min(weapon_slots.Count, Mathf.Max(0, info.weaponSocketCount));

        for (int i = 0; i < weapon_slots.Count; i++)
        {
            WeaponSlot slot = weapon_slots[i];
            slot.weapon_id = i < Mathf.Min(active, defaults.Length) ? defaults[i] : 0;
            weapon_slots[i] = slot;
        }
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

        for (int i = 0; i < SocketCount; i++)
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

            // weapon_id 0 이하 = 빈 소켓(무기 미장착). 정상 상태이므로 경고하지 않고 건너뛴다.
            // weapon_data_by_slot에 항목이 없으면 UpdateSlot이 알아서 발사를 건너뛴다.
            if (weapon_id <= 0) continue;

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
            // 로봇이 이 소켓을 갖지 않으면(ActiveSocketCount 밖) 이미지를 숨긴다 - 소켓이 물리적으로
            // 존재해도 머리 파츠가 정한 소켓 개수보다 많으면 화면에 무기가 들려 있으면 안 된다.
            if (i >= SocketCount)
            {
                if (weapon_slots[i].hand_sprite_renderer != null) weapon_slots[i].hand_sprite_renderer.enabled = false;
                continue;
            }

            // 켜고 끄는 판단은 RefreshWeaponVisual이 한다(빈 소켓이면 손 이미지를 끈다).
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

        if (!GameDataManager.Instance.Weapons.TryGetValue(slot.weapon_id, out WeaponData data))
        {
            // 빈 소켓(weapon_id 0 이하)이면 손 이미지를 끈다. 씬에 기본 무기 스프라이트가 그대로
            // 남아 있어서, 끄지 않으면 장착하지도 않은 무기가 계속 손에 보인다.
            // (weapon_id는 있는데 데이터를 못 찾은 경우는 데이터 오류이므로 아래 주석대로
            //  기존 이미지를 유지한 채 그냥 빠져나간다.)
            if (slot.weapon_id <= 0)
            {
                expected_weapon_sprite_by_slot.Remove(slot_index);
                slot.hand_sprite_renderer.enabled = false;
            }
            return;
        }

        // 빈 소켓에 무기를 새로 장착했을 때 다시 보이게 하려면 여기서 켜야 한다
        // (상점 구매 → EquipWeapon → RefreshWeaponVisual 경로가 이 지점을 지난다).
        slot.hand_sprite_renderer.enabled = true;

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

    /// <summary>씬에 물리적으로 리깅된 소켓 개수(RigingPoint 등이 실제로 배치된 개수). 인스펙터
    /// 설정 그대로이며, 로봇 종류와 무관하게 고정이다. ModdingManager.ActiveSocketCount가 이 값과
    /// 머리(로봇) 파츠의 weaponSocketCount 중 작은 값을 골라 "실제로 쓸 수 있는" 소켓 수를 정한다.</summary>
    public int RiggedSocketCount => weapon_slots.Count;

    /// <summary>현재 실제로 쓸 수 있는 무기 소켓 개수(상점/정비 UI가 목록을 만들 때 사용).
    /// 2026-08-12 "무기 소켓 개별화" 전에는 RiggedSocketCount와 항상 같았지만, 이제 머리(로봇)
    /// 파츠가 정한 소켓 개수가 더 적으면 그 값으로 잘린다(ModdingManager.ActiveSocketCount 참고).</summary>
    public int SocketCount => ModdingManager.Instance != null ? ModdingManager.Instance.ActiveSocketCount : weapon_slots.Count;

    /// <summary>이번 프레임에 소켓 중 하나라도 사거리 안의 적을 조준했는지. "위장 디스크"
    /// (비공격 시 이동속도 상승)가 참조한다.</summary>
    public bool IsTargetingEnemy { get; private set; }

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
    /// UI 조회용 - 이 소켓에 실제로 적용되는 사거리/감지거리(무기 기본값 x 소켓 파츠 배율,
    /// 감지거리는 사거리·화면 상한으로 한 번 더 잘린 최종값). 상점 상세 팝업이 "데이터 값"이
    /// 아니라 "지금 이 소켓에서 실제로 나가는 거리"를 보여줄 수 있도록 노출한다.
    /// </summary>
    public float GetEffectiveTravelRange(int socketIndex)
    {
        return TryGetSocketInfo(socketIndex, out WeaponData weapon, out _)
            ? GetTravelRange(weapon, socketIndex)
            : 0f;
    }

    /// <inheritdoc cref="GetEffectiveTravelRange"/>
    public float GetEffectiveDetectRange(int socketIndex)
    {
        return TryGetSocketInfo(socketIndex, out WeaponData weapon, out _)
            ? GetDetectRange(weapon, socketIndex)
            : 0f;
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

    /// <summary>소켓별로 독립된 무기 소켓 파츠(등급)가 주는 사거리/감지거리/회전속도 배율.
    /// 2026-08-12 "무기 소켓 개별화" 전에는 소켓 파츠가 모든 소켓에 공통 적용돼 프레임당
    /// 한 번만 읽어도 됐지만, 이제 소켓마다 다른 파츠를 낄 수 있어 소켓 인덱스별로 조회한다.</summary>
    private ModdingManager.SocketModifiers GetSocketModifiers(int slot_index)
    {
        return ModdingManager.Instance != null
            ? ModdingManager.Instance.GetWeaponSocketModifiers(slot_index)
            : ModdingManager.SocketModifiers.Identity;
    }

    private void Update()
    {
        // 게임오버/승리 이후, 그리고 정비 화면(AI 코어/로봇 정비/상점)이 열려 있는 동안에는
        // 조준/발사 모두 정지 - 정비 중에는 인게임이 완전히 멈춰 있어야 한다(사용자 확정 사항).
        if (GameOverManager.IsGameOver || GameWinManager.IsGameWon || GameFlowManager.IsIntermission || GameFlowManager.IsPaused) return;

        // 구르는 동안에는 무기가 머리 위로 올라가 캐릭터와 함께 돌 뿐, 조준·발사는 완전히
        // 멈춘다(빠르게 이동하는 대신 잠깐 공격을 못 하는 패널티 - 사용자 확정 사항).
        bool rolling = player_stats != null && player_stats.IsDashing;
        if (rolling)
        {
            // 찌르기 연출이 재생되던 중 구르기가 시작되면 취소한다. 취소하지 않으면 연출이 끝나는
            // 시점(t>=1)에 구르기 시작 전 위치(월드 좌표 스냅샷)로 되돌리려 시도하는데, 그 사이
            // 캐릭터가 구른 만큼 실제 위치와 어긋나 무기가 엉뚱한 곳에 붙어버린다.
            if (!was_rolling)
            {
                for (int i = 0; i < weapon_slots.Count; i++) GetOrCreateRuntimeState(i).melee_thrust_active = false;
            }
            ApplyRollPoseToAllSlots();
            was_rolling = true;
            return;
        }

        if (was_rolling)
        {
            RestoreRollHomePositions();
            was_rolling = false;
        }

        // 2026-08-12 "무기 소켓 개별화" - 소켓 개수는 더 이상 씬에 리깅된 개수(weapon_slots.Count)
        // 그대로가 아니라, 머리(로봇) 파츠가 정한 개수와 실제 리깅된 개수 중 작은 값이다.
        // 로봇이 소켓을 적게 가지면 나머지 물리 소켓은 조준/발사/비주얼 갱신 대상에서 빠진다.
        int active_socket_count = ModdingManager.Instance != null
            ? Mathf.Min(weapon_slots.Count, ModdingManager.Instance.ActiveSocketCount)
            : weapon_slots.Count;

        IsTargetingEnemy = false; // UpdateSlot이 소켓 하나라도 타겟을 찾으면 true로 바뀐다
        for (int i = 0; i < active_socket_count; i++)
        {
            UpdateSlot(i);
            // 타겟을 놓쳐 UpdateSlot이 일찍 반환해도 이미 시작된 찌르기 연출은 끝까지 재생한다.
            UpdateMeleeThrustVisual(i, weapon_slots[i]);
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

        bool is_melee = weapon.weapon_firemode == WeaponFireMode.MeleeSwing;

        // 근접무기는 찌르기 연출(StartMeleeThrustVisual)이 소켓 위치로 완전히 복귀할 때까지
        // 조준·재타겟팅을 멈춘다. 예전에는 연출이 재생되는 도중에도 매 프레임 새 타겟 방향으로
        // 회전을 계속 갱신해서, 칼이 제자리로 돌아오는 것과 동시에 다른 방향으로 돌아가버려
        // "소켓 위치로 복귀하지 않는다"는 인상을 줬다(2026-08-12 사용자 리포트).
        if (is_melee && GetOrCreateRuntimeState(slot_index).melee_thrust_active)
        {
            IsTargetingEnemy = true; // 여전히 공격 동작 중이므로 "타겟팅 없음" 조건부 효과는 발동하지 않아야 한다
            return;
        }

        // 감지거리 = 적을 감지해 발사를 시작하는 거리. 탄이 날아가는 최대 거리(사거리)와는
        // 별개이며, 둘 다 무기 기본값(weapon_range)에 소켓 등급 배율을 곱해서 얻는다.
        float detect_range = GetDetectRange(weapon, slot_index);
        EnemyUnit target = FindNearestEnemyInRange(pivot.position, detect_range);
        if (target != null) IsTargetingEnemy = true;

        // 이 소켓에 이전에 총이 장착돼 있었다면 ApplyAngleFlip이 flipY/localRotation을 남겨뒀을 수
        // 있다. 근접무기는 ApplyAngleFlip을 아예 안 타므로 매 프레임 명시적으로 정상 상태로 되돌린다.
        if (is_melee && slot.hand_sprite_renderer != null)
        {
            slot.hand_sprite_renderer.flipY = false;
            slot.hand_sprite_renderer.transform.localRotation = Quaternion.identity;
        }

        if (target == null)
        {
            float rest_angle = RotatePivotTowards(slot, weapon, pivot, slot.rest_rotation_degrees, slot_index);
            if (!is_melee) ApplyAngleFlip(slot, rest_angle, false);
            return;
        }

        Vector3 direction = target.transform.position - pivot.position;
        direction.z = 0f; // X-Y 평면만 사용

        if (direction.sqrMagnitude > 0.0001f)
        {
            // weapon_imgangle: 무기 그림마다 총구가 그려진 각도가 달라서, 무기를 바꾸면
            // 슬롯 보정각(rotation_offset_degrees)만으로는 총구가 타겟을 향하지 않는다.
            //
            // 근접무기는 slot.rotation_offset_degrees를 더하지 않는다 - 그 값은 좌/우 소켓에
            // 각각 다르게 박혀 있는(126.3도/37도) "총기 전용" 보정값이다(총은 손마다 별도로
            // 그려진 미러링 이미지를 쓰지만, 근접무기 3종은 좌우 이미지가 완전히 같은 파일이라
            // 소켓이 달라져도 그림의 실제 방향은 그대로다). 슬롯 값을 그대로 더하면 무기가
            // 어느 손에 장착됐는지에 따라 칼끝 방향이 달라져버린다. 그래서 근접무기는
            // weapon_imgangle 하나만으로 방향을 맞춘다(2026-08-12, "근접무기가 뒤집어져
            // 있음" 리포트로 발견).
            float target_angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
                                 + (is_melee ? 0f : slot.rotation_offset_degrees) + weapon.weapon_imgangle;
            float current_angle = RotatePivotTowards(slot, weapon, pivot, target_angle, slot_index);

            // ApplyAngleFlip(상하 반전)의 각도 범위도 총기 전용 슬롯 값이라 근접무기에는 적용하지
            // 않는다 - 근접무기는 칼이 어느 각도로 돌아가도(뒤집혀도) 자연스러워 굳이 필요 없다.
            if (!is_melee) ApplyAngleFlip(slot, current_angle, true);

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
    /// AI 코어 업그레이드 "사거리 증폭"이 누적한 % 보너스. 사거리·감지거리 둘 다 이 배율을
    /// 함께 곱해야 사용자가 확정한 대로 "사거리와 감지거리가 항상 함께" 오른다(2026-08-14).
    /// GoldGain과 같은 성격이라 RobotStats에는 연결하지 않고 여기서 직접 읽는다.
    /// </summary>
    private static float GetWeaponRangeBonusMultiplier()
    {
        RunState.CoreStatBonuses.TryGetValue(StatType.WeaponRangeBonus, out float bonusPercent);
        return 1f + bonusPercent / 100f;
    }

    /// <summary>
    /// 탄이 실제로 날아가는 최대 거리 = 무기 사거리 x 이 소켓의 사거리 배율 x AI 코어 사거리 증폭
    /// x 머리 효과 배율, 마지막으로 머리 효과의 사거리 상한으로 자른다.
    ///
    /// 머리 효과는 픽시 하나뿐이다 - 모든 소켓이 근접이면 배율 x2, 원거리가 하나라도 섞이면
    /// 그 원거리 무기만 근접급 상한으로 잘린다.
    /// </summary>
    private float GetTravelRange(WeaponData weapon, int slot_index)
    {
        float range = weapon.TravelRange * GetSocketModifiers(slot_index).Range * GetWeaponRangeBonusMultiplier()
                      * HeadEffects.RangeMultiplier(weapon);

        if (HeadEffects.TryGetRangeCap(weapon, out float cap)) range = Mathf.Min(range, cap);
        return range;
    }

    /// <summary>
    /// 적을 감지해 발사를 시작하는 거리 = 무기 감지거리(weapon_detect) x 이 소켓의 감지거리 배율
    /// x AI 코어 사거리 증폭. 두 가지로 한 번 더 잘린다:
    /// 1) 사거리 - 감지한 적에게 탄이 닿아야 의미가 있다
    /// 2) max_detect_range - 화면 밖의 보이지 않는 적과 교전하지 않도록 하는 상한
    /// </summary>
    private float GetDetectRange(WeaponData weapon, int slot_index)
    {
        float detect = weapon.DetectRange * GetSocketModifiers(slot_index).DetectRange * GetWeaponRangeBonusMultiplier()
                       * HeadEffects.RangeMultiplier(weapon);
        detect = Mathf.Min(detect, GetTravelRange(weapon, slot_index));

        if (max_detect_range > 0f) detect = Mathf.Min(detect, max_detect_range);
        return detect;
    }

    /// <summary>
    /// 무기 피벗을 목표 각도 쪽으로 무기의 회전 속도(x 이 소켓의 회전속도 배율)만큼만 돌리고,
    /// 이번 프레임에 실제로 적용된 각도를 돌려준다.
    /// 무기 데이터에 회전 속도가 없으면 슬롯 값으로 폴백하고, 그것도 0 이하면 즉시 스냅한다.
    /// </summary>
    private float RotatePivotTowards(WeaponSlot slot, WeaponData weapon, Transform pivot, float target_angle, int slot_index)
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
            float speed = base_speed * GetSocketModifiers(slot_index).RotationSpeed;
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
                FireBeam(weapon, fire_origin, aim_direction, damage, slot_index);
                attack_duration = Mathf.Max(0f, weapon.weapon_duration);
                break;

            case WeaponFireMode.MeleeSwing:
                // 근접은 투사체를 만들지 않는다. 예전에는 여기서 "총구 기준 weapon_range 반경"을
                // 즉시 판정했는데, 근접 사거리가 4.95~5.85유닛(화면 세로 반경 5.4에 맞먹는다)이라
                // 코앞을 찌르는 그림과 달리 화면 안 좀비가 전부 맞았다(2026-08-13 사용자 리포트).
                // 이제는 찌르는 동작 자체가 판정이다 - StartMeleeThrustVisual이 판정 파라미터를
                // 저장하고, UpdateMeleeThrustVisual이 칼이 나가는 프레임마다 칼 그림 범위를 판정한다.
                attack_duration = Mathf.Max(0f, weapon.weapon_duration);
                StartMeleeThrustVisual(slot_index, slot, weapon, aim_direction, damage);
                break;

            default:
                // 데이터테이블의 weapon_tanhwan(발사 탄환) 이름으로 투사체 프리팹 결정
                GameObject projectile_prefab = ResolveProjectilePrefab(weapon, slot);
                if (projectile_prefab == null) return;

                FireProjectiles(projectile_prefab, slot, weapon, fire_origin, aim_direction, target_distance, damage, slot_index);
                break;
        }

        // 가드맨의 "연속 2회 발사" - 짧은 간격을 두고 같은 발사를 한 번 더 예약한다
        // (탄 수를 2배로 늘리는 방식이 아니라 실제로 두 번 쏘는 방식, 2026-08-19 사용자 확정).
        // 빔·근접은 발사 동작 자체가 시간을 먹으므로 겹치지 않게 투사체 모드에만 적용한다.
        int extra_bursts = HeadEffects.ExtraBursts(weapon);
        if (extra_bursts > 0 && weapon.weapon_firemode == WeaponFireMode.Projectile)
        {
            StartCoroutine(FireExtraBursts(extra_bursts, slot, weapon, target, damage, aim_direction));
        }

        // 대기시간은 <b>발사 동작이 끝난 뒤부터</b> 흐른다(사용자 확정 사항).
        // 덕분에 3초짜리 빔은 3초 + 대기시간이 한 주기가 되어 빔이 여러 개 겹치지 않는다.
        // 머리 효과의 공격속도 배율(컴스톡 연사/메테우스 폭발/버서커/해피픽셀/네온아이/핫팟/
        // 프라이빗 컴스톡)은 기존 임시 버프 배율과 <b>곱셈으로</b> 함께 걸린다.
        float attack_speed = CurrentAttackSpeedMultiplier() * HeadEffects.AttackSpeedMultiplier(weapon);
        float cooldown = weapon.weapon_atsp > 0f && attack_speed > 0f
            ? 1f / (weapon.weapon_atsp * attack_speed)
            : 1f;
        state.next_fire_time = Time.time + attack_duration + cooldown;
    }

    /// <summary>
    /// 가드맨 전용 - 1회차 발사 뒤 <see cref="HeadEffects.GuardmanBurstInterval"/>마다 한 번 더 쏜다.
    ///
    /// 데미지는 1회차에서 계산한 값을 그대로 재사용한다(발사마다 치명타를 다시 굴리면 같은
    /// 트리거인데 2회차만 크리가 터지는 일이 생겨 "연속 2회 발사" 한 동작으로 읽히지 않는다).
    /// 타겟이 그사이 죽었으면 조준 방향만 마지막으로 알던 쪽으로 유지한 채 쏜다.
    ///
    /// <b>2026-08-19 버그 수정</b>: `last_direction`의 초기값이 실제 1회차 발사 방향이 아니라
    /// 고정값 `Vector3.right`였다 - 1회차가 적을 즉사시키는 무기(산탄총 등)에서는 2회차 시점에
    /// `target.IsDead`가 true가 되어 방향 갱신 블록을 건너뛰므로, 1회차 실제 발사 방향과 무관하게
    /// 그대로 `Vector3.right`(0도)로 쏴버렸다 - 1회차가 0도가 아닌 방향이었다면 "같은 방향 2연발"이
    /// 아니라 "쏘고 엉뚱한 각도로 튄" 것처럼 보였다(사용자 리포트: "한 번 쏘고 180도 돌고 두 번째
    /// 발사"). 호출부(TryFireSlot)가 이미 계산해 둔 1회차 `aim_direction`을 받아 초기값으로 쓴다.
    /// </summary>
    private System.Collections.IEnumerator FireExtraBursts(int bursts, WeaponSlot slot, WeaponData weapon, EnemyUnit target, int damage, Vector3 initial_direction)
    {
        // 슬롯 인덱스를 다시 찾지 않도록 발사에 필요한 것만 들고 간다.
        int slot_index = weapon_slots.IndexOf(slot);
        Vector3 last_direction = initial_direction;

        for (int i = 0; i < bursts; i++)
        {
            yield return new WaitForSeconds(HeadEffects.GuardmanBurstInterval);

            // 웨이브가 끝났거나(정비 진입) 게임이 끝났으면 남은 발사를 버린다.
            if (GameOverManager.IsGameOver || GameWinManager.IsGameWon) yield break;
            if (slot.muzzle_point == null) yield break;

            Vector3 origin = slot.muzzle_point.position;
            float distance = 0f;

            if (target != null && !target.IsDead)
            {
                Vector3 to_target = target.transform.position - origin;
                to_target.z = 0f;
                distance = to_target.magnitude;
                if (to_target.sqrMagnitude > 0.0001f) last_direction = to_target.normalized;
            }

            GameObject prefab = ResolveProjectilePrefab(weapon, slot);
            if (prefab == null) yield break;

            FireProjectiles(prefab, slot, weapon, origin, last_direction, distance, damage, slot_index);
        }
    }

    /// <summary>
    /// 근접무기가 "찌르듯이 한 번 튀어나갔다가 돌아오는" 동작을 시작한다(2026-08-12 신규,
    /// 2026-08-13부터 데미지 판정도 이 동작이 담당).
    /// 무기 소켓(rig_point/muzzle_point)의 <b>월드 위치</b>를 잠깐 조준 방향으로 밀었다가 되돌린다.
    /// 복귀 지점 자체는 부모(캐릭터) 기준 로컬 좌표로 저장한다 - 월드 좌표를 그대로 저장하면
    /// 동작이 재생되는 동안 캐릭터가 이동해도 복귀 지점이 따라가지 못해 소켓이 몸에서 떨어진 채로
    /// 복귀하는 버그가 있었다(2026-08-12 사용자 리포트). 밀어내는 거리(offset)는 여전히 월드
    /// 단위로 더한다 - 로컬로 하면 부모 스케일/회전에 따라 밀리는 거리가 달라진다.
    /// </summary>
    private void StartMeleeThrustVisual(int slot_index, WeaponSlot slot, WeaponData weapon, Vector3 direction, int damage)
    {
        Transform pivot = slot.rig_point != null ? slot.rig_point : slot.muzzle_point;
        if (pivot == null) return;

        WeaponRuntimeState state = GetOrCreateRuntimeState(slot_index);
        state.melee_thrust_active = true;
        state.melee_thrust_start_time = Time.time;
        state.melee_thrust_duration = Mathf.Max(0.01f, weapon.weapon_duration);
        state.melee_thrust_distance = weapon.ProjectileSize; // weapon_atsize를 찌르는 거리로 재활용
        state.melee_thrust_home_local = pivot.localPosition;
        state.melee_thrust_direction = direction;

        state.melee_damage = damage;
        state.melee_def_ignore = weapon.weapon_defignore;
        state.melee_knockback = weapon.weapon_knockback;
        state.melee_hit_targets.Clear(); // 이번 찌르기의 중복 방지 집합을 새로 시작
    }

    /// <summary>
    /// StartMeleeThrustVisual이 시작한 찌르기 연출을 매 프레임 진행시킨다. 0→1(찌르기)→0(복귀)
    /// 삼각파로 절반 지점에서 최대로 뻗었다가 나머지 절반 동안 원위치로 돌아온다.
    /// 복귀 지점(home)은 매 프레임 부모의 <b>현재</b> 트랜스폼으로 로컬→월드 변환해서 구하므로,
    /// 연출이 재생되는 동안 캐릭터가 이동/회전해도 소켓이 항상 캐릭터를 따라간 뒤 정확히 그
    /// 자리로 복귀한다. 구르기 등으로 pivot이 다른 곳으로 옮겨진 채 연출이 끝나도 다음 발사 때
    /// 새 home_local을 다시 잡으므로 어긋나지 않는다.
    /// </summary>
    private void UpdateMeleeThrustVisual(int slot_index, WeaponSlot slot)
    {
        WeaponRuntimeState state = GetOrCreateRuntimeState(slot_index);
        if (!state.melee_thrust_active) return;

        Transform pivot = slot.rig_point != null ? slot.rig_point : slot.muzzle_point;
        if (pivot == null) { state.melee_thrust_active = false; return; }

        Vector3 home_world = pivot.parent != null ? pivot.parent.TransformPoint(state.melee_thrust_home_local) : state.melee_thrust_home_local;

        float t = (Time.time - state.melee_thrust_start_time) / state.melee_thrust_duration;
        if (t >= 1f)
        {
            pivot.position = home_world;
            state.melee_thrust_active = false;
            return;
        }

        float progress = t < 0.5f ? t * 2f : (1f - t) * 2f; // 0→1→0
        pivot.position = home_world + state.melee_thrust_direction * (progress * state.melee_thrust_distance);

        // 찔러 나가는 구간(0 → 최대)에서만 판정한다. 칼이 지나간 자리를 프레임마다 판정하므로
        // 최대로 뻗은 지점뿐 아니라 <b>찌르는 경로 전체</b>가 타격 범위가 된다.
        // 돌아오는 구간까지 판정하면 "찔렀다 빼는" 동작의 타격 시점이 흐려진다(중복 방지 집합이
        // 있어 두 번 맞지는 않는다).
        if (t < 0.5f) ApplyMeleeHitAtBlade(slot, pivot, home_world, state);
    }

    /// <summary>
    /// 지금 이 프레임에 <b>칼 그림이 실제로 차지한 범위</b>를 판정한다(2026-08-13 신규).
    ///
    /// 판정 범위를 무기 데이터(weapon_range)에서 가져오지 않는 이유: 근접 3종의 사거리 값은
    /// 4.95~5.85유닛으로 화면 세로 반경(5.4)에 맞먹어서, 코앞을 찌르는 그림과 판정이 5배 가까이
    /// 어긋나 있었다. 스프라이트의 월드 bounds를 쓰면 스케일·회전·좌우 반전·소켓 위치가 전부
    /// 자동으로 반영되므로 "보이는 것 = 맞는 것"이 구조적으로 보장된다.
    /// (이 프로젝트에서 반복된 "시각 크기와 판정 크기가 서로 다른 상수를 쓰는" 버그 계열의
    ///  해결 패턴을 그대로 따른다 - EnemyProjectile 콜라이더, 폭발 반경 사례 참고)
    ///
    /// <b>높이 보정</b>: 무기는 어깨 높이(플레이어 발밑 기준 y +0.79)에 그려지는데 적 히트박스는
    /// 발밑에 있다(실측: 좀비 콜라이더는 자기 원점 기준 y -0.62~-0.02). 이 게임의 y축은 화면상
    /// 높이와 진행 방향을 겸하기 때문에, 칼 그림의 월드 위치를 그대로 판정하면 수직으로 0.5유닛
    /// 이상 떠서 <b>아무도 맞지 않는다</b>. 그래서 수평 위치는 칼을 그대로 따르고 판정 높이만
    /// 소켓 높이만큼 내려 발밑 기준으로 맞춘다 - 좀비의 근접 공격 판정(원점 간 거리)이나
    /// 자동 조준(적의 원점을 겨냥)과 같은 기준이다.
    /// </summary>
    private void ApplyMeleeHitAtBlade(WeaponSlot slot, Transform pivot, Vector3 socket_home_world, WeaponRuntimeState state)
    {
        Vector3 center;
        float radius;

        SpriteRenderer blade = slot.hand_sprite_renderer;
        if (blade != null && blade.sprite != null)
        {
            Bounds bounds = blade.bounds; // 월드 AABB - 회전/스케일/부모 스케일이 모두 반영된 값
            center = bounds.center;
            radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
        }
        else
        {
            // 무기 이미지를 못 찾는 예외 상황에서도 근접 공격이 조용히 무력화되지 않도록,
            // 찌르는 거리의 절반을 반경으로 삼아 소켓 위치를 판정한다.
            center = pivot.position;
            radius = Mathf.Max(0.2f, state.melee_thrust_distance * 0.5f);
        }

        // 소켓이 몸 어디쯤에 달려 있는지(어깨 높이)만큼 판정 높이를 내린다 - 위 주석 참고
        if (player_stats != null) center.y -= socket_home_world.y - player_stats.transform.position.y;

        center.z = 0f; // X-Y 평면만 사용
        if (radius <= 0.001f) return;

        MeleeSwing.Execute(center, state.melee_thrust_direction, radius, state.melee_damage,
                           state.melee_def_ignore, state.melee_knockback,
                           MeleeSwing.FullAngleDegrees, state.melee_hit_targets);
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
        // 디스크(공명의 소리/결정의 마찰음 등)의 시간제/영구 공격력 보정치를 포함한다.
        float robot_atk = 0f;
        if (player_stats != null) robot_atk = player_stats.Atk + player_stats.GetTempStatBonus(StatType.Atk);

        // 2026-08-19 버그 수정: 프라이빗 컴스톡의 [정밀] 공격력 x2는 무기 자체의 위력
        // (weapon_atk)에만 곱해야 한다. robot_atk는 슬롯 수만큼 균등 분배되는 "로봇 전체"의
        // 공격력이라(위 함수 설명 참고), 여기까지 함께 배로 늘리면 정밀 무기 하나만 끼워도
        // 다른 슬롯의 무기까지 덩달아 이득을 보게 된다 - "정밀화기 장착 시 정밀화기의 공격력만
        // 2배가 되어야 한다"는 의도와 어긋난다. `HeadEffects.WeaponAttackMultiplier()`가 이
        // 종류의(무기 자체 위력 전용) 배율만 돌려주며, 가드맨·버서커처럼 전체 데미지에 곱해야
        // 하는 배율은 아래 `DamageMultiplier()`가 그대로 담당한다(둘은 서로 배타적이라
        // 중복 적용되지 않는다).
        float weapon_component = weapon.weapon_atk * HeadEffects.WeaponAttackMultiplier(weapon);
        float damage = weapon_component + robot_atk / weapon.ProjectileCount;

        if (player_stats != null && player_stats.Cc > 0f)
        {
            float crit_roll = UnityEngine.Random.Range(0f, 100f);
            if (crit_roll <= player_stats.Cc)
            {
                damage += damage * player_stats.Cd;
            }
        }

        // "777 디스크" - 확률로 이번 데미지를 배로 늘린다.
        if (player_stats != null && player_stats.DiscEffects != null)
        {
            damage = player_stats.DiscEffects.ApplyOnAttackProcs(damage);
        }

        // 2026-08-19 머리 효과(가드맨 산탄 +15% / 버서커 체력 50% 이하 x1.5). 근접·빔·투사체가
        // 전부 이 함수를 거치므로 여기 한 줄이면 세 발사 방식에 모두 적용된다. 프라이빗
        // 컴스톡의 정밀 x2는 여기서 다시 곱해지지 않는다(`DamageMultiplier`가 PrivateComstock
        // 에는 1을 돌려준다) - 이미 위에서 `WeaponAttackMultiplier`로 weapon_atk에만 적용했다.
        damage *= HeadEffects.DamageMultiplier(weapon);

        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    // 2026-08-12 디스크 기획서 "광분 바이러스 디스크" 반영 - 이 게임에 "공격속도"라는 로봇
    // 스탯이 원래 없어서(무기마다 자기 weapon_atsp만 있음), 처치 시 잠깐 발사 대기시간을
    // 줄여주는 전용 배율을 여기 별도로 둔다. 1보다 크면 그만큼 대기시간이 짧아진다.
    private float temp_attack_speed_multiplier = 1f;
    private float temp_attack_speed_expire_time = 0f;

    /// <summary>duration초 동안 발사 대기시간을 1/(1+multiplierBonus)배로 줄인다(공격속도 상승).</summary>
    public void ApplyTempAttackSpeedBuff(float multiplierBonus, float duration)
    {
        temp_attack_speed_multiplier = 1f + Mathf.Max(0f, multiplierBonus);
        temp_attack_speed_expire_time = Time.time + duration;
    }

    private float CurrentAttackSpeedMultiplier() => Time.time < temp_attack_speed_expire_time ? temp_attack_speed_multiplier : 1f;

    /// <summary>지속시간 동안 직선 범위를 태우는 빔을 만든다(플라즈마 캐논).</summary>
    private void FireBeam(WeaponData weapon, Vector3 origin, Vector3 direction, int total_damage, int slot_index)
    {
        Sprite visual = ResolveWeaponSprite(beam_sprite_name, weapon);

        BeamProjectile.Fire(visual, origin, direction, GetTravelRange(weapon, slot_index), weapon.ProjectileSize,
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
    private void FireProjectiles(GameObject projectile_prefab, WeaponSlot slot, WeaponData weapon, Vector3 origin, Vector3 direction, float target_distance, int damage, int slot_index)
    {
        int projectile_count = weapon.ProjectileCount;

        float speed = weapon.weapon_speed > 0f
            ? weapon.weapon_speed
            : (slot.projectile_speed > 0f ? slot.projectile_speed : WeaponData.DefaultProjectileSpeed);

        // 폭발 무기는 기본적으로 최대 사거리에서 터지지만,
        // 타겟이 사거리 안쪽에 있다면 그 지점에서 조기 폭발한다.
        float travel_range = GetTravelRange(weapon, slot_index);
        if (weapon.weapon_splash > 0f && target_distance > 0f && target_distance < travel_range)
        {
            travel_range = target_distance;
        }

        // 메테우스의 폭발 범위 +20%. 조기 폭발 판정(위)은 반경이 아니라 거리만 보므로 영향 없다.
        float splash_radius = weapon.weapon_splash * HeadEffects.SplashRadiusMultiplier(weapon);

        // 프라이빗 컴스톡의 관통 +2. 무제한 관통(-1)인 무기는 더할 것이 없으므로 건드리지 않는다
        // (-1에 2를 더하면 1이 되어 오히려 관통이 <b>줄어든다</b>).
        int bonus_pierce = HeadEffects.BonusPierce(weapon);

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

            int pierce = weapon.RollPierceCount(); // 탄마다 따로 확률 관통 판정
            if (pierce >= 0) pierce += bonus_pierce; // -1(무제한)은 그대로 둔다

            projectile.Launch(new Projectile.Spec
            {
                Direction = shot_direction,
                Speed = speed,
                Damage = damage,
                MaxRange = travel_range,
                Size = weapon.ProjectileSize,
                PierceCount = pierce,
                SplashRadius = splash_radius,
                DefIgnore = weapon.weapon_defignore,
                Knockback = weapon.weapon_knockback,
                BlastVisualDuration = blast_visual_duration
            });
        }
    }
}
