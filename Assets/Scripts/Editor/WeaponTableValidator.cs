using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 무기 테이블 3종(GameDataAsset.weapons / PartsCatalog.weaponMeta / ShopCatalog.weaponEntries)이
/// 서로 어긋나지 않았는지 검사하는 <b>에디터 전용</b> 도구.
///
/// 한 테이블에만 무기를 넣고 나머지를 빠뜨리면 게임은 에러 없이 조용히 이상하게 동작한다
/// (무게 0으로 취급되거나, 상점에 영영 안 나오거나, 이미지가 안 보이거나).
/// 밸런스를 고칠 때마다 이걸 돌리면 그런 실수를 몇 초 만에 잡을 수 있다.
/// </summary>
public static class WeaponTableValidator
{
    [MenuItem("Comstock/무기 테이블 검증")]
    public static void Validate()
    {
        var gameData = AssetDatabase.LoadAssetAtPath<GameDataAsset>("Assets/Data/GameDataAsset.asset");
        var partsCatalog = AssetDatabase.LoadAssetAtPath<PartsCatalog>("Assets/Data/PartsCatalog.asset");
        var shopCatalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>("Assets/Data/ShopCatalog.asset");

        if (gameData == null || partsCatalog == null || shopCatalog == null)
        {
            Debug.LogError("검증 실패: 데이터 에셋을 찾을 수 없습니다.");
            return;
        }

        var problems = new List<string>();
        var sb = new StringBuilder();
        sb.AppendLine("=== 무기 테이블 검증 ===");

        // 1) ID 중복
        var ids = new HashSet<int>();
        foreach (WeaponData w in gameData.weapons)
        {
            if (!ids.Add(w.weapon_id)) problems.Add($"무기 ID 중복: {w.weapon_id}");
        }
        sb.AppendLine($"무기 {gameData.weapons.Count}행 (고유 ID {ids.Count}개)");

        // 2) 세 테이블의 ID 집합이 완전히 일치하는지
        var metaIds = new HashSet<int>();
        foreach (int id in ids) if (partsCatalog.TryGetWeaponMeta(id, out _)) metaIds.Add(id);

        var shopIds = new HashSet<int>();
        foreach (ShopCatalog.WeaponEntry e in shopCatalog.WeaponEntries) shopIds.Add(e.weaponId);

        foreach (int id in ids)
        {
            if (!metaIds.Contains(id)) problems.Add($"weaponMeta 누락: {id} (무게 0 + 타입 제한이 조용히 사라진다)");
            if (!shopIds.Contains(id)) problems.Add($"상점 목록 누락: {id} (영영 구매할 수 없다)");
        }
        foreach (int id in shopIds)
        {
            if (!ids.Contains(id)) problems.Add($"상점에 있으나 무기 데이터에 없는 ID: {id}");
        }
        sb.AppendLine($"weaponMeta {metaIds.Count}행 / 상점 {shopIds.Count}행");

        // 3) 등급 필드가 ID와 일치하는지 (ID = 300000 + 종류*100 + 등급+1)
        // 4) 종류별 공격력이 1.15^등급을 따르는지
        var byKind = new Dictionary<int, WeaponData[]>();
        foreach (WeaponData w in gameData.weapons)
        {
            int kind = (w.weapon_id - 300000) / 100;
            int gradeFromId = (w.weapon_id % 100) - 1;

            if ((int)w.weapon_grade != gradeFromId)
                problems.Add($"{w.weapon_id} {w.weapon_name}: 등급 불일치 (필드 {w.weapon_grade} vs ID {gradeFromId})");

            if (!byKind.TryGetValue(kind, out WeaponData[] arr)) byKind[kind] = arr = new WeaponData[5];
            if (gradeFromId >= 0 && gradeFromId < 5) arr[gradeFromId] = w;
        }

        foreach (var kv in byKind)
        {
            WeaponData baseW = kv.Value[0];
            if (baseW.weapon_id == 0) { problems.Add($"종류 {kv.Key}: 일반 등급 행이 없습니다"); continue; }

            for (int g = 1; g < 5; g++)
            {
                WeaponData w = kv.Value[g];
                if (w.weapon_id == 0) { problems.Add($"종류 {kv.Key}: {(ItemGrade)g} 등급 행이 없습니다"); continue; }

                float expected = baseW.weapon_atk * Mathf.Pow(1.15f, g);
                if (Mathf.Abs(w.weapon_atk - expected) / Mathf.Max(0.0001f, expected) > 0.01f)
                    problems.Add($"{w.weapon_id} {w.weapon_name}: 공격력 {w.weapon_atk} (기대 {expected:0.##}) - 등급별 15% 규칙 위반");
            }
        }
        sb.AppendLine($"무기 종류 {byKind.Count}종 x 5등급");

        // 5) 스프라이트가 실제로 로드되는지 (여기서 실패하면 인게임에서 무기가 안 보인다)
        var missingSprites = new HashSet<string>();
        foreach (WeaponData w in gameData.weapons)
        {
            CheckSprite(w.weapon_lfwpimg, missingSprites);
            CheckSprite(w.weapon_rgwpimg, missingSprites);
        }
        foreach (string s in missingSprites) problems.Add($"스프라이트를 찾을 수 없음: {s}");

        // 6) 사거리/감지거리 정합성
        foreach (WeaponData w in gameData.weapons)
        {
            if (w.weapon_range <= 0f) problems.Add($"{w.weapon_id} {w.weapon_name}: 사거리가 0 이하");
            if (w.weapon_detect > w.weapon_range)
                problems.Add($"{w.weapon_id} {w.weapon_name}: 감지거리({w.weapon_detect}) > 사거리({w.weapon_range}) - 닿지 않을 적을 조준하게 된다");
        }

        // 7) 발사 방식별 필수 값
        foreach (WeaponData w in gameData.weapons)
        {
            if (w.weapon_firemode == WeaponFireMode.Beam && w.weapon_duration <= 0f)
                problems.Add($"{w.weapon_id} {w.weapon_name}: 빔인데 지속시간이 0");

            if (w.weapon_firemode == WeaponFireMode.Projectile)
            {
                if (string.IsNullOrWhiteSpace(w.weapon_tanhwan))
                    problems.Add($"{w.weapon_id} {w.weapon_name}: 투사체 무기인데 발사 탄환 이름이 비어 있음");
                if (w.weapon_speed <= 0f)
                    problems.Add($"{w.weapon_id} {w.weapon_name}: 투사체 무기인데 속도가 0");
            }

            if (w.weapon_pierce_chance < 0f || w.weapon_pierce_chance > 1f)
                problems.Add($"{w.weapon_id} {w.weapon_name}: 관통 확률이 0~1 범위를 벗어남 ({w.weapon_pierce_chance})");
            if (w.weapon_defignore < 0f || w.weapon_defignore > 1f)
                problems.Add($"{w.weapon_id} {w.weapon_name}: 방어무시 비율이 0~1 범위를 벗어남 ({w.weapon_defignore})");
        }

        // 결과
        if (problems.Count == 0)
        {
            sb.AppendLine("\n문제 없음 - 8개 검사 전부 통과");
            Debug.Log(sb.ToString());
        }
        else
        {
            sb.AppendLine($"\n문제 {problems.Count}건:");
            foreach (string p in problems) sb.AppendLine("  - " + p);
            Debug.LogError(sb.ToString());
        }
    }

    private static void CheckSprite(string name, HashSet<string> missing)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Resources.Load<Sprite>(name.Trim()) == null) missing.Add(name);
    }
}
