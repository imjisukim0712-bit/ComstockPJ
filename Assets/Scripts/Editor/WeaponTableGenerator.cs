using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 무기 13종 x 5등급 = 65행을 세 개의 데이터 에셋에 한 번에 만들어 넣는 <b>에디터 전용</b> 도구.
///
/// 왜 생성기인가: 65행 x 24필드 = 1,560개 값을 인스펙터나 YAML로 손으로 넣는 것은 비현실적이고,
/// 밸런스는 반드시 재조정된다. 사람이 실제로 정의하는 값은 아래 Defs 배열의 13줄뿐이고
/// 나머지 52행은 등급 배율(1.15^등급)로 파생된다. 계수를 고치고 다시 실행하면 끝이다.
///
/// <b>런타임 데이터의 유일한 출처는 여전히 Assets/Data/*.asset 이다.</b> 이 파일은 그 에셋을
/// 채우는 에디터 도구일 뿐이며, 구글 시트 같은 프로젝트 밖 리소스를 일절 참조하지 않는다
/// (외부 연동 금지 규칙과 무관하다).
///
/// 건드리는 리스트는 정확히 세 개다 - GameDataAsset.weapons / PartsCatalog.weaponMeta /
/// ShopCatalog.weaponEntries. monsters·robots·drops·parts·discs 등은 절대 손대지 않는다.
/// </summary>
public static class WeaponTableGenerator
{
    private const string GameDataPath = "Assets/Data/GameDataAsset.asset";
    private const string PartsCatalogPath = "Assets/Data/PartsCatalog.asset";
    private const string ShopCatalogPath = "Assets/Data/ShopCatalog.asset";

    /// <summary>등급 수 (일반/희귀/서사/유일/전설).</summary>
    private const int GradeCount = 5;

    /// <summary>
    /// 등급이 한 단계 오를 때마다 예측화력이 얼마나 오르는지(사용자 지정 15%).
    /// 예측화력 = 공격력 x 투사체수 x 공속 으로 정의하고, 이 증가분을 <b>공격력에만</b> 반영한다.
    /// 공속까지 함께 올리면 전기톱검(3/s)이 5.2/s가 되어 발사 빈도가 붕괴하고,
    /// 사거리를 올리면 "화면 밖 교전" 문제가 등급별로 되살아난다.
    /// </summary>
    private const float PowerPerGrade = 1.15f;

    /// <summary>등급별 가격 배율. ShopCatalog.GradeSetting.priceMultiplier와 같은 값을 쓴다.</summary>
    private static readonly float[] PriceMultipliers = { 1.0f, 1.8f, 3.0f, 5.0f, 8.0f };

    // 회전 속도 5단계(도/초) - 기획 문서의 "매우낮음~매우높음" 표현을 수치로 옮긴 것
    private const float RotVeryLow = 180f;
    private const float RotLow = 270f;
    private const float RotSlightlyLow = 360f;
    private const float RotNormal = 480f;
    private const float RotSlightlyHigh = 600f;
    private const float RotHigh = 780f;
    private const float RotVeryHigh = 1080f;

    // 탄퍼짐 5단계(반각, 도)
    private const float AimVeryLow = 0.5f;
    private const float AimLow = 1.5f;
    private const float AimNormal = 3f;
    private const float AimSlightlyHigh = 5f;
    private const float AimHigh = 8f;

    /// <summary>무기 한 종류의 <b>일반 등급</b> 정의. 상위 등급은 여기서 파생된다.</summary>
    private struct WeaponDef
    {
        public int kind;              // 종류 번호 1~13 → ID = 300000 + kind*100 + 등급(1~5)
        public string name;
        public WeaponClass wclass;    // 소켓 장착 제한용 (경/중/근접)
        public WeaponType wtype;      // 분류/표시용 (연사/산탄/정밀/폭발/에너지/근접)

        public float atk;             // 일반 등급 공격력 (상위 등급은 x1.15^g)
        public float atsp;            // 초당 공격 횟수 (대기시간 = 1/atsp, 발사 종료 후부터)
        public float range;           // 사거리(유닛) = 기획 수치 / 50
        public float detect;          // 감지거리(유닛)
        public float speed;           // 투사체 속도
        public float rotspeed;        // 조준 회전 속도
        public float atsize;          // 투사체 크기 (빔은 반폭)
        public float aim;             // 탄퍼짐 반각
        public float rebound;         // 다중탄 각도 간격
        public int projectiles;
        public int pierce;            // 0=없음 / N=N회 / -1=무제한
        public float pierceChance;    // 0=확정
        public float splash;          // 폭발 반경(유닛)
        public float defignore;       // 방어력 무시 비율
        public float knockback;
        public float duration;        // 빔 지속시간
        public WeaponFireMode firemode;

        public float imgscale;
        public float weight;          // 자기장코어+다리 지탱력과 비교되는 무게
        public int basePrice;         // 일반 등급 가격 (상위 등급은 x PriceMultipliers)

        public string tanhwan;
        public string leftImg;
        public string rightImg;
    }

    /// <summary>
    /// 사람이 편집하는 유일한 곳. 거리 값은 전부 <b>기획 문서 수치 ÷ 50</b>(1유닛 = 50)이며,
    /// 근접 3종만 적 몸통 접촉 거리(약 1.4유닛)를 넘기도록 +1.0유닛 오프셋을 줬다.
    ///
    /// 공격력 기준(플레이 실측으로 잡은 값):
    /// 웨이브 1의 스폰 압력이 <b>초당 3.75마리</b>(배치 3마리 / 0.8초)이므로, 소켓 2정으로
    /// 그보다 빠르게 처치하지 못하면 적이 상한(30마리)까지 쌓여 플레이어가 포위당해 죽는다.
    /// 그래서 예측화력을 중무장 90 / 경무장 60 / 근접 60 (1정 기준) 밴드로 맞췄다.
    /// 관통·스플래시가 있는 무기는 한 번에 여러 마리를 때리므로 명목 수치를 그만큼 낮게 잡았다
    /// (예: 대물저격총은 명목 30이지만 3관통이라 실효 90).
    /// </summary>
    private static readonly WeaponDef[] Defs =
    {
        // ===== 중무장 5종 =====
        new WeaponDef {
            kind = 1, name = "중기관총", wclass = WeaponClass.Heavy, wtype = WeaponType.RapidFire,
            atk = 9f, atsp = 10f, range = 7.0f, detect = 6.0f, speed = 22f, rotspeed = RotNormal,
            atsize = 0.25f, aim = AimNormal, rebound = 0f, projectiles = 1,
            pierce = 0, pierceChance = 0f, splash = 0f, defignore = 0f, knockback = 0f, duration = 0f,
            firemode = WeaponFireMode.Projectile,
            imgscale = 1.15f, weight = 5.0f, basePrice = 45,
            tanhwan = "Bullets", leftImg = "LeftHMG", rightImg = "RightHMG"
        },
        new WeaponDef {
            kind = 2, name = "전투산탄총", wclass = WeaponClass.Heavy, wtype = WeaponType.Shotgun,
            atk = 14f, atsp = 0.8f, range = 4.0f, detect = 3.0f, speed = 18f, rotspeed = RotHigh,
            atsize = 0.2f, aim = AimHigh, rebound = 4f, projectiles = 8,
            pierce = 1, pierceChance = 0.6f, splash = 0f, defignore = 0f, knockback = 0f, duration = 0f,
            firemode = WeaponFireMode.Projectile,
            imgscale = 1.15f, weight = 4.5f, basePrice = 45,
            tanhwan = "Bullets", leftImg = "LeftCombatShotgun", rightImg = "RightCombatShotgun"
        },
        new WeaponDef {
            kind = 3, name = "대물저격총", wclass = WeaponClass.Heavy, wtype = WeaponType.Precision,
            atk = 120f, atsp = 0.25f, range = 15.0f, detect = 8.0f, speed = 40f, rotspeed = RotSlightlyLow,
            atsize = 0.3f, aim = AimVeryLow, rebound = 0f, projectiles = 1,
            pierce = 3, pierceChance = 0f, splash = 0f, defignore = 0f, knockback = 0f, duration = 0f,
            firemode = WeaponFireMode.Projectile,
            imgscale = 1.15f, weight = 5.5f, basePrice = 55,
            tanhwan = "Bullets", leftImg = "LeftAMR", rightImg = "RightAMR"
        },
        new WeaponDef {
            // 3초 동안 총 90 데미지를 나눠 넣고(빔), 발사가 끝난 뒤 2초를 쉰다(atsp 0.5 = 대기 2초).
            // 대기시간이 발사 종료 후부터 흐르므로 빔이 여러 개 겹치지 않는다.
            kind = 4, name = "플라즈마캐논", wclass = WeaponClass.Heavy, wtype = WeaponType.Energy,
            atk = 150f, atsp = 0.5f, range = 8.0f, detect = 7.0f, speed = 0f, rotspeed = RotLow,
            atsize = 0.5f, aim = AimVeryLow, rebound = 0f, projectiles = 1,
            pierce = -1, pierceChance = 0f, splash = 0f, defignore = 0.5f, knockback = 0f, duration = 3.0f,
            firemode = WeaponFireMode.Beam,
            imgscale = 1.15f, weight = 6.0f, basePrice = 60,
            tanhwan = "Energy", leftImg = "LeftPlasmaCannon", rightImg = "RightPlasmaCannon"
        },
        new WeaponDef {
            kind = 5, name = "로켓런처", wclass = WeaponClass.Heavy, wtype = WeaponType.Explosive,
            atk = 75f, atsp = 0.4f, range = 7.0f, detect = 4.8f, speed = 14f, rotspeed = RotSlightlyHigh,
            atsize = 0.3f, aim = AimVeryLow, rebound = 0f, projectiles = 1,
            pierce = 0, pierceChance = 0f, splash = 2.4f, defignore = 0f, knockback = 0f, duration = 0f,
            firemode = WeaponFireMode.Projectile,
            imgscale = 1.15f, weight = 5.5f, basePrice = 55,
            tanhwan = "Bullets", leftImg = "LeftRocketLauncher", rightImg = "RightRocketLauncher"
        },

        // ===== 경무장 5종 =====
        new WeaponDef {
            kind = 6, name = "소드오프샷건", wclass = WeaponClass.Light, wtype = WeaponType.Shotgun,
            atk = 8f, atsp = 1.2f, range = 3.6f, detect = 2.6f, speed = 18f, rotspeed = RotVeryHigh,
            atsize = 0.2f, aim = AimHigh, rebound = 5f, projectiles = 6,
            pierce = 1, pierceChance = 0.4f, splash = 0f, defignore = 0f, knockback = 0f, duration = 0f,
            firemode = WeaponFireMode.Projectile,
            imgscale = 1.0f, weight = 2.0f, basePrice = 25,
            tanhwan = "Bullets", leftImg = "LeftSawedOff", rightImg = "RightSawedOff"
        },
        new WeaponDef {
            kind = 7, name = "레이저피스톨", wclass = WeaponClass.Light, wtype = WeaponType.Energy,
            atk = 30f, atsp = 2f, range = 6.0f, detect = 5.0f, speed = 30f, rotspeed = RotVeryHigh,
            atsize = 0.2f, aim = AimVeryLow, rebound = 0f, projectiles = 1,
            pierce = 1, pierceChance = 0.5f, splash = 0f, defignore = 0.25f, knockback = 0f, duration = 0f,
            firemode = WeaponFireMode.Projectile,
            imgscale = 0.85f, weight = 1.5f, basePrice = 25,
            tanhwan = "Energy", leftImg = "LeftLaserPistol", rightImg = "RightLaserPistol"
        },
        new WeaponDef {
            kind = 8, name = "지정사수소총", wclass = WeaponClass.Light, wtype = WeaponType.Precision,
            atk = 55f, atsp = 0.55f, range = 10.0f, detect = 7.0f, speed = 32f, rotspeed = RotNormal,
            atsize = 0.25f, aim = AimVeryLow, rebound = 0f, projectiles = 1,
            pierce = 1, pierceChance = 0f, splash = 0f, defignore = 0f, knockback = 0f, duration = 0f,
            firemode = WeaponFireMode.Projectile,
            imgscale = 1.0f, weight = 3.0f, basePrice = 40,
            tanhwan = "Bullets", leftImg = "LeftDMR", rightImg = "RightDMR"
        },
        new WeaponDef {
            // 기존 기관단총 스프라이트(LeftSMG/RightSMG)를 그대로 재사용한다 - 신규 리소스와 바이트가 동일
            kind = 9, name = "기관단총", wclass = WeaponClass.Light, wtype = WeaponType.RapidFire,
            atk = 10f, atsp = 2f, range = 6.4f, detect = 5.6f, speed = 22f, rotspeed = RotHigh,
            atsize = 0.22f, aim = AimSlightlyHigh, rebound = 3f, projectiles = 3,
            pierce = 0, pierceChance = 0f, splash = 0f, defignore = 0f, knockback = 0f, duration = 0f,
            firemode = WeaponFireMode.Projectile,
            imgscale = 0.85f, weight = 2.5f, basePrice = 30,
            tanhwan = "Bullets", leftImg = "LeftSMG", rightImg = "RightSMG"
        },
        new WeaponDef {
            kind = 10, name = "유탄발사기", wclass = WeaponClass.Light, wtype = WeaponType.Explosive,
            atk = 40f, atsp = 0.5f, range = 5.0f, detect = 4.0f, speed = 12f, rotspeed = RotHigh,
            atsize = 0.3f, aim = AimVeryLow, rebound = 0f, projectiles = 1,
            pierce = 0, pierceChance = 0f, splash = 1.5f, defignore = 0f, knockback = 0f, duration = 0f,
            firemode = WeaponFireMode.Projectile,
            imgscale = 1.0f, weight = 3.0f, basePrice = 35,
            tanhwan = "Bullets", leftImg = "LeftGrenadeLauncher", rightImg = "RightGrenadeLauncher"
        },

        // ===== 근접 3종 (이미지가 방향별로 없어 좌우 공용) =====
        new WeaponDef {
            kind = 11, name = "생존단검", wclass = WeaponClass.Melee, wtype = WeaponType.Melee,
            atk = 33f, atsp = 1.8f, range = 2.2f, detect = 2.2f, speed = 0f, rotspeed = RotVeryHigh,
            atsize = 1f, aim = 0f, rebound = 0f, projectiles = 1,
            pierce = 0, pierceChance = 0f, splash = 0f, defignore = 0f, knockback = 5f, duration = 0f,
            firemode = WeaponFireMode.MeleeSwing,
            imgscale = 0.5f, weight = 1.0f, basePrice = 20,
            tanhwan = "", leftImg = "SurvivalKnife", rightImg = "SurvivalKnife"
        },
        new WeaponDef {
            kind = 12, name = "전술 마체테", wclass = WeaponClass.Melee, wtype = WeaponType.Melee,
            atk = 50f, atsp = 1.2f, range = 2.6f, detect = 2.6f, speed = 0f, rotspeed = RotVeryHigh,
            atsize = 1f, aim = 0f, rebound = 0f, projectiles = 1,
            pierce = 0, pierceChance = 0f, splash = 0f, defignore = 0f, knockback = 9f, duration = 0f,
            firemode = WeaponFireMode.MeleeSwing,
            imgscale = 0.65f, weight = 1.5f, basePrice = 25,
            tanhwan = "", leftImg = "Machete", rightImg = "Machete"
        },
        new WeaponDef {
            kind = 13, name = "전기톱검", wclass = WeaponClass.Melee, wtype = WeaponType.Melee,
            atk = 20f, atsp = 3f, range = 2.4f, detect = 2.4f, speed = 0f, rotspeed = RotVeryHigh,
            atsize = 1f, aim = 0f, rebound = 0f, projectiles = 1,
            pierce = 0, pierceChance = 0f, splash = 0f, defignore = 0f, knockback = 5f, duration = 0f,
            firemode = WeaponFireMode.MeleeSwing,
            imgscale = 0.65f, weight = 2.0f, basePrice = 30,
            tanhwan = "", leftImg = "ChainsawSword", rightImg = "ChainsawSword"
        }
    };

    [MenuItem("Comstock/무기 테이블 65행 재생성")]
    public static void Generate()
    {
        var gameData = AssetDatabase.LoadAssetAtPath<GameDataAsset>(GameDataPath);
        var partsCatalog = AssetDatabase.LoadAssetAtPath<PartsCatalog>(PartsCatalogPath);
        var shopCatalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);

        if (gameData == null || partsCatalog == null || shopCatalog == null)
        {
            Debug.LogError($"에셋을 찾을 수 없습니다. GameData={gameData != null} Parts={partsCatalog != null} Shop={shopCatalog != null}");
            return;
        }

        var weapons = new List<WeaponData>();
        var metas = new List<PartsCatalog.WeaponMetaEntry>();
        var entries = new List<ShopCatalog.WeaponEntry>();

        foreach (WeaponDef def in Defs)
        {
            for (int g = 0; g < GradeCount; g++)
            {
                int id = MakeWeaponId(def.kind, g);
                float powerScale = Mathf.Pow(PowerPerGrade, g);

                weapons.Add(new WeaponData
                {
                    weapon_id = id,
                    weapon_name = def.name,
                    weapon_grade = (ItemGrade)g,

                    // 등급 상승분은 공격력에만 반영한다(예측화력 15%/등급)
                    weapon_atk = Round2(def.atk * powerScale),
                    weapon_atsp = def.atsp,
                    weapon_range = def.range,
                    weapon_detect = def.detect,
                    weapon_speed = def.speed,
                    weapon_rotspeed = def.rotspeed,
                    weapon_atsize = def.atsize,
                    weapon_aim = def.aim,
                    weapon_rebound = def.rebound,
                    weapon_projectiles = def.projectiles,
                    weapon_pierce = def.pierce,
                    weapon_pierce_chance = def.pierceChance,
                    weapon_splash = def.splash,
                    weapon_defignore = def.defignore,
                    weapon_knockback = def.knockback,
                    weapon_duration = def.duration,
                    weapon_firemode = def.firemode,
                    weapon_imgscale = def.imgscale,
                    weapon_imgangle = 0f, // 실제 그림의 총구 각도를 보고 맞추는 값 - 화면 확인 후 조정
                    weapon_tanhwan = def.tanhwan,
                    weapon_lfwpimg = def.leftImg,
                    weapon_rgwpimg = def.rightImg
                });

                // 무게와 타입은 등급과 무관하게 같다(상위 등급이 더 무거워지면 성장이 오히려 손해가 된다)
                metas.Add(new PartsCatalog.WeaponMetaEntry
                {
                    weaponId = id,
                    type = def.wtype,
                    weaponClass = def.wclass,
                    weight = def.weight
                });

                entries.Add(new ShopCatalog.WeaponEntry
                {
                    weaponId = id,
                    basePrice = Mathf.RoundToInt(def.basePrice * PriceMultipliers[g])
                });
            }
        }

        // weapons만 교체한다 - monsters/robots/amors/drops는 손대지 않는다
        gameData.weapons = weapons;
        partsCatalog.EditorSetWeaponMeta(metas);
        shopCatalog.EditorSetWeaponEntries(entries);

        EditorUtility.SetDirty(gameData);
        EditorUtility.SetDirty(partsCatalog);
        EditorUtility.SetDirty(shopCatalog);
        AssetDatabase.SaveAssets();

        Debug.Log(BuildReport(weapons, metas, entries));
    }

    /// <summary>ID 체계: 300000 + 종류(1~13) x 100 + 등급(1~5).</summary>
    public static int MakeWeaponId(int kind, int gradeIndex) => 300000 + kind * 100 + (gradeIndex + 1);

    // 소수 둘째 자리까지만 남긴다(1.15^4 같은 값이 지저분하게 길어지는 것을 막는다)
    private static float Round2(float value) => Mathf.Round(value * 100f) / 100f;

    private static string BuildReport(List<WeaponData> weapons, List<PartsCatalog.WeaponMetaEntry> metas, List<ShopCatalog.WeaponEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"무기 테이블 재생성 완료 - 무기 {weapons.Count}행 / 무기메타 {metas.Count}행 / 상점 {entries.Count}행");
        sb.AppendLine("종류별 예측화력(공격력 x 투사체 x 공속), 일반 → 전설:");

        for (int i = 0; i < Defs.Length; i++)
        {
            WeaponDef def = Defs[i];
            float normal = def.atk * def.projectiles * def.atsp;
            float legend = def.atk * Mathf.Pow(PowerPerGrade, 4) * def.projectiles * def.atsp;
            int firstId = MakeWeaponId(def.kind, 0);

            sb.AppendLine($"  {firstId} {def.name,-8} {normal,6:0.#} → {legend,6:0.#}  " +
                          $"(사거리 {def.range:0.##} / 감지 {def.detect:0.##} / 무게 {def.weight:0.#})");
        }

        return sb.ToString();
    }
}
