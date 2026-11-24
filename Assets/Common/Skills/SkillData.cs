using System;
using System.Collections.Generic;
using UnityEngine;

// ── The skill system's data (기획서 3·4·5장) ──
// 32 skills any weapon can carry on Q / E / R, each with an element and function tags for synergies; a skill's level
// (일반 강화, the upgrade room) and at most one boss special (보스 특수 강화) that changes how it works.

public enum SkillElement { Fire, Ice, Lightning, Wind, Earth, Holy, Shadow, Blood }

[Flags]
public enum SkillTag
{
    None = 0,
    Projectile = 1 << 0,   // 투사체
    Explosion = 1 << 1,    // 폭발
    Smash = 1 << 2,        // 강타
    Chain = 1 << 3,        // 연쇄
    Dash = 1 << 4,         // 돌진
    Field = 1 << 5,        // 장판
    Buff = 1 << 6,         // 강화
    Orbit = 1 << 7,        // 회전
    Pierce = 1 << 8,       // 관통
    Freeze = 1 << 9,       // 빙결
    Burn = 1 << 10,        // 화상
}

// How a skill is cast (the code that runs it lives in SkillCast)
public enum SkillKind
{
    Projectile,   // flies at the cursor, hits (or pierces) and may burst
    Strike,       // lands where the cursor is after a short delay (pillar, meteor, bolt)
    Nova,         // a ring bursting out round the player
    Smash,        // a blow on the ground just in front
    Line,         // a straight line in front, all at once (beam, spikes, slash wave)
    Field,        // a lasting zone that hurts and hinders
    Dash,         // the player rushes forward through enemies
    Chain,        // leaps from enemy to enemy
    Orbit,        // orbs wheeling round the player for a while
    Buff,         // a boon on the player for a while
}

// 보스 특수 강화: changes how a skill works (one per skill)
public enum SkillSpecial
{
    None,
    Barrage,      // 연속 섬광: weaker, but up to 3 charges
    OneStrike,    // 일섬: one huge blow, few uses per fight
    ChainPierce,  // 연속 관통: passes through every enemy / leaps again
    Aftershock,   // 충격파: a second blast where it lands
    Wide,         // 광역: much larger, but slower
    Split,        // 분열: more of it, spread out
    Echo,         // 메아리: casts itself again a moment later, weaker
    Swift,        // 신속: much shorter cooldown, a little weaker
    Overload,     // 과부하: much stronger, longer cooldown
    Lasting,      // 지속: fields, orbits, boons and lingering winds last far longer
}

public class SkillDef
{
    public string id, name, desc;
    public SkillElement element;
    public SkillTag tags;
    public SkillKind kind;
    public float cooldown = 6f;
    public float power = 4f;        // damage = player damage × power × level bonus
    public float radius = 2.5f;
    public float range = 12f;
    public int count = 1;           // projectiles, bolts, leaps, orbs...
    public float duration = 0f;     // fields, buffs, orbits
    public string fx = "sk_burst";  // its main effect sheet
    public int icon;                // index into the 32-icon atlas (sk_icons1 then sk_icons2)
    public Texture2D iconTex;       // a weapon's own icon (weapon orbs)
    public string iconPath;         // its own painted icon (Resources), used instead of the atlas when present
    private bool iconTried;
    public string weaponSkill;      // non-null: a weapon's right-click skill carried as an orb (its name on the bar)

    public bool Has(SkillTag t) => (tags & t) != 0;
    public bool IsWeapon => weaponSkill != null;
    public Texture2D Icon
    {
        get
        {
            if (iconTex == null && !iconTried && !string.IsNullOrEmpty(iconPath)) { iconTried = true; iconTex = Resources.Load<Texture2D>(iconPath); }
            return iconTex != null ? iconTex : IsWeapon ? null : SkillCatalog.Icon(icon);
        }
    }
    public Color Color => IsWeapon ? new Color(0.85f, 0.85f, 0.9f) : SkillCatalog.ElementColor(element);
}

// One skill as the player holds it: its level (일반 강화) and its special (보스 특수 강화)
public class SkillInstance
{
    public SkillDef def;
    public int level;                 // +0 .. +MaxLevel
    public SkillSpecial special;
    public float cooldownLeft;
    public int charges = 1;           // for 연속 섬광
    public int usesLeft = -1;         // for 일섬 (-1 = no limit)
    public bool echo;                 // a 메아리 recast (weaker)
    public List<SkillSpecial> offers; // 보스 특수 강화: the two directions on offer, kept until chosen or rerolled

    public SkillInstance(SkillDef d) { def = d; }

    public const int MaxLevel = 10;
    public float LevelDamage => 1f + 0.15f * level;
    public float LevelCooldown => Mathf.Max(0.5f, 1f - 0.04f * level);
    public string Title => level > 0 ? $"{def.name} +{level}" : def.name;
    public int MaxCharges => special == SkillSpecial.Barrage ? 3 : 1;
    public float Cooldown => def.cooldown * LevelCooldown * (special switch
    {
        SkillSpecial.Wide => 1.25f, SkillSpecial.OneStrike => 1.8f, SkillSpecial.Barrage => 0.55f,
        SkillSpecial.Swift => 0.6f, SkillSpecial.Overload => 1.3f, _ => 1f,
    });
    public float SpecialDamage => special switch
    {
        SkillSpecial.Barrage => 0.6f, SkillSpecial.OneStrike => 3f, SkillSpecial.Swift => 0.85f, SkillSpecial.Overload => 1.6f, _ => 1f,
    };
    public float Duration => def.duration * (special == SkillSpecial.Lasting ? 1.6f : 1f);
    public bool CanSpecial => !def.IsWeapon && special == SkillSpecial.None;
    public bool CanUpgrade => !def.IsWeapon && level < MaxLevel;
}

public static class SkillCatalog
{
    public static readonly string[] ElementNames = { "화염", "빙결", "번개", "바람", "대지", "신성", "암흑", "출혈" };

    public static Color ElementColor(SkillElement e) => e switch
    {
        SkillElement.Fire => new Color(1f, 0.45f, 0.15f),
        SkillElement.Ice => new Color(0.55f, 0.85f, 1f),
        SkillElement.Lightning => new Color(0.7f, 0.75f, 1f),
        SkillElement.Wind => new Color(0.6f, 1f, 0.75f),
        SkillElement.Earth => new Color(0.85f, 0.65f, 0.4f),
        SkillElement.Holy => new Color(1f, 0.88f, 0.5f),
        SkillElement.Shadow => new Color(0.65f, 0.4f, 1f),
        _ => new Color(0.9f, 0.08f, 0.16f),   // 출혈: deep blood red
    };

    public static readonly string[] TagNames = { "투사체", "폭발", "강타", "연쇄", "돌진", "장판", "강화", "회전", "관통", "빙결", "화상" };

    public static string TagText(SkillTag tags)
    {
        var parts = new List<string>();
        for (int i = 0; i < TagNames.Length; i++)
            if (((int)tags & (1 << i)) != 0)
                parts.Add(TagNames[i]);
        return string.Join(" · ", parts);
    }

    public static readonly List<SkillDef> All = new List<SkillDef>
    {
        // ── 화염 ──
        new SkillDef { id = "fireball", name = "화염구", element = SkillElement.Fire, kind = SkillKind.Projectile, tags = SkillTag.Projectile | SkillTag.Explosion | SkillTag.Burn,
            cooldown = 4f, power = 4f, radius = 2.4f, range = 16f, fx = "sk_orb", icon = 0, desc = "날아가 부딪힌 곳에서 폭발하고 불태웁니다" },
        new SkillDef { id = "flamepillar", name = "불기둥", element = SkillElement.Fire, kind = SkillKind.Strike, tags = SkillTag.Explosion | SkillTag.Burn,
            cooldown = 6f, power = 6f, radius = 2.6f, range = 12f, fx = "sk_pillar", icon = 1, desc = "커서 위치에 불기둥이 솟아 폭발합니다" },
        new SkillDef { id = "firefield", name = "화염 장판", element = SkillElement.Fire, kind = SkillKind.Field, tags = SkillTag.Field | SkillTag.Burn,
            cooldown = 9f, power = 1.2f, radius = 3.4f, range = 10f, duration = 4f, fx = "sk_field", icon = 2, desc = "불타는 바닥을 깔아 계속 피해를 줍니다" },
        new SkillDef { id = "meteor", name = "유성 낙하", element = SkillElement.Fire, kind = SkillKind.Strike, tags = SkillTag.Explosion | SkillTag.Smash | SkillTag.Burn,
            cooldown = 12f, power = 12f, radius = 4f, range = 14f, fx = "sk_meteor", icon = 3, desc = "잠시 뒤 거대한 유성이 떨어져 넓게 폭발합니다" },
        // ── 빙결 ──
        new SkillDef { id = "frostspear", name = "서리 창", element = SkillElement.Ice, kind = SkillKind.Projectile, tags = SkillTag.Projectile | SkillTag.Pierce | SkillTag.Freeze,
            cooldown = 4f, power = 4f, radius = 1f, range = 18f, fx = "sk_shard", icon = 4, desc = "적을 꿰뚫고 지나가며 얼립니다" },
        new SkillDef { id = "icewave", name = "얼음 파동", element = SkillElement.Ice, kind = SkillKind.Nova, tags = SkillTag.Freeze,
            cooldown = 7f, power = 3.5f, radius = 5f, fx = "sk_nova", icon = 5, desc = "주위로 냉기를 터뜨려 적을 얼립니다" },
        new SkillDef { id = "frostsmash", name = "빙결 강타", element = SkillElement.Ice, kind = SkillKind.Smash, tags = SkillTag.Smash | SkillTag.Freeze,
            cooldown = 5f, power = 6f, radius = 3f, fx = "sk_spike", icon = 6, desc = "앞을 내려쳐 얼음 가시를 솟게 합니다" },
        new SkillDef { id = "blizzard", name = "눈보라", element = SkillElement.Ice, kind = SkillKind.Field, tags = SkillTag.Field | SkillTag.Freeze,
            cooldown = 11f, power = 1f, radius = 4.2f, range = 12f, duration = 5f, fx = "sk_field", icon = 7, desc = "눈보라가 몰아쳐 적을 느리게 하고 얼립니다" },
        // ── 번개 ──
        new SkillDef { id = "chainlightning", name = "연쇄 번개", element = SkillElement.Lightning, kind = SkillKind.Chain, tags = SkillTag.Chain,
            cooldown = 5f, power = 3.5f, range = 12f, count = 4, fx = "sk_bolt", icon = 8, desc = "번개가 적에서 적으로 튕겨 갑니다" },
        new SkillDef { id = "thunderstrike", name = "낙뢰", element = SkillElement.Lightning, kind = SkillKind.Strike, tags = SkillTag.Smash,
            cooldown = 7f, power = 5f, radius = 2f, range = 12f, count = 3, fx = "sk_bolt", icon = 9, desc = "커서 주변에 벼락이 여러 번 내리칩니다" },
        new SkillDef { id = "lightningdash", name = "번개 질주", element = SkillElement.Lightning, kind = SkillKind.Dash, tags = SkillTag.Dash | SkillTag.Chain,
            cooldown = 6f, power = 4f, radius = 1.6f, range = 8f, fx = "sk_bolt", icon = 10, desc = "번개가 되어 돌진하며 지나간 적을 감전시킵니다" },
        new SkillDef { id = "thunderorbs", name = "뇌전 구체", element = SkillElement.Lightning, kind = SkillKind.Orbit, tags = SkillTag.Orbit | SkillTag.Chain,
            cooldown = 12f, power = 2f, radius = 2.4f, count = 3, duration = 6f, fx = "sk_orb", icon = 11, desc = "번개 구체가 몸 주위를 돌며 닿는 적을 감전시킵니다" },
        // ── 바람 ──
        new SkillDef { id = "tornado", name = "회오리", element = SkillElement.Wind, kind = SkillKind.Projectile, tags = SkillTag.Projectile | SkillTag.Pierce,
            cooldown = 6f, power = 1.6f, radius = 1.8f, range = 14f, duration = 2.5f, fx = "sk_tornado", icon = 12, desc = "느리게 나아가는 회오리가 닿는 적을 계속 벱니다" },
        new SkillDef { id = "galeslash", name = "질풍 베기", element = SkillElement.Wind, kind = SkillKind.Line, tags = SkillTag.Pierce,
            cooldown = 4f, power = 4.5f, radius = 1.4f, range = 11f, fx = "sk_blade", icon = 13, desc = "앞으로 바람의 칼날을 날려 일직선을 벱니다" },
        new SkillDef { id = "gust", name = "돌풍", element = SkillElement.Wind, kind = SkillKind.Nova, tags = SkillTag.None,
            cooldown = 6f, power = 2.5f, radius = 5.5f, fx = "sk_nova", icon = 14, desc = "주위의 적을 멀리 날려 보냅니다" },
        new SkillDef { id = "windblessing", name = "바람의 가호", element = SkillElement.Wind, kind = SkillKind.Buff, tags = SkillTag.Buff,
            cooldown = 16f, power = 0.35f, duration = 6f, fx = "sk_aura", icon = 15, desc = "잠시 동안 이동이 빨라지고 공격이 강해집니다" },
        // ── 대지 ──
        new SkillDef { id = "earthsmash", name = "대지 강타", element = SkillElement.Earth, kind = SkillKind.Smash, tags = SkillTag.Smash,
            cooldown = 5f, power = 6.5f, radius = 3.4f, fx = "sk_burst", icon = 16, desc = "땅을 내려쳐 주위의 적을 쓰러뜨립니다" },
        new SkillDef { id = "rockspikes", name = "암석 가시", element = SkillElement.Earth, kind = SkillKind.Line, tags = SkillTag.Smash | SkillTag.Pierce,
            cooldown = 6f, power = 5f, radius = 1.6f, range = 10f, fx = "sk_spike", icon = 17, desc = "앞으로 바위 가시가 줄지어 솟아오릅니다" },
        new SkillDef { id = "earthquake", name = "지진", element = SkillElement.Earth, kind = SkillKind.Field, tags = SkillTag.Field | SkillTag.Smash,
            cooldown = 12f, power = 1.6f, radius = 5f, duration = 3f, fx = "sk_burst", icon = 18, desc = "주위 땅이 흔들려 계속 피해를 주고 적을 비틀거리게 합니다" },
        new SkillDef { id = "stoneshield", name = "바위 방패", element = SkillElement.Earth, kind = SkillKind.Buff, tags = SkillTag.Buff,
            cooldown = 18f, power = 0.5f, duration = 6f, fx = "sk_shield", icon = 19, desc = "잠시 동안 받는 피해가 크게 줄어듭니다" },
        // ── 신성 ──
        new SkillDef { id = "holyspear", name = "성스러운 창", element = SkillElement.Holy, kind = SkillKind.Line, tags = SkillTag.Pierce,
            cooldown = 5f, power = 5.5f, radius = 1.2f, range = 14f, fx = "sk_beam", icon = 20, desc = "빛의 창이 일직선을 꿰뚫습니다" },
        new SkillDef { id = "holyburst", name = "신성 폭발", element = SkillElement.Holy, kind = SkillKind.Nova, tags = SkillTag.Explosion,
            cooldown = 9f, power = 4f, radius = 4.5f, fx = "sk_nova", icon = 21, desc = "빛이 터져 주위 적을 태우고 체력을 조금 회복합니다" },
        new SkillDef { id = "judgment", name = "빛의 심판", element = SkillElement.Holy, kind = SkillKind.Strike, tags = SkillTag.Smash,
            cooldown = 10f, power = 4.5f, radius = 1.8f, range = 12f, count = 5, fx = "sk_pillar", icon = 22, desc = "가까운 적들 위로 빛기둥이 내리꽂힙니다" },
        new SkillDef { id = "blessing", name = "축복", element = SkillElement.Holy, kind = SkillKind.Buff, tags = SkillTag.Buff,
            cooldown = 18f, power = 0.5f, duration = 7f, fx = "sk_aura", icon = 23, desc = "잠시 동안 모든 피해가 늘어나고 체력이 차오릅니다" },
        // ── 암흑 ──
        new SkillDef { id = "shadowblades", name = "그림자 칼날", element = SkillElement.Shadow, kind = SkillKind.Projectile, tags = SkillTag.Projectile,
            cooldown = 4f, power = 2.6f, radius = 1f, range = 14f, count = 5, fx = "sk_blade", icon = 24, desc = "부채꼴로 칼날 다섯 개를 던집니다" },
        new SkillDef { id = "darkorbs", name = "암흑 구체", element = SkillElement.Shadow, kind = SkillKind.Orbit, tags = SkillTag.Orbit,
            cooldown = 12f, power = 2.4f, radius = 2.2f, count = 4, duration = 6f, fx = "sk_orb", icon = 25, desc = "어둠의 구체가 몸 주위를 돌며 적을 벱니다" },
        new SkillDef { id = "shadowstep", name = "그림자 도약", element = SkillElement.Shadow, kind = SkillKind.Dash, tags = SkillTag.Dash | SkillTag.Explosion,
            cooldown = 6f, power = 5f, radius = 2.6f, range = 9f, fx = "sk_burst", icon = 26, desc = "그림자로 도약해 내려선 곳을 터뜨립니다" },
        new SkillDef { id = "voidfield", name = "공허 장판", element = SkillElement.Shadow, kind = SkillKind.Field, tags = SkillTag.Field,
            cooldown = 12f, power = 1.2f, radius = 4f, range = 12f, duration = 4f, fx = "sk_field", icon = 27, desc = "공허가 적을 중심으로 끌어당기며 갉아먹습니다" },
        // ── 출혈 (no lifesteal, no boon: wounds that keep bleeding — stacks of 출혈, see EnemyStatus.Bleed) ──
        new SkillDef { id = "rend", name = "출혈 베기", element = SkillElement.Blood, kind = SkillKind.Smash, tags = SkillTag.Smash,
            cooldown = 5f, power = 4.5f, radius = 3.2f, fx = "sk_blade", icon = 28, iconPath = "UI/BleedIcons/ic_rend",
            desc = "앞을 크게 베어 출혈을 3중첩 새깁니다" },
        new SkillDef { id = "bloodneedle", name = "피바늘", element = SkillElement.Blood, kind = SkillKind.Projectile, tags = SkillTag.Projectile | SkillTag.Pierce,
            cooldown = 4f, power = 2f, radius = 1f, range = 16f, count = 3, fx = "sk_shard", icon = 29, iconPath = "UI/BleedIcons/ic_needle",
            desc = "적을 꿰뚫는 바늘 셋을 부채꼴로 던져, 맞은 적마다 출혈을 2중첩 새깁니다" },
        new SkillDef { id = "rupture", name = "선혈 파열", element = SkillElement.Blood, kind = SkillKind.Nova, tags = SkillTag.Explosion,
            cooldown = 9f, power = 2.5f, radius = 5f, fx = "sk_burst", icon = 30, iconPath = "UI/BleedIcons/ic_rupture",
            desc = "주위 적의 상처를 터뜨려, 남은 출혈 피해를 한 번에 주고 중첩을 비웁니다" },
        new SkillDef { id = "bloodpool", name = "피웅덩이", element = SkillElement.Blood, kind = SkillKind.Field, tags = SkillTag.Field,
            cooldown = 11f, power = 0.6f, radius = 3.8f, range = 12f, duration = 5f, fx = "sk_field", icon = 31, iconPath = "UI/BleedIcons/ic_pool",
            desc = "피웅덩이를 깔아 위에 선 적에게 출혈을 계속 새기고 느리게 합니다" },
    };

    public static SkillDef Get(string id) => All.Find(s => s.id == id);

    // ── A weapon's right-click skill as an orb (one def per skill, made the first time it's seen) ──
    private static readonly Dictionary<string, SkillDef> weaponDefs = new Dictionary<string, SkillDef>();
    // key: the fighter and its skill (two fighters can share a skill's name), name: what the bar calls it
    public static SkillDef WeaponOrb(string key, string skillName, Texture2D icon)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (!weaponDefs.TryGetValue(key, out SkillDef d))
            weaponDefs[key] = d = new SkillDef
            {
                id = "w:" + key, name = skillName, weaponSkill = key, kind = SkillKind.Buff, tags = SkillTag.None,
                element = SkillElement.Earth, cooldown = 0f, iconTex = icon, desc = "무기 고유 스킬 (원래 우클릭) — 이 무기를 들었을 때만 쓸 수 있습니다",
            };
        else if (d.iconTex == null) d.iconTex = icon;
        return d;
    }

    public static SkillDef Random(ICollection<string> exclude = null)
    {
        var pool = All.FindAll(s => exclude == null || !exclude.Contains(s.id));
        if (pool.Count == 0) pool = All;
        return pool[UnityEngine.Random.Range(0, pool.Count)];
    }

    // ── Icons: two VARCO sheets of 16 (4×4), cut once into 32 textures ──
    private static Texture2D[] icons;

    public static Texture2D Icon(int index)
    {
        if (icons == null)
        {
            icons = new Texture2D[32];
            for (int sheet = 0; sheet < 2; sheet++)
            {
                Texture2D atlas = Resources.Load<Texture2D>("UI/sk_icons" + (sheet + 1));
                if (atlas == null) continue;
                int cw = atlas.width / 4, ch = atlas.height / 4;
                Color[] all = null;
                try { all = atlas.GetPixels(); } catch { }
                if (all == null) continue;
                for (int i = 0; i < 16; i++)
                {
                    int cx = i % 4, cy = 3 - i / 4;   // (pixel rows run bottom-up)
                    var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    var px = new Color[cw * ch];
                    for (int y = 0; y < ch; y++)
                        Array.Copy(all, (cy * ch + y) * atlas.width + cx * cw, px, y * cw, cw);
                    tex.SetPixels(px);
                    tex.Apply();
                    icons[sheet * 16 + i] = tex;
                }
            }
        }
        return index >= 0 && index < icons.Length ? icons[index] : null;
    }

    // ── 보스 특수 강화: what each special does, and which skills it suits ──
    public static string SpecialName(SkillSpecial s) => s switch
    {
        SkillSpecial.Barrage => "연속 섬광",
        SkillSpecial.OneStrike => "일섬",
        SkillSpecial.ChainPierce => "연속 관통",
        SkillSpecial.Aftershock => "충격파",
        SkillSpecial.Wide => "광역",
        SkillSpecial.Split => "분열",
        SkillSpecial.Echo => "메아리",
        SkillSpecial.Swift => "신속",
        SkillSpecial.Overload => "과부하",
        SkillSpecial.Lasting => "지속",
        _ => "",
    };

    public static string SpecialDesc(SkillSpecial s) => s switch
    {
        SkillSpecial.Barrage => "피해가 40% 줄지만 최대 3회까지 충전해 연달아 쓸 수 있습니다",
        SkillSpecial.OneStrike => "전투마다 3번만 쓸 수 있지만 피해가 3배인 한 방이 됩니다",
        SkillSpecial.ChainPierce => "모든 적을 꿰뚫고, 연쇄는 두 배로 더 튕겨 갑니다",
        SkillSpecial.Aftershock => "맞힌 곳에서 충격파가 한 번 더 터집니다",
        SkillSpecial.Wide => "범위가 60% 넓어지지만 재사용이 25% 느려집니다",
        SkillSpecial.Split => "두 갈래가 더 생겨 부채꼴로 퍼집니다",
        SkillSpecial.Echo => "0.5초 뒤 같은 스킬이 60% 위력으로 한 번 더 발동합니다",
        SkillSpecial.Swift => "재사용 대기가 40% 짧아지고 피해는 15% 줄어듭니다",
        SkillSpecial.Overload => "피해가 60% 늘어나지만 재사용 대기가 30% 길어집니다",
        SkillSpecial.Lasting => "장판·회전·강화·회오리의 지속 시간이 60% 늘어납니다",
        _ => "",
    };

    public static bool Fits(SkillDef d, SkillSpecial s) => s switch
    {
        SkillSpecial.Barrage => d.kind != SkillKind.Buff && d.kind != SkillKind.Field && d.kind != SkillKind.Orbit,
        SkillSpecial.OneStrike => d.kind != SkillKind.Buff && d.kind != SkillKind.Field && d.kind != SkillKind.Orbit,
        SkillSpecial.ChainPierce => d.kind == SkillKind.Projectile || d.kind == SkillKind.Chain || d.kind == SkillKind.Line || d.kind == SkillKind.Dash,
        SkillSpecial.Aftershock => d.kind == SkillKind.Projectile || d.kind == SkillKind.Strike || d.kind == SkillKind.Smash || d.kind == SkillKind.Line || d.kind == SkillKind.Dash,
        SkillSpecial.Wide => d.kind == SkillKind.Nova || d.kind == SkillKind.Smash || d.kind == SkillKind.Field || d.kind == SkillKind.Strike || d.kind == SkillKind.Orbit,
        SkillSpecial.Split => d.kind == SkillKind.Projectile || d.kind == SkillKind.Strike || d.kind == SkillKind.Orbit,
        SkillSpecial.Echo => d.kind != SkillKind.Buff && d.kind != SkillKind.Orbit,
        SkillSpecial.Swift => true,
        SkillSpecial.Overload => d.kind != SkillKind.Buff,
        SkillSpecial.Lasting => d.duration > 0f,
        _ => false,
    };

    // ── 스킬 시너지: element + function combinations among the equipped skills ──
    public class Synergy
    {
        public string name, desc;
        public Func<IList<SkillInstance>, bool> active;
    }

    private static bool HasElement(IList<SkillInstance> s, SkillElement e) { foreach (var i in s) if (i != null && !i.def.IsWeapon && i.def.element == e) return true; return false; }
    private static bool HasTag(IList<SkillInstance> s, SkillTag t) { foreach (var i in s) if (i != null && !i.def.IsWeapon && i.def.Has(t)) return true; return false; }
    private static int CountElement(IList<SkillInstance> s, SkillElement e) { int n = 0; foreach (var i in s) if (i != null && !i.def.IsWeapon && i.def.element == e) n++; return n; }

    public static readonly List<Synergy> Synergies = new List<Synergy>
    {
        new Synergy { name = "화염 + 폭발", desc = "폭발 피해 +35%", active = s => HasElement(s, SkillElement.Fire) && HasTag(s, SkillTag.Explosion) },
        new Synergy { name = "빙결 + 강타", desc = "얼어붙은 적에게 피해 +50%", active = s => HasElement(s, SkillElement.Ice) && HasTag(s, SkillTag.Smash) },
        new Synergy { name = "번개 + 연쇄", desc = "번개가 맞힌 적 근처로 한 번 더 전이", active = s => HasElement(s, SkillElement.Lightning) && HasTag(s, SkillTag.Chain) },
        new Synergy { name = "바람 + 투사체", desc = "투사체가 더 빠르고 한 명 더 꿰뚫음", active = s => HasElement(s, SkillElement.Wind) && HasTag(s, SkillTag.Projectile) },
        new Synergy { name = "대지 + 강타", desc = "강타가 적을 잠시 쓰러뜨림", active = s => HasElement(s, SkillElement.Earth) && HasTag(s, SkillTag.Smash) },
        new Synergy { name = "신성 + 강화", desc = "강화 스킬이 체력을 회복", active = s => HasElement(s, SkillElement.Holy) && HasTag(s, SkillTag.Buff) },
        new Synergy { name = "암흑 + 돌진", desc = "돌진한 자리에 그림자 폭발", active = s => HasElement(s, SkillElement.Shadow) && HasTag(s, SkillTag.Dash) },
        new Synergy { name = "선혈의 낙인", desc = "출혈 스킬 2개: 출혈 중인 적에게 주는 피해 +15%, 출혈 피해 +50%", active = s => CountElement(s, SkillElement.Blood) >= 2 },
        new Synergy { name = "화염 + 장판", desc = "장판 지속 +2초", active = s => HasElement(s, SkillElement.Fire) && HasTag(s, SkillTag.Field) },
        new Synergy { name = "원소 조화", desc = "서로 다른 원소 3개 이상: 보조스킬 피해 +15%", active = s => { var seen = new HashSet<SkillElement>(); foreach (var i in s) if (i != null && !i.def.IsWeapon) seen.Add(i.def.element); return seen.Count >= 3; } },
    };
}
