using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    [Header("게시된 CSV 링크 (구글시트 gid별)")]
    [SerializeField] private string monsterCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?gid=67460213&single=true&output=csv";
    [SerializeField] private string robotCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?output=csv";
    [SerializeField] private string weaponCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?gid=1779041701&single=true&output=csv";
    [SerializeField] private string amorCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?gid=1473615208&single=true&output=csv";
    [SerializeField] private string dropCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vR3ZmogRkqtQS-ihFC-Y1UurF2CrNFOWp5STHgQ2XUtk6FwN_ccxuEXOvkT_vumWa-_ORbr4Kh900Cl/pub?gid=161526836&single=true&output=csv";

    public Dictionary<int, MonsterData> Monsters { get; private set; } = new Dictionary<int, MonsterData>();
    public Dictionary<int, RobotData> Robots { get; private set; } = new Dictionary<int, RobotData>();
    public Dictionary<int, WeaponData> Weapons { get; private set; } = new Dictionary<int, WeaponData>();
    public Dictionary<int, AmorData> Amors { get; private set; } = new Dictionary<int, AmorData>();
    public Dictionary<int, List<DropEntry>> DropsByMonster { get; private set; } = new Dictionary<int, List<DropEntry>>();

    public bool IsLoaded { get; private set; } = false;
    public event Action OnLoaded;

    private int loadedCount = 0;
    private const int TOTAL_TABLES = 5;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(LoadCsv(monsterCsvUrl, ParseMonsterRow));
        StartCoroutine(LoadCsv(robotCsvUrl, ParseRobotRow));
        StartCoroutine(LoadCsv(weaponCsvUrl, ParseWeaponRow));
        StartCoroutine(LoadCsv(amorCsvUrl, ParseAmorRow));
        StartCoroutine(LoadCsv(dropCsvUrl, ParseDropRow));
    }

    private IEnumerator LoadCsv(string url, Action<string[]> rowParser)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"CSV 로드 실패: {url} / {req.error}");
            }
            else
            {
                var rows = CsvParser.ParseDataRows(req.downloadHandler.text);
                foreach (var row in rows)
                {
                    try
                    {
                        rowParser(row);
                    }
                    catch (Exception e)
                    {
                        // 컬럼 개수가 부족하거나 형식이 안 맞는 행이 있어도
                        // 그 행만 스킵하고 나머지 데이터는 정상적으로 로드되도록 처리
                        Debug.LogError($"행 파싱 실패 (url: {url}) / row: {string.Join(",", row)} / {e.Message}");
                    }
                }
            }

            loadedCount++;
            if (loadedCount >= TOTAL_TABLES)
            {
                IsLoaded = true;
                OnLoaded?.Invoke();
            }
        }
    }

    private static float F(string s) => string.IsNullOrEmpty(s) ? 0f : float.Parse(s, CultureInfo.InvariantCulture);
    private static int I(string s) => string.IsNullOrEmpty(s) ? 0 : int.Parse(s, CultureInfo.InvariantCulture);

    // 시트에 아직 추가되지 않은 컬럼(인덱스 범위 밖)에 접근해도 예외 대신 null(→기본값 0)을 반환
    private static string Col(string[] c, int index) => index < c.Length ? c[index] : null;

    // "TRUE"/"FALSE" 문자열(대소문자 무관)을 bool로 변환. 값이 없거나 형식이 다르면 false
    private static bool B(string s) => !string.IsNullOrEmpty(s) && bool.TryParse(s, out bool result) && result;

    // 문자열 컬럼. 앞뒤 공백/개행(\r)을 제거해서 프리팹 이름 비교가 어긋나지 않도록 한다
    private static string S(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Trim();

    private void ParseMonsterRow(string[] c)
    {
        var d = new MonsterData
        {
            monster_id = I(c[0]), monster_name = c[1], monster_hp = I(c[2]), monster_atk = I(c[3]),
            monster_def = I(c[4]), monster_speed = F(c[5]), monster_range = F(c[6]),
            monster_type = I(c[7]), monster_atsp = F(c[8])
        };
        Monsters[d.monster_id] = d;
    }

    private void ParseRobotRow(string[] c)
    {
        var d = new RobotData
        {
            robot_id = I(c[0]), robot_name = c[1], robot_hp = I(c[2]), robot_atk = I(c[3]),
            robot_def = I(c[4]), robot_cc = F(c[5]), robot_cd = F(c[6]), robot_speed = F(c[7]),
            robot_capacity = F(c[8]), robot_reload = F(c[9]), robot_avoid = F(c[10]),
            robot_luck = F(c[11]), robot_mess = F(c[12]), robot_special = I(Col(c, 13))
        };
        Robots[d.robot_id] = d;
    }

    private void ParseWeaponRow(string[] c)
    {
        var d = new WeaponData
        {
            weapon_id = I(Col(c, 0)), weapon_name = Col(c, 1), weapon_atk = I(Col(c, 2)), weapon_atsp = F(Col(c, 3)),
            weapon_range = I(Col(c, 4)), weapon_atsize = F(Col(c, 5)), weapon_aim = F(Col(c, 6)),
            weapon_rebound = F(Col(c, 7)), weapon_projectiles = I(Col(c, 8)), weapon_capacity = I(Col(c, 9)),
            weapon_reload = I(Col(c, 10)), weapon_penetration = B(Col(c, 11)),
            weapon_tanhwan = S(Col(c, 12)), // 무기 발사 탄환 = 투사체 프리팹 이름
            weapon_lfwpimg = S(Col(c, 13)), // 무기 왼손 이미지
            weapon_rgwpimg = S(Col(c, 14))  // 무기 오른손 이미지
        };
        Weapons[d.weapon_id] = d;
    }

    private void ParseAmorRow(string[] c)
    {
        var d = new AmorData
        {
            amor_id = I(c[0]), amor_name = c[1], amor_hp = I(c[2]), amor_def = I(c[3]),
            amor_speed = F(c[4]), amor_avoid = F(c[5])
        };
        Amors[d.amor_id] = d;
    }

    private void ParseDropRow(string[] c)
    {
        var d = new DropEntry { monster_id = I(c[0]), item_id = I(c[1]), item_drop = F(c[2]) };
        if (!DropsByMonster.ContainsKey(d.monster_id)) DropsByMonster[d.monster_id] = new List<DropEntry>();
        DropsByMonster[d.monster_id].Add(d);
    }
}
