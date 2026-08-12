using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 데이터 테이블(몬스터/로봇/무기/방어구)을 보관하는 **유일한 출처**.
/// GameDataManager가 Awake에서 여기서 동기적으로 읽어간다.
///
/// 값을 바꾸려면 Unity 인스펙터에서 이 에셋을 직접 편집한다. 외부(구글 시트 등)에서
/// 가져오는 경로는 없다 - 사용자 지시로 시트 연동을 완전히 제거했으므로, 다시 추가하지 말 것.
/// </summary>
[CreateAssetMenu(fileName = "GameDataAsset", menuName = "Comstock/게임 데이터 에셋")]
public class GameDataAsset : ScriptableObject
{
    public List<MonsterData> monsters = new List<MonsterData>();
    public List<RobotData> robots = new List<RobotData>();
    public List<WeaponData> weapons = new List<WeaponData>();
    public List<AmorData> amors = new List<AmorData>();
}
