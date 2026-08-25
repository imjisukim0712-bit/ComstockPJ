using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 디버깅용 부품상자 - 정비 화면을 거치지 않고 지정한 파츠를 즉시 장착해서 테스트하는 도구.
/// 하이라키에서 우클릭 → Comstock → 디버그 부품상자로 추가한다(DebugPartBoxMenu.cs 참고).
///
/// 실제 상점/드랍 부품상자(RewardPickup)와 달리 <b>무작위가 아니라 인스펙터에서 고른 슬롯의
/// 파츠를 정확히</b> 장착하며, 플레이 중 키를 눌러 같은 슬롯의 다른 파츠로 즉시 순환할 수
/// 있다(예: 다리 4종을 하나씩 빠르게 넘겨보기 - F2/F3).
///
/// 씬에 여러 개 놓아 슬롯별로 따로 테스트할 수도 있다(다리용 하나 + 헬멧용 하나 등).
/// </summary>
public class DebugPartBox : MonoBehaviour
{
    [Tooltip("장착할 파츠 슬롯")]
    [SerializeField] private PartSlot slot = PartSlot.Leg;

    [Tooltip("같은 슬롯의 파츠를 partId 순으로 나열했을 때의 인덱스. 플레이 중 순환 키로도 바뀐다")]
    [SerializeField] private int partIndex = 0;

    [Tooltip("체크하면 씬 시작과 동시에(정비 화면 없이) 바로 장착한다")]
    [SerializeField] private bool applyOnStart = true;

    [Tooltip("다음 파츠로 순환하는 키")]
    [SerializeField] private Key nextKey = Key.F3;
    [Tooltip("이전 파츠로 순환하는 키")]
    [SerializeField] private Key prevKey = Key.F2;

    private readonly List<PartData> options = new List<PartData>();

    private void Awake()
    {
        RefreshOptions();
        if (applyOnStart) ApplyCurrent();
    }

    private void Update()
    {
        if (Keyboard.current == null || options.Count == 0) return;

        if (Keyboard.current[nextKey].wasPressedThisFrame)
        {
            partIndex = (partIndex + 1) % options.Count;
            ApplyCurrent();
        }
        else if (Keyboard.current[prevKey].wasPressedThisFrame)
        {
            partIndex = (partIndex - 1 + options.Count) % options.Count;
            ApplyCurrent();
        }
    }

    private void RefreshOptions()
    {
        options.Clear();
        ModdingManager modding = ModdingManager.Instance;
        if (modding == null || modding.Catalog == null)
        {
            Debug.LogWarning("[디버그 부품상자] ModdingManager/PartsCatalog를 찾을 수 없습니다. " +
                              "씬에 ModdingManager가 배치되어 있는지 확인하세요.");
            return;
        }

        foreach (PartData part in modding.Catalog.Parts)
        {
            if (part.slot == slot) options.Add(part);
        }
        options.Sort((a, b) => a.partId.CompareTo(b.partId));

        if (options.Count == 0)
            Debug.LogWarning($"[디버그 부품상자] {slot.ToDisplayName()} 슬롯에 파츠가 없습니다.");
    }

    private void ApplyCurrent()
    {
        if (options.Count == 0) return;
        partIndex = ((partIndex % options.Count) + options.Count) % options.Count;

        PartData part = options[partIndex];
        ModdingManager.Instance?.EquipPart(part);
        Debug.Log($"[디버그 부품상자] {slot.ToDisplayName()} 슬롯 {partIndex + 1}/{options.Count} -> " +
                  $"'{part.Part()}'(id {part.partId}) 장착");
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.65f, 0f, 0.9f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.6f, "디버그 부품상자\n" + slot.ToDisplayName());
    }
#endif
}
