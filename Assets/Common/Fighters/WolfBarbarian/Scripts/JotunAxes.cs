using System.Collections;
using System.Collections.Generic;
using NineWorlds.Jotunheim;
using UnityEngine;

// Jotunheim's wolf-tribe barbarian (늑대 부족 야만전사) and his two weapons, as the Jotunheim project built them. The
// click is that project's attack (its PlayerMiner: a tap swings, holding raises the axe through two charge stages and
// slams, with a shockwave at the higher stages) and its charged double slash (JOT_ChargeDoubleSlash: a second cut and
// a bonus on whatever both cuts caught). The weapon's four skills on right click, Q, E and R are the project's own
// skill components (JOT_GreataxeSkills, JOT_TwinAxeSkills), added while the weapon is in hand, with their skill bar.
public abstract class JotunAxes : ArmoryWeapon
{
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
    private static readonly int ChargeHash = Animator.StringToHash("Charge");
    private static readonly int ChargeSpeedHash = Animator.StringToHash("ChargeSpeed");

    // (the Jotunheim scenes' PlayerMiner)
    private const float DigRange = 3f;
    private const float FrontArc = 140f;
    private const float ChargeDelay = 0.22f;
    private const float StageOneTime = 0.8f, StageTwoTime = 1.7f;
    private const float RaiseSpeed = 0.6f;
    private static readonly float[] StageDamage = { 1.3f, 2f, 3.2f };
    private const float ShockRadius = 3.5f, ShockDamage = 1f, ShockRadiusFull = 5f, ShockDamageFull = 1.8f;
    private const float KnockdownSeconds = 2f;
    // (JOT_ChargeDoubleSlash)
    private const float SecondCutPoint = 0.84f;
    private static readonly float[] SecondCutDamage = { 1f, 1.5f, 2.4f };
    private static readonly float[] BothCutsBonus = { 0.3f, 0.6f, 1f };

    private static readonly Color StageOneColor = new Color(1f, 0.6f, 0.15f);
    private static readonly Color StageTwoColor = new Color(0.55f, 0.85f, 1f);
    private static readonly Color CutColor = new Color(1f, 0.55f, 0.2f);
    private static readonly Color BonusColor = new Color(1f, 0.3f, 0.25f);

    private bool pressing, slamRequested, secondCutPending;
    private float pressStart, heldAtRelease;
    private Vector3 chargeAim;
    private int chargeStage;
    private Transform chargeGlow;
    private readonly HashSet<Enemy> firstCut = new HashSet<Enemy>();
    private JOT_SkillSet skillSet;

    public bool IsSwinging { get; private set; }
    public bool IsCharging { get; private set; }
    public float ChargeHeld => pressing ? Time.time - pressStart : IsCharging ? heldAtRelease : 0f;
    public int ChargeStage => !IsCharging ? 0 : ChargeHeld >= StageTwoTime ? 2 : ChargeHeld >= StageOneTime ? 1 : 0;

    public override bool Rooted => IsSwinging;
    // Space is the weapon's own step / dodge (JOT_SkillSet slot 4), not the game's dash
    public override bool OwnsSpace => true;
    // The leap (지진분쇄) and other untouchable moments dodge the game's enemies too
    public override bool Invulnerable => JOT_SkillSet.Untouchable;

    private float Reach => DigRange + RunState.Stats.rangeBonus;

    // The skill component for this weapon
    protected abstract System.Type SkillType { get; }

    protected override void Equipped(bool on)
    {
        if (skillSet != null)
            Destroy(skillSet);
        skillSet = on ? (JOT_SkillSet)gameObject.AddComponent(SkillType) : null;
    }

    public override void ResetCombat()
    {
        StopAllCoroutines();
        IsSwinging = IsCharging = pressing = slamRequested = secondCutPending = false;
        if (animator != null && animator.isActiveAndEnabled)
        {
            animator.SetFloat(ChargeSpeedHash, 1f);
            animator.CrossFade("Locomotion", 0.1f);
        }
        // A new run: the skills come back ready (a fresh component)
        if (IsActive && skillSet != null)
        {
            Destroy(skillSet);
            skillSet = (JOT_SkillSet)gameObject.AddComponent(SkillType);
        }
    }

    // ── The click: tap = attack, hold = raise the axe and charge through two stages ──

    public override void Press(Vector3 aimPoint)
    {
        if (IsSwinging)
            return;
        pressing = true;
        pressStart = Time.time;
        chargeAim = aimPoint;
    }

    public override void Hold(Vector3 aimPoint)
    {
        if (!pressing)
            return;
        chargeAim = aimPoint;
        if (!IsCharging && ChargeHeld >= ChargeDelay)
            StartCoroutine(Charge());
    }

    public override void Release(Vector3 aimPoint)
    {
        if (!pressing)
            return;
        heldAtRelease = Time.time - pressStart;
        pressing = false;
        chargeAim = aimPoint;
        if (IsCharging)
            slamRequested = true;
        else
            SwingFree(aimPoint);
    }

    private void SwingFree(Vector3 aimPoint)
    {
        if (IsSwinging)
            return;
        float speed = AttackSpeed;
        if (animator != null)
            animator.SetFloat(AttackSpeedHash, speed);
        float impact = (profile != null ? profile.AttackImpactDelay : 0.22f) / speed;
        float duration = (profile != null ? profile.AttackDuration : 0.53f) / speed;
        StartCoroutine(Swing(aimPoint, impact, duration, () => miner.ChainFrom(HitFront(1f))));
    }

    private IEnumerator Swing(Vector3 aimPoint, float impact, float duration, System.Action onImpact)
    {
        IsSwinging = true;
        Vector3 toTarget = aimPoint - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
        if (animator != null)
            animator.SetTrigger(AttackHash);
        yield return new WaitForSeconds(impact);
        onImpact();
        yield return new WaitForSeconds(Mathf.Max(0f, duration - impact));
        IsSwinging = false;
    }

    private IEnumerator Charge()
    {
        GameAudio.Play("charge_hold");   // (power gathering into the axes)
        IsCharging = true;
        IsSwinging = true;
        slamRequested = false;
        firstCut.Clear();
        secondCutPending = true;
        int stageShown = 0;
        float speed = AttackSpeed;
        float holdPoint = profile != null ? profile.ChargeHoldPoint : 0.34f;
        float impactPoint = profile != null ? profile.ChargeImpactPoint : 0.49f;
        float swingSeconds = profile != null ? profile.SwingStateSeconds : 1.27f;
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, speed);
            animator.SetFloat(ChargeSpeedHash, RaiseSpeed);
            animator.SetTrigger(ChargeHash);
        }

        // Raise the axe slowly, then hold it up until the button is released
        while (!slamRequested)
        {
            Face(chargeAim, 540f);
            if (animator != null)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Charge") && state.normalizedTime >= holdPoint)
                    animator.SetFloat(ChargeSpeedHash, 0f);
            }
            int stage = ChargeStage;
            if (stage > stageShown)
            {
                stageShown = stage;
                Shockwave.Spawn(transform.position, 1.2f, stage == 2 ? StageTwoColor : StageOneColor, 10);
            }
            chargeStage = stage;
            yield return null;
        }

        // Release: finish the swing from where it is and land the blow
        int finalStage = ChargeStage;
        chargeStage = finalStage;
        Face(chargeAim, 9999f);
        float progress = 0f;
        if (animator != null)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            progress = state.IsName("Charge") ? Mathf.Min(state.normalizedTime, impactPoint) : 0f;
            animator.SetFloat(ChargeSpeedHash, speed);
        }
        float stateSeconds = swingSeconds / speed;
        yield return new WaitForSeconds((impactPoint - progress) * stateSeconds);
        Slam(finalStage);
        yield return new WaitForSeconds((1f - impactPoint) * stateSeconds * 0.8f);

        IsCharging = false;
        IsSwinging = false;
    }

    private void Slam(int stage)
    {
        List<Enemy> struck = HitFront(StageDamage[stage]);
        foreach (Enemy enemy in struck)
            firstCut.Add(enemy);
        if (stage >= 1)
        {
            // A shockwave rolls out from the impact; at full charge it knocks everything down
            float radius = stage == 2 ? ShockRadiusFull : ShockRadius;
            float shockMultiplier = stage == 2 ? ShockDamageFull : ShockDamage;
            Shockwave.Spawn(transform.position, radius, stage == 2 ? StageTwoColor : StageOneColor, 24);
            CameraShake.Shake(stage == 2 ? 0.35f : 0.2f, stage == 2 ? 0.3f : 0.15f);
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead)
                    continue;
                Vector3 offset = enemy.transform.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude > radius)
                    continue;
                if (!struck.Contains(enemy))
                {
                    Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * shockMultiplier)), critical);
                    struck.Add(enemy);
                    firstCut.Add(enemy);
                }
                if (stage == 2 && !enemy.IsDead)
                    enemy.Knockdown(KnockdownSeconds);
            }
        }
        miner.ChainFrom(struck);
    }

    private void Update()
    {
        // The double slash: the second cut when the Charge state reaches it
        if (secondCutPending && animator != null)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.IsName("Charge") && state.normalizedTime >= SecondCutPoint)
            {
                secondCutPending = false;
                SecondCut();
            }
            else if (!IsCharging && !state.IsName("Charge") && !animator.IsInTransition(0))
                secondCutPending = false;   // the charge was cut short
        }

        // A glow at the feet while charging
        if (!IsCharging)
        {
            if (chargeGlow != null)
                chargeGlow.gameObject.SetActive(false);
            return;
        }
        if (chargeGlow == null)
            chargeGlow = Mats.Shape(PrimitiveType.Cylinder, null, Vector3.zero, Vector3.one, null, false, "ChargeGlow").transform;
        int stage = ChargeStage;
        Color color = stage == 2 ? StageTwoColor : stage == 1 ? StageOneColor : new Color(0.45f, 0.3f, 0.15f);
        chargeGlow.GetComponent<Renderer>().sharedMaterial = Mats.Get("charge_glow_" + stage, color, 0.5f, color * (stage == 0 ? 0.6f : 2f));
        chargeGlow.gameObject.SetActive(true);
        float pulse = 1.4f + stage * 0.35f + Mathf.Sin(Time.time * (6f + stage * 4f)) * 0.08f;
        chargeGlow.position = new Vector3(transform.position.x, transform.position.y - 0.98f, transform.position.z);
        chargeGlow.localScale = new Vector3(pulse, 0.005f, pulse);
    }

    private void SecondCut()
    {
        int s = Mathf.Clamp(chargeStage, 0, SecondCutDamage.Length - 1);
        CameraShake.Shake(0.12f, 0.08f + 0.04f * s);
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead || !InReach(enemy))
                continue;
            Vector3 to = enemy.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.25f && Vector3.Angle(transform.forward, to) > FrontArc / 2f)
                continue;
            StrikeTimes(enemy, SecondCutDamage[s]);
            if (firstCut.Contains(enemy) && !enemy.IsDead)
                StrikeTimes(enemy, BothCutsBonus[s]);
        }
    }

    private void StrikeTimes(Enemy enemy, float multiplier)
    {
        bool critical = Random.value < RunState.Stats.critChance;
        float total = (stats != null ? stats.Damage : 1) * multiplier * (critical ? RunState.Stats.critMultiplier : 1f);
        Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(total)), critical);
    }

    private bool InReach(Component target)
    {
        Collider c = target.GetComponent<Collider>();
        Vector3 nearest = c != null ? c.ClosestPoint(transform.position) : target.transform.position;
        return new Vector2(nearest.x - transform.position.x, nearest.z - transform.position.z).magnitude <= Reach;
    }

    // Blade hits on every enemy in reach in front, at this multiple of a normal hit
    private List<Enemy> HitFront(float damageMultiplier)
    {
        var struck = new List<Enemy>();
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead || !InReach(enemy))
                continue;
            Vector3 toEnemy = enemy.transform.position - transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude > 0.25f && Vector3.Angle(transform.forward, toEnemy) > FrontArc / 2f)
                continue;
            Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * damageMultiplier)), critical);
            struck.Add(enemy);
        }
        return struck;
    }

    private void Face(Vector3 point, float degreesPerSecond)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degreesPerSecond * Time.deltaTime);
    }

    // The charge gauge over the head (the game's own)
    public override void DrawHud()
    {
        Camera cam = Camera.main;
        if (cam == null || !IsCharging)
            return;
        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.6f);
        if (screen.z <= 0f)
            return;
        const float width = 64f, height = 8f;
        Rect bar = new Rect(screen.x - width / 2f, Screen.height - screen.y - 24f, width, height);
        UiFont.Fill(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), new Color(0f, 0f, 0f, 0.7f));
        int stage = ChargeStage;
        Color color = stage == 2 ? StageTwoColor : stage == 1 ? StageOneColor : new Color(0.75f, 0.7f, 0.6f);
        UiFont.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(ChargeHeld / StageTwoTime), bar.height), color);
        float tick = bar.x + bar.width * StageOneTime / StageTwoTime;
        UiFont.Fill(new Rect(tick - 1f, bar.y - 2f, 2f, bar.height + 4f), Color.white);
    }
}
