using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 코어가 레벨업할 때 정비 시간에 제시되는 업그레이드 선택지 풀.
/// 레벨업마다 이 목록에서 서로 다른 3개를 무작위로 뽑아 카드로 보여준다(AiCoreManager 참고).
/// 시트에서 가져오는 데이터가 아니라 기획자가 직접 채워 넣는 로컬 전용 데이터라
/// GameDataAsset과 별도의 에셋으로 관리한다.
/// </summary>
[CreateAssetMenu(fileName = "AiCoreUpgradePool", menuName = "Comstock/AI 코어 업그레이드 풀")]
public class AiCoreUpgradePool : ScriptableObject
{
    [Serializable]
    public struct Option
    {
        public string displayName;
        [TextArea] public string description;
        public StatType statType;
        public float amount;
    }

    public List<Option> options = new List<Option>();
}
