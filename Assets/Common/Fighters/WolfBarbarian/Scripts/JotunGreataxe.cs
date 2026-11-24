using NineWorlds.Jotunheim;

// 양손도끼: 버티고 밀어붙이는 전사 (어깨 돌진 · 회전베기 · 전사의 포효 · 지진분쇄)
public class JotunGreataxe : JotunAxes
{
    public override CharacterProfile.WeaponKind Kind => CharacterProfile.WeaponKind.Greataxe;
    protected override System.Type SkillType => typeof(JOT_GreataxeSkills);
    public override SkillTree Tree => JOT_AxeTrees.Greataxe;
    public override string TreeName => "양손도끼 기술";
    public override string Hint => "[클릭] 공격 [꾹] 차징 [우클릭] 어깨 돌진 [Q] 회전베기 [E] 전사의 포효 [R] 지진분쇄 [Space] 늑대 발걸음";
}
