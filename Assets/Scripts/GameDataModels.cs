using System;

[Serializable]
public struct MonsterData
{
    public int monster_id;
    public string monster_name;
    public int monster_hp;
    public int monster_atk;
    public int monster_def;
    public float monster_speed;
    public float monster_range;
    public int monster_type;
    public float monster_atsp;
}

[Serializable]
public struct RobotData
{
    public int robot_id;
    public string robot_name;
    public int robot_hp;
    public int robot_atk;
    public int robot_def;
    public float robot_cc;
    public float robot_cd;
    public float robot_speed;
    public float robot_capacity;
    public float robot_reload;
    public float robot_avoid;
    public float robot_luck;
    public float robot_mess;
}

[Serializable]
public struct WeaponData
{
    public int weapon_id;
    public string weapon_name;
    public int weapon_atk;
    public float weapon_atsp;
    public int weapon_range;
    public float weapon_atsize;
    public float weapon_aim;
    public float weapon_rebound;
    public int weapon_projectiles;
    public int weapon_capacity;
    public int weapon_reload;
}

[Serializable]
public struct AmorData
{
    public int amor_id;
    public string amor_name;
    public int amor_hp;
    public int amor_def;
    public float amor_speed;
    public float amor_avoid;
}

[Serializable]
public struct DropEntry
{
    public int monster_id;
    public int item_id;
    public float item_drop;
}
