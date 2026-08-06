using System;
using System.Globalization;
using System.Net;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기획자가 채운 Google Sheets(게시된 CSV 링크)를 읽어 GameDataAsset(로컬 에셋)에 채워 넣는
/// 에디터 전용 도구. 런타임(GameDataManager)은 더 이상 네트워크를 사용하지 않으므로,
/// 시트 내용이 바뀌면 이 창을 열어 "가져오기"를 눌러 로컬 에셋을 갱신하고 커밋한다.
/// 메뉴: Comstock/게임 데이터 가져오기
/// </summary>
public class GameDataCsvImporter : EditorWindow
{
    private const string MonsterCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?gid=67460213&single=true&output=csv";
    private const string RobotCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?output=csv";
    private const string WeaponCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?gid=1779041701&single=true&output=csv";
    private const string AmorCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?gid=1473615208&single=true&output=csv";
    private const string DropCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?gid=161526836&single=true&output=csv";

    private GameDataAsset targetAsset;
    private string monsterUrl = MonsterCsvUrl;
    private string robotUrl = RobotCsvUrl;
    private string weaponUrl = WeaponCsvUrl;
    private string amorUrl = AmorCsvUrl;
    private string dropUrl = DropCsvUrl;

    [MenuItem("Comstock/게임 데이터 가져오기")]
    private static void Open()
    {
        GetWindow<GameDataCsvImporter>("게임 데이터 가져오기");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "구글시트(게시된 CSV 링크)를 읽어 아래 GameDataAsset에 저장합니다.\n" +
            "런타임은 이 에셋만 읽으므로, 시트를 수정한 뒤에는 반드시 여기서 다시 가져와야 반영됩니다.",
            MessageType.Info);

        targetAsset = (GameDataAsset)EditorGUILayout.ObjectField("대상 에셋", targetAsset, typeof(GameDataAsset), false);

        EditorGUILayout.Space();
        monsterUrl = EditorGUILayout.TextField("몬스터 CSV URL", monsterUrl);
        robotUrl = EditorGUILayout.TextField("로봇 CSV URL", robotUrl);
        weaponUrl = EditorGUILayout.TextField("무기 CSV URL", weaponUrl);
        amorUrl = EditorGUILayout.TextField("방어구 CSV URL", amorUrl);
        dropUrl = EditorGUILayout.TextField("드랍 CSV URL", dropUrl);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(targetAsset == null))
        {
            if (GUILayout.Button("가져오기"))
            {
                ImportInto(targetAsset, monsterUrl, robotUrl, weaponUrl, amorUrl, dropUrl);
            }
        }
    }

    /// <summary>
    /// 5개 CSV를 동기적으로 다운로드해 asset에 채워 넣고 저장한다.
    /// script-execute 등에서 창을 열지 않고도 호출할 수 있도록 static으로 노출한다.
    /// </summary>
    public static void ImportInto(GameDataAsset asset, string monsterUrl, string robotUrl, string weaponUrl, string amorUrl, string dropUrl)
    {
        if (asset == null)
        {
            Debug.LogError("가져오기 대상 GameDataAsset이 없습니다.");
            return;
        }

        try
        {
            using (var client = new WebClient())
            {
                asset.monsters.Clear();
                foreach (var row in CsvParser.ParseDataRows(client.DownloadString(monsterUrl)))
                    asset.monsters.Add(ParseMonsterRow(row));

                asset.robots.Clear();
                foreach (var row in CsvParser.ParseDataRows(client.DownloadString(robotUrl)))
                    asset.robots.Add(ParseRobotRow(row));

                asset.weapons.Clear();
                foreach (var row in CsvParser.ParseDataRows(client.DownloadString(weaponUrl)))
                    asset.weapons.Add(ParseWeaponRow(row));

                asset.amors.Clear();
                foreach (var row in CsvParser.ParseDataRows(client.DownloadString(amorUrl)))
                    asset.amors.Add(ParseAmorRow(row));

                asset.drops.Clear();
                foreach (var row in CsvParser.ParseDataRows(client.DownloadString(dropUrl)))
                    asset.drops.Add(ParseDropRow(row));
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log($"게임 데이터 가져오기 완료 - 몬스터 {asset.monsters.Count} / 로봇 {asset.robots.Count} / " +
                      $"무기 {asset.weapons.Count} / 방어구 {asset.amors.Count} / 드랍 {asset.drops.Count}행");
        }
        catch (Exception e)
        {
            Debug.LogError($"게임 데이터 가져오기 실패: {e.Message}");
        }
    }

    private static float F(string s) => string.IsNullOrEmpty(s) ? 0f : float.Parse(s, CultureInfo.InvariantCulture);
    private static int I(string s) => string.IsNullOrEmpty(s) ? 0 : int.Parse(s, CultureInfo.InvariantCulture);
    private static string Col(string[] c, int index) => index < c.Length ? c[index] : null;
    private static bool B(string s) => !string.IsNullOrEmpty(s) && bool.TryParse(s, out bool result) && result;
    private static string S(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Trim();

    private static MonsterData ParseMonsterRow(string[] c) => new MonsterData
    {
        monster_id = I(c[0]), monster_name = c[1], monster_hp = I(c[2]), monster_atk = I(c[3]),
        monster_def = I(c[4]), monster_speed = F(c[5]), monster_range = F(c[6]),
        monster_type = I(c[7]), monster_atsp = F(c[8])
    };

    private static RobotData ParseRobotRow(string[] c) => new RobotData
    {
        robot_id = I(c[0]), robot_name = c[1], robot_hp = I(c[2]), robot_atk = I(c[3]),
        robot_def = I(c[4]), robot_cc = F(c[5]), robot_cd = F(c[6]), robot_speed = F(c[7]),
        robot_capacity = F(c[8]), robot_reload = F(c[9]), robot_avoid = F(c[10]),
        robot_luck = F(c[11]), robot_mess = F(c[12]), robot_special = I(Col(c, 13))
    };

    private static WeaponData ParseWeaponRow(string[] c) => new WeaponData
    {
        weapon_id = I(Col(c, 0)), weapon_name = Col(c, 1), weapon_atk = I(Col(c, 2)), weapon_atsp = F(Col(c, 3)),
        weapon_range = I(Col(c, 4)), weapon_atsize = F(Col(c, 5)), weapon_aim = F(Col(c, 6)),
        weapon_rebound = F(Col(c, 7)), weapon_projectiles = I(Col(c, 8)), weapon_capacity = I(Col(c, 9)),
        weapon_reload = I(Col(c, 10)), weapon_penetration = B(Col(c, 11)),
        weapon_tanhwan = S(Col(c, 12)),
        weapon_lfwpimg = S(Col(c, 13)),
        weapon_rgwpimg = S(Col(c, 14))
    };

    private static AmorData ParseAmorRow(string[] c) => new AmorData
    {
        amor_id = I(c[0]), amor_name = c[1], amor_hp = I(c[2]), amor_def = I(c[3]),
        amor_speed = F(c[4]), amor_avoid = F(c[5])
    };

    private static DropEntry ParseDropRow(string[] c) => new DropEntry
    {
        monster_id = I(c[0]), item_id = I(c[1]), item_drop = F(c[2])
    };
}
