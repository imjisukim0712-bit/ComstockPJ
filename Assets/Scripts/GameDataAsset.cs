using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 데이터 테이블(몬스터/로봇/무기/방어구/드랍)을 로컬 에셋으로 보관한다.
/// 이전에는 GameDataManager가 런타임에 Google Sheets CSV를 다운로드해 채웠으나,
/// 네트워크 의존을 없애기 위해 이 에셋에 값을 미리 저장해두고 GameDataManager가
/// 여기서 동기적으로 읽어온다.
/// 값을 채우거나 갱신할 때는 GameDataCsvImporter(Editor 전용) 도구를 사용한다.
/// </summary>
[CreateAssetMenu(fileName = "GameDataAsset", menuName = "Comstock/게임 데이터 에셋")]
public class GameDataAsset : ScriptableObject
{
    public List<MonsterData> monsters = new List<MonsterData>();
    public List<RobotData> robots = new List<RobotData>();
    public List<WeaponData> weapons = new List<WeaponData>();
    public List<AmorData> amors = new List<AmorData>();
    public List<DropEntry> drops = new List<DropEntry>();
}
