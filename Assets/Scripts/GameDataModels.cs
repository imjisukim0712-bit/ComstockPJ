using System;
using UnityEngine;

/// <summary>
/// 몬스터(좀비) 1종의 스탯 데이터.
///
/// 2026-08-10 좀비 기획서 Ver04의 질량/규격 필드를 사용한다. AI 성격값과 감지범위는
/// 사용자 요청으로 제거됐으며, 모든 좀비는 스폰 직후부터 플레이어를 직접 추적한다.
/// </summary>
[Serializable]
public struct MonsterData
{
    public int monster_id;
    public string monster_name;
    public int monster_hp;
    public int monster_atk;
    public int monster_def;
    public float monster_speed;
    public float monster_range;
    public int monster_type;
    public float monster_atsp;

    [Tooltip("질량 - 피격 넉백 저항에 쓰인다(질량이 높을수록 덜 밀려남, 기획서 p.22). 0이면 기준 질량(50)으로 취급")]
    public float monster_mass;

    [Tooltip("규격(소형/중형/대형/초대형) - 이미지·콜라이더 크기를 결정한다")]
    public MonsterSizeClass monster_size;

    /// <summary>넉백 계산에 쓰는 유효 질량. 미설정(0) 데이터는 기준 질량으로 폴백한다.</summary>
    public float EffectiveMass => monster_mass > 0f ? monster_mass : EnemyUnit.ReferenceMass;

}

[Serializable]
public struct RobotData
{
    public int robot_id;
    public string robot_name;
    public int robot_hp;
    public int robot_atk;
    public int robot_def;
    public float robot_cc;
    public float robot_cd;
    public float robot_speed;
    public float robot_capacity;
    public float robot_reload;
    public float robot_avoid;
    public float robot_luck;
    public float robot_mess;
    public int robot_special; // 필살기 ID (시트 필드명: robot_special)
}

/// <summary>
/// 무기 1행. <b>등급마다 별도의 행이 존재한다</b>(13종 x 5등급 = 65행).
/// 예전처럼 상점에서 등급 배율을 곱하는 방식이 아니라, weapon_grade를 행 자체가 들고 있고
/// 등급별 공격력이 데이터에 이미 반영되어 있다(일반 기준 x1.15^등급).
///
/// ID 체계: 300000 + 종류(1~13) x 100 + 등급(1~5)
///   예) 중기관총 일반 = 300101 / 희귀 = 300102 / ... / 전설 = 300105
///
/// 수치 단위: 거리는 전부 <b>월드 유닛</b>이다(기획 문서의 픽셀 수치 ÷ 50).
/// 참고로 직교 카메라 기준 세로 가시 반경은 8.66, 가로는 15.4 유닛이다.
/// </summary>
[Serializable]
public struct WeaponData
{
    public int weapon_id;
    public string weapon_name;

    [Tooltip("이 행의 등급. 상점 품목 추첨과 표시 색상에 쓰인다")]
    public ItemGrade weapon_grade;

    [Tooltip("투사체 1발당 공격력. 등급별 상승분이 이미 반영된 값이다")]
    public float weapon_atk;

    [Tooltip("초당 공격 횟수. 대기시간 = 1/이 값 이며, 발사 동작이 끝난 뒤부터 흐르기 시작한다")]
    public float weapon_atsp;

    [Tooltip("탄이 날아가는 최대 거리(유닛). 빔은 빔 길이, 근접은 스윙 반경으로 쓰인다")]
    public float weapon_range;

    [Tooltip("적을 감지해 발사를 시작하는 거리(유닛). 사거리를 넘지 않도록 잘린다. 0이면 사거리와 같게 취급")]
    public float weapon_detect;

    [Tooltip("투사체 이동 속도(유닛/초). 0이면 기본값으로 폴백")]
    public float weapon_speed;

    [Tooltip("조준 방향으로 무기가 돌아가는 속도(도/초). 0이면 소켓 기본값으로 폴백")]
    public float weapon_rotspeed;

    [Tooltip("투사체 크기(스케일). 빔에서는 빔의 반폭으로 쓰인다")]
    public float weapon_atsize;

    [Tooltip("탄퍼짐 반각(도). 투사체 <b>하나하나</b>가 이 범위 안에서 개별로 흔들린다")]
    public float weapon_aim;

    [Tooltip("다중탄이 부채꼴로 벌어지는 탄 사이 각도 간격(도). 투사체가 1개면 무시된다")]
    public float weapon_rebound;

    [Tooltip("한 번의 발사에서 나가는 투사체 개수")]
    public int weapon_projectiles;

    [Tooltip("관통 횟수. 0 = 첫 충돌에 소멸 / N = N번까지 뚫음 / -1 = 무제한 관통")]
    public int weapon_pierce;

    [Tooltip("관통이 발동할 확률(0~1). 투사체마다 따로 굴린다. 0이면 항상 발동(=확정 관통)")]
    public float weapon_pierce_chance;

    [Tooltip("착탄 시 범위 피해 반경(유닛). 0이면 단일 대상만 타격한다")]
    public float weapon_splash;

    [Tooltip("적 방어력을 무시하는 비율(0~1). 0.5면 방어력의 절반만 적용된다")]
    public float weapon_defignore;

    [Tooltip("적중한 적을 밀어내는 초기 속도(유닛/초). 0이면 넉백 없음")]
    public float weapon_knockback;

    [Tooltip("지속 피해 시간(초). 빔 전용이며, 이 시간에 걸쳐 weapon_atk를 나눠서 넣는다. 0이면 즉시 타격")]
    public float weapon_duration;

    [Tooltip("공격 생성 방식 - 투사체 / 빔 / 근접 스윙")]
    public WeaponFireMode weapon_firemode;

    [Tooltip("손에 든 이미지의 추가 크기 배율. 1 = 자동 정규화 크기 그대로")]
    public float weapon_imgscale;

    [Tooltip("이미지에 그려진 총구 방향과 실제 조준각의 차이를 메우는 보정각(도)")]
    public float weapon_imgangle;

    public string weapon_tanhwan;   // 발사할 투사체 프리팹 이름 (Assets/Prefebs 안의 프리팹명)
    public string weapon_lfwpimg;   // 왼손에 들었을 때 보여줄 이미지 이름 (Resources 폴더의 스프라이트명)
    public string weapon_rgwpimg;   // 오른손에 들었을 때 보여줄 이미지 이름 (Resources 폴더의 스프라이트명)

    // --- 0(미설정)으로 남은 값을 안전한 기본값으로 바꿔주는 폴백. PartData의 RangeMultiplier와 같은 패턴 ---

    public const float DefaultRange = 20f;
    public const float DefaultProjectileSpeed = 15f;
    public const float DefaultRotationSpeed = 540f;

    /// <summary>탄이 실제로 날아가는 최대 거리(소켓 배율 적용 전).</summary>
    public float TravelRange => weapon_range > 0f ? weapon_range : DefaultRange;

    /// <summary>적을 감지해 발사를 시작하는 거리. 닿지도 않을 적을 조준하지 않도록 사거리로 잘라둔다.</summary>
    public float DetectRange => Mathf.Min(weapon_detect > 0f ? weapon_detect : TravelRange, TravelRange);

    public float ProjectileSpeed => weapon_speed > 0f ? weapon_speed : DefaultProjectileSpeed;
    public float RotationSpeed => weapon_rotspeed > 0f ? weapon_rotspeed : DefaultRotationSpeed;
    public float ImageScale => weapon_imgscale > 0f ? weapon_imgscale : 1f;
    public float ProjectileSize => weapon_atsize > 0f ? weapon_atsize : 1f;
    public int ProjectileCount => Mathf.Max(1, weapon_projectiles);

    /// <summary>관통 발동 확률. 0(미설정)은 "확률 개념 없음 = 항상 발동"으로 해석한다.</summary>
    public float PierceChance => weapon_pierce_chance > 0f ? Mathf.Clamp01(weapon_pierce_chance) : 1f;

    /// <summary>이번 발사에서 이 투사체가 관통을 얻는지 굴린다(투사체마다 따로 호출할 것).</summary>
    public int RollPierceCount()
    {
        if (weapon_pierce == 0) return 0;
        return UnityEngine.Random.value <= PierceChance ? weapon_pierce : 0;
    }
}

[Serializable]
public struct AmorData
{
    public int amor_id;
    public string amor_name;
    public int amor_hp;
    public int amor_def;
    public float amor_speed;
    public float amor_avoid;
}

[Serializable]
public struct DropEntry
{
    public int monster_id;
    public int item_id;
    public float item_drop;
}
