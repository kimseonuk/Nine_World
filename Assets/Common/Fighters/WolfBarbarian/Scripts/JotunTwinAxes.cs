using NineWorlds.Jotunheim;

// 사슬 쌍도끼: 파고들어 출혈을 쌓는 사냥꾼 (그림자 사냥 · 쌍도 투척 · 사냥꾼의 도약 · 환영 사형집행)
public class JotunTwinAxes : JotunAxes
{
    public override CharacterProfile.WeaponKind Kind => CharacterProfile.WeaponKind.TwinAxes;
    protected override System.Type SkillType => typeof(JOT_TwinAxeSkills);
    public override SkillTree Tree => JOT_AxeTrees.TwinAxes;
    public override string TreeName => "쌍도끼 기술";
    public override string Hint => "[클릭] 공격 [꾹] 차징 [우클릭] 그림자 사냥 [Q] 쌍도 투척 [E] 사냥꾼의 도약 [R] 환영 사형집행 [Space] 그림자 회피";
}
