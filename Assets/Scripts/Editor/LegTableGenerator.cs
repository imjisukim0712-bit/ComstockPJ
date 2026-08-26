using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 다리(Leg) 파츠 테이블을 <c>20260818_다리기획서_Ver02_백승오.pdf</c> 명세대로 재생성한다.
/// `WeaponTableGenerator`/`PartsTableGenerator`와 같은 패턴이다.
///
/// <b>기존 21개(표준 다리 1 + 유압/안정화/경량 부츠/제트 부스터 각 5등급 = 20)는 전부 더미였다</b>
/// (아이콘 없음, 특수효과 전부 None, 등급별 가산 스탯만 있고 액티브/패시브 스킬이 아예 없었다).
/// 2026-08-21 사용자 지시: "기존의 더미 다리들은 다 없애고 기획서에 나온 다리만 남겨놔."
///
/// <b>기획서에는 등급 구분이 없다</b> - 다리 종류마다 딱 하나의 수치 집합만 적혀 있다(무기/장갑처럼
/// 일반→전설 5단계 표가 아니다). 그래서 이 생성기는 다른 테이블 생성기(무기 65행 = 13종×5등급)와
/// 달리 <b>종류당 정확히 1개 행</b>만 만든다 - "기획서에 나온 다리만 남겨놔"를 문자 그대로
/// 따른 것이다.
///
/// 기본 다리(500004)는 <see cref="UnlockTracker.DefaultLegPartId"/>가 참조하는 시작 파츠라
/// partId를 유지한 채 <b>내용만 명세대로 갈아끼운다</b>(삭제 후 재생성하면 ID가 바뀌어
/// 세이브/해금 로직이 깨진다). 나머지 3종은 새 ID(500010/500011/500012)로 추가한다.
/// </summary>
public static class LegTableGenerator
{
    private const string CatalogPath = "Assets/Data/PartsCatalog.asset";

    private const int DefaultLegId = 500004;   // UnlockTracker.DefaultLegPartId와 반드시 같은 값
    private const int SpiderLegId = 500010;
    private const int CaterpillarLegId = 500011;
    private const int RocketLegId = 500012;

    private const float LegWeight = 3.5f; // 기존 더미 다리들의 임시값을 그대로 승계(명세에 무게 언급 없음)

    [MenuItem("Comstock/다리 테이블 4종 재생성")]
    public static void Generate()
    {
        PartsCatalog catalog = AssetDatabase.LoadAssetAtPath<PartsCatalog>(CatalogPath);
        if (catalog == null)
        {
            Debug.LogError($"[다리 생성기] 카탈로그를 찾지 못했습니다: {CatalogPath}");
            return;
        }

        List<PartData> parts = catalog.EditorGetParts();
        int before = parts.Count;

        // 기본 다리(시작 파츠)만 남기고 Leg 슬롯의 나머지(유압/안정화/경량 부츠/제트 부스터
        // 20개)를 전부 제거한다.
        int removed = parts.RemoveAll(p => p.slot == PartSlot.Leg && p.partId != DefaultLegId);

        // 기본 다리를 명세대로 갈아끼운다. 인덱스를 찾아 그 자리에서 교체한다(순서 보존).
        int defaultIndex = parts.FindIndex(p => p.partId == DefaultLegId);
        if (defaultIndex < 0)
        {
            Debug.LogError($"[다리 생성기] 기본 다리(partId {DefaultLegId})를 카탈로그에서 찾지 못했습니다. " +
                            "isDefaultStarter 다리가 삭제된 적이 있는지 확인하세요.");
            return;
        }
        parts[defaultIndex] = BuildDefaultLeg();

        parts.Add(BuildSpiderLeg());
        parts.Add(BuildCaterpillarLeg());
        parts.Add(BuildRocketLeg());

        catalog.EditorSetParts(parts);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[다리 생성기] 완료 - 이전 {before}개 → 현재 {parts.Count}개 " +
                  $"(더미 다리 {removed}개 제거, 기본 다리 1개 갈아끼움, 거미/캐터필러/로켓 3종 신규 추가)");
    }

    private static PartData BuildDefaultLeg() => new PartData
    {
        partId = DefaultLegId,
        partName = "기본 다리",
        slot = PartSlot.Leg,
        grade = ItemGrade.Normal,
        isDefaultStarter = true,
        iconName = "Leg_Standard",
        weight = LegWeight,
        weightCapacity = 12f, // 기존 표준 다리 값 승계(명세에 지탱력 언급 없음)

        effect = PartEffect.MoveSpeedPercentBonus,
        effectAmount = 10f,   // 이동속도 +10%

        legSkillType = LegSkillType.Roll,
        legSkillCooldown = 1f, // 쿨타임 1초
        legVisualType = LegVisualMode.Biped,
    };

    private static PartData BuildSpiderLeg() => new PartData
    {
        partId = SpiderLegId,
        partName = "거미 다리",
        slot = PartSlot.Leg,
        grade = ItemGrade.Normal,
        isDefaultStarter = false,
        iconName = "Leg_Spider",
        weight = LegWeight,
        weightCapacity = 12f,

        // 명세: "이동속도 +20%, 질량 * 0.5". 파츠 하나엔 PartEffect가 하나뿐이라(effect 필드가
        // 이동속도 %효과로 이미 쓰인다) 질량 %는 전용 필드(legMassPercent)로 뺐다.
        effect = PartEffect.MoveSpeedPercentBonus,
        effectAmount = 20f,   // 이동속도 +20%
        legMassPercent = -50f, // 질량 * 0.5

        legSkillType = LegSkillType.Hop,
        legSkillCooldown = 1f,          // 쿨타임 1초
        legHpLossSpeedPenalty = true,   // 체력 25% 손실마다 이동속도 -5%(누적)
        legVisualType = LegVisualMode.Spider,
    };

    private static PartData BuildCaterpillarLeg() => new PartData
    {
        partId = CaterpillarLegId,
        partName = "캐터필러",
        slot = PartSlot.Leg,
        grade = ItemGrade.Normal,
        isDefaultStarter = false,
        iconName = "Leg_Caterpillar",
        weight = LegWeight,
        weightCapacity = 12f,

        effect = PartEffect.MoveSpeedPercentBonus,
        effectAmount = 25f,   // 이동속도 +25%

        legSkillType = LegSkillType.Boost,
        legSkillCooldown = 3f, // 쿨타임 3초
        legVisualType = LegVisualMode.Tread,
    };

    private static PartData BuildRocketLeg() => new PartData
    {
        partId = RocketLegId,
        partName = "로켓 추진기",
        slot = PartSlot.Leg,
        grade = ItemGrade.Normal,
        isDefaultStarter = false,
        iconName = "Leg_Rocket",
        weight = LegWeight,
        weightCapacity = 12f,

        effect = PartEffect.MoveSpeedPercentBonus,
        effectAmount = 10f,   // 이동속도 +10%

        legSkillType = LegSkillType.None,   // 액티브 스킬 없음
        legSpeedRampPassive = true,          // 동일 방향 2초 이동 시 점진 가속(최대 +2)
        legVisualType = LegVisualMode.Rocket,
    };

    /// <summary>4종 다리가 전부 있는지, 중복 ID가 없는지 확인한다.</summary>
    [MenuItem("Comstock/다리 테이블 검증")]
    public static void Validate()
    {
        PartsCatalog catalog = AssetDatabase.LoadAssetAtPath<PartsCatalog>(CatalogPath);
        if (catalog == null) return;

        var legs = new List<PartData>();
        foreach (PartData p in catalog.Parts) if (p.slot == PartSlot.Leg) legs.Add(p);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[다리 검증] Leg 슬롯 총 {legs.Count}개 (기대값 4)");
        foreach (PartData p in legs)
        {
            sb.AppendLine($"  {p.partId} {p.partName} - 스킬={p.legSkillType}(쿨 {p.legSkillCooldown}) " +
                          $"HP패널티={p.legHpLossSpeedPenalty} 가속패시브={p.legSpeedRampPassive} " +
                          $"기본장착={p.isDefaultStarter}");
        }
        Debug.Log(sb.ToString());
    }
}
