/// <summary>
/// 데이터 에셋에 들어 있는 이름·설명을 화면에 뿌리기 직전에 통과하는 <b>단 하나의 창구</b>
/// (다국어 Phase 3, 2026-08-25).
///
/// <para><b>왜 에셋을 안 고치고 여기서 갈아끼우나</b>:
/// `GameDataAsset`(무기 65·로봇 13·몬스터 6·방어구 2), `PartsCatalog`(파츠 156),
/// `ShopCatalog`(디스크 21+설명 21)에 이미 한글 이름이 들어 있고, 이 에셋들은
/// `Assets/Scripts/Editor/`의 생성기들이 만들어낸다. 에셋에 언어 칸을 새로 파면
/// 생성기·에셋·인스펙터를 다 같이 고쳐야 하고, 언어를 추가할 때마다 또 그래야 한다.
/// 대신 <b>ID로 만든 키</b>(<c>weapon.300101.name</c>)를 번역 파일에서 찾아보고,
/// 없으면 <b>에셋에 있는 원래 한글 문자열을 그대로</b> 쓴다. 그래서:</para>
/// <list type="bullet">
/// <item>한국어에서는 번역 파일에 키가 하나도 없어도 지금과 100% 똑같이 동작한다.</item>
/// <item>언어를 추가할 때 에셋·생성기를 전혀 건드리지 않는다(JSON에 키만 채우면 된다).</item>
/// <item>번역이 일부만 된 상태로도 안전하다 - 빠진 항목만 한글로 남는다.</item>
/// </list>
///
/// <para><b>키 규약</b>: <c>weapon.&lt;id&gt;.name</c> / <c>robot.&lt;id&gt;.name</c> /
/// <c>monster.&lt;id&gt;.name</c> / <c>armor.&lt;id&gt;.name</c> / <c>part.&lt;id&gt;.name</c> /
/// <c>disc.&lt;id&gt;.name</c> / <c>disc.&lt;id&gt;.effect</c></para>
/// </summary>
public static class DataNames
{
    /// <summary>
    /// 번역 키가 있으면 번역문을, 없으면 에셋의 원문을 돌려준다.
    /// <para><see cref="Loc.T(string)"/>를 그냥 부르면 키가 없을 때 <b>키 문자열</b>이 나오는데,
    /// 여기서는 그러면 안 된다 - 에셋에 멀쩡한 한글 이름이 있으니 그게 훨씬 나은 폴백이다.
    /// 그래서 <see cref="Loc.Has(string)"/>로 먼저 확인한다.</para>
    /// </summary>
    private static string Resolve(string key, string assetText)
    {
        if (Loc.Has(key)) return Loc.T(key);
        return assetText;
    }

    public static string Weapon(this WeaponData weapon)
        => Resolve($"weapon.{weapon.weapon_id}.name", weapon.weapon_name);

    public static string Robot(this RobotData robot)
        => Resolve($"robot.{robot.robot_id}.name", robot.robot_name);

    public static string Monster(this MonsterData monster)
        => Resolve($"monster.{monster.monster_id}.name", monster.monster_name);

    public static string Armor(this AmorData armor)
        => Resolve($"armor.{armor.amor_id}.name", armor.amor_name);

    // PartData/DiscData 는 class 가 아니라 struct 라 null 검사를 하지 않는다(할 수도 없다).
    public static string Part(this PartData part)
        => Resolve($"part.{part.partId}.name", part.partName);

    public static string Disc(this DiscData disc)
        => Resolve($"disc.{disc.discId}.name", disc.discName);

    /// <summary>디스크의 효과 설명문(상점·정비·도감이 그대로 뿌린다).</summary>
    public static string DiscEffect(this DiscData disc)
        => Resolve($"disc.{disc.discId}.effect", disc.effectDescription);

    public static string Accessory(this AccessoryData accessory)
        => Resolve($"accessory.{accessory.accessoryId}.name", accessory.accessoryName);

    /// <summary>도감의 해금 조건 문구. 키는 항목 ID로 만든다.</summary>
    public static string UnlockCondition(this UnlockEntry entry)
        => Resolve($"unlock.{entry.itemId}.condition", entry.conditionText);

    /// <summary>데이터에서 이름을 못 찾았을 때 도감이 쓰는 예비 이름.</summary>
    public static string UnlockFallbackName(this UnlockEntry entry)
        => Resolve($"unlock.{entry.itemId}.name", entry.fallbackName);

    /// <summary>
    /// AI 코어 업그레이드 카드의 옵션 이름.
    /// <para>Option 에는 고유 ID 필드가 없어서 <see cref="StatType"/>을 키로 쓴다 -
    /// 에셋의 9개 옵션이 서로 다른 statType 을 하나씩 갖고 있어 실질적인 ID 역할을 한다.
    /// 나중에 같은 statType 을 쓰는 옵션이 추가되면 그때는 Option 에 id 필드를 넣어야 한다.</para>
    /// </summary>
    public static string DisplayName(this AiCoreUpgradePool.Option option)
        => Resolve($"aicore.option.{(int)option.statType}.name", option.displayName);
}
