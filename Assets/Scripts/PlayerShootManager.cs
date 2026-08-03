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
/// delayed_blast_weapon_ids 범위(기본 300400~300499, 수류탄류)에 속한 무기는
/// weapon_atsize를 접촉 판정 크기로 쓰지 않고, 작은 크기로 날아가다가 사거리 끝에서
/// weapon_atsize 범위에 한 번에 데미지를 주는 방식으로 동작한다.
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

        [Tooltip("데이터테이블의 weapon_tanhwan(발사 탄환) 이름을 못 찾았을 때 사용할 예비 프리팹")]
        public GameObject projectile_prefab;

        [Tooltip("이 무기 투사체의 이동 속도 - 데이터테이블에 없는 값이라 여기서 직접 지정")]
        public float projectile_speed;
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

    private Camera main_camera;
    private int current_slot_index = 0;
    private float next_fire_time = 0f;
    private WeaponData? current_weapon_data;
    private readonly Dictionary<int, WeaponRuntimeState> runtime_state_by_slot = new Dictionary<int, WeaponRuntimeState>();

    // weapon_tanhwan(프리팹 이름) → 프리팹. 대소문자 구분 없이 조회
    private readonly Dictionary<string, GameObject> prefab_by_name =
        new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

    // 이름을 못 찾았을 때 매 발사마다 경고가 도배되지 않도록 기록
    private readonly HashSet<string> warned_prefab_names = new HashSet<string>();

    private void Awake()
    {
        main_camera = Camera.main;

        foreach (var entry in projectile_prefabs)
        {
            if (string.IsNullOrWhiteSpace(entry.prefab_name) || entry.prefab == null) continue;
            prefab_by_name[entry.prefab_name.Trim()] = entry.prefab;
        }
    }

    private void Start()
    {
        RefreshCurrentWeaponData();
    }

    private void Update()
    {
        HandleWeaponSwitch();
        HandleReloadProgress();

        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryFire();
        }
    }

    private void HandleWeaponSwitch()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[weapon1_key].wasPressedThisFrame && weapon_slots.Count > 0)
        {
            current_slot_index = 0;
            RefreshCurrentWeaponData();
        }
        else if (Keyboard.current[weapon2_key].wasPressedThisFrame && weapon_slots.Count > 1)
        {
            current_slot_index = 1;
            RefreshCurrentWeaponData();
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
            state.ammo_remaining = data.weapon_capacity > 0 ? data.weapon_capacity : int.MaxValue; // 장탄수가 0/미설정이면 무제한 취급
        }
    }

    // 재장전 중인 슬롯이 있으면 시간 경과를 체크해서 재장전을 끝내준다.
    private void HandleReloadProgress()
    {
        foreach (var kv in runtime_state_by_slot)
        {
            WeaponRuntimeState state = kv.Value;
            if (state.is_reloading && Time.time >= state.reload_end_time)
            {
                state.is_reloading = false;

                int weapon_id = weapon_slots[kv.Key].weapon_id;
                if (GameDataManager.Instance.Weapons.TryGetValue(weapon_id, out WeaponData data))
                {
                    state.ammo_remaining = data.weapon_capacity > 0 ? data.weapon_capacity : int.MaxValue;
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

        if (state.is_reloading) return; // weapon_reload 중에는 발사 불가

        Vector3 fire_origin = slot.muzzle_point.position;
        Vector3 target_point = GetMouseWorldPointOnPlane(fire_origin.z);

        Vector3 aim_direction = target_point - fire_origin;
        aim_direction.z = 0f; // Z축 미사용 규칙 - 방향은 X-Y 평면 안에서만 계산
        if (aim_direction.sqrMagnitude < 0.0001f) aim_direction = Vector3.right;
        aim_direction.Normalize();

        // weapon_aim(조준 정확도) → 클릭 지점 기준 방향을 좌우로 무작위로 흐트러뜨림
        if (weapon.weapon_aim > 0f)
        {
            float random_deviation = UnityEngine.Random.Range(-weapon.weapon_aim, weapon.weapon_aim);
            aim_direction = Quaternion.Euler(0f, 0f, random_deviation) * aim_direction;
        }

        FireProjectiles(projectile_prefab, slot, weapon, fire_origin, aim_direction);

        // weapon_capacity(장탄수)만큼 소모했는지 체크 → 소모했으면 weapon_reload(재장전 시간) 시작
        int shots_consumed = Mathf.Max(1, weapon.weapon_projectiles);
        if (weapon.weapon_capacity > 0)
        {
            state.ammo_remaining -= shots_consumed;
            if (state.ammo_remaining <= 0)
            {
                state.is_reloading = true;
                state.reload_end_time = Time.time + Mathf.Max(0f, weapon.weapon_reload);
            }
        }

        // weapon_atsp(공격속도)가 높을수록 다음 발사까지 대기시간이 짧아짐
        float cooldown = weapon.weapon_atsp > 0f ? 1f / weapon.weapon_atsp : 1f;
        next_fire_time = Time.time + cooldown;
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

    private void FireProjectiles(GameObject projectile_prefab, WeaponSlot slot, WeaponData weapon, Vector3 origin, Vector3 direction)
    {
        int projectile_count = Mathf.Max(1, weapon.weapon_projectiles);
        float side_spacing = weapon.weapon_rebound * side_spacing_multiplier;
        float speed = slot.projectile_speed > 0f ? slot.projectile_speed : 15f; // 슬롯에 값이 없으면 기본 속도로 폴백
        float size = weapon.weapon_atsize > 0f ? weapon.weapon_atsize : 1f;
        bool can_penetrate = weapon.weapon_penetration;

        // 지연 폭발 무기는 날아가는 동안만 작은 크기를 쓰고, 원래 공격범위는 폭발 시점에 적용
        bool delayed_blast = IsDelayedBlastWeapon(weapon.weapon_id);
        float travel_size = delayed_blast ? Mathf.Max(0.01f, delayed_blast_travel_size) : size;

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

            projectile.Launch(direction, speed, weapon.weapon_atk, weapon.weapon_range, travel_size, can_penetrate);

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
