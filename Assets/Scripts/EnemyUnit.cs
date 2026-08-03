using UnityEngine;

public class EnemyUnit : MonoBehaviour
{
    public int MonsterId { get; private set; }
    public int MaxHp { get; private set; }
    public int CurrentHp { get; private set; }
    public int Atk { get; private set; }
    public int Def { get; private set; }
    public float MoveSpeed { get; private set; }

    public void Init(MonsterData data)
    {
        MonsterId = data.monster_id;
        MaxHp = data.monster_hp;
        CurrentHp = data.monster_hp;
        Atk = data.monster_atk;
        Def = data.monster_def;
        MoveSpeed = data.monster_speed;
    }

    public void TakeDamage(int amount)
    {
        int dmg = Mathf.Max(1, amount - Def);
        CurrentHp -= dmg;
        if (CurrentHp <= 0) Die();
    }

    private void Die()
    {
        int? droppedItemId = DropTableManager.RollDrop(MonsterId);
        if (droppedItemId.HasValue)
        {
            // TODO: 실제 아이템 픽업 오브젝트 생성/지급 로직은 여기에 연결
            Debug.Log($"몬스터 {MonsterId} 사망 → 아이템 {droppedItemId.Value} 드랍");
        }

        Destroy(gameObject);
    }
}
