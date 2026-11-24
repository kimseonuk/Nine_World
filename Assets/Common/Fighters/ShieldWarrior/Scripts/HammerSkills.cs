// The hammer's (묠니르) skills now live in the one 번개 망치 스킬트리 (StormSkills.Tree), shared with the plain
// fighter's lightning kit: each of 묠니르's skills and upgrades is unlocked by the node of the same kind there.
public static class HammerSkills
{
    // The skills
    public const string Dodge = StormSkills.Dash;       // 회피 [Space]      ← 대시
    public const string Spin = StormSkills.RSkill;      // 회전 베기 [우클릭] ← 휠윈드
    public const string Thunder = StormSkills.ESkill;   // 천둥 강타 [E]     ← 번개 방출
    public const string Whirl = StormSkills.RSkill;     // 휠윈드 [R]
    public const string Throw = StormSkills.QSkill;     // 묠니르 [Q]        ← 망치 투척

    // Upgrades
    public const string Storm = StormSkills.EChain;     // 뇌전 확산    ← 연쇄 방전
    public const string Wrath = StormSkills.EMaster;    // 토르의 분노  ← 천둥의 심판
    public const string Cyclone = StormSkills.RShock;   // 회전 연격    ← 감전
    public const string Tempest = StormSkills.RLong;    // 폭풍 회오리  ← 폭풍의 눈
    public const string Homing = StormSkills.QBounce;   // 귀환의 번개  ← 튕기는 망치
    public const string Blink = StormSkills.Blink;      // 번개 걸음
    public const string Tempo = StormSkills.Tempo;      // 천둥의 리듬

    public static SkillTree Tree => StormSkills.Tree;

    public static bool Has(string id) => Tree.Has(id);

    // 천둥의 리듬: the skills come round sooner
    public static float CooldownScale => Has(Tempo) ? 0.8f : 1f;
}
