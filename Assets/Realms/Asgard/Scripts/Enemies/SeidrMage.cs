using System.Collections;
using UnityEngine;

// 세이드 마도사: a hooded sorcerer of seidr magic, only its violet eyes showing. It hangs back behind the others,
// fills the floor with curses and — its most dangerous trick — stops to chant a violet rune onto every monster
// near it, making them hit harder. Cut the chant short (or kill it first).
//   주술 투사체   a slow violet orb that hunts the player
//   룬 지대       rune circles laid under and round the player that burst a moment later
//   저주 영역     a pool of curse that hurts whoever stands in it
//   영혼의 사슬   ghost chains rise from a marked ring and bind the player's feet
//   주술 강화     a chant (it stands still, open to attack) that empowers nearby monsters
public class SeidrMage : HumanoidEnemy
{
    private static readonly int CastHash = Animator.StringToHash("Cast");
    private static readonly int GroundHash = Animator.StringToHash("Ground");
    private static readonly int CurseHash = Animator.StringToHash("Curse");
    private static readonly int ChainHash = Animator.StringToHash("Chain");
    private static readonly int ChantHash = Animator.StringToHash("Chant");

    [Header("Distance")]
    [SerializeField] private float preferredRange = 8f;
    [SerializeField] private float castRange = 12f;
    [SerializeField] private float tooClose = 4f;

    [Header("Hex orb")]
    [SerializeField] private float orbCast = 0.6f;
    [SerializeField] private float spellCooldown = 1.8f;

    [Header("Rune ground")]
    [SerializeField] private int runeCircles = 3;
    [SerializeField] private float runeRadius = 1.6f;
    [SerializeField] private float runeDelay = 1.3f;
    [SerializeField] private float runeDamageMultiplier = 1.2f;
    [SerializeField] private float runeCooldown = 6f;

    [Header("Curse field")]
    [SerializeField] private float curseRadius = 2.3f;
    [SerializeField] private float curseSeconds = 5f;
    [SerializeField] private float curseTickMultiplier = 0.35f;
    [SerializeField] private float curseCooldown = 9f;

    [Header("Soul chains")]
    [SerializeField] private float chainRadius = 1.5f;
    [SerializeField] private float chainDelay = 0.9f;
    [SerializeField] private float chainBind = 1.8f;
    [SerializeField] private float chainCooldown = 7f;

    [Header("Empowering chant")]
    [SerializeField] private float chantTime = 1.6f;
    [SerializeField] private float chantRadius = 10f;
    [SerializeField] private float empowerFactor = 1.4f;
    [SerializeField] private float empowerSeconds = 8f;
    [SerializeField] private float chantCooldown = 11f;

    private float runeTimer, curseTimer, chainTimer, chantTimer;

    protected override void Awake()
    {
        base.Awake();
        runeTimer = Random.Range(2f, runeCooldown);
        curseTimer = Random.Range(3f, curseCooldown);
        chainTimer = Random.Range(2.5f, chainCooldown);
        chantTimer = Random.Range(3f, chantCooldown * 0.6f);
    }

    private Vector3 HandPoint => transform.position + Vector3.up * 1f * transform.localScale.y + transform.forward * 0.55f;

    protected override Vector3 Think(Vector3 toPlayer, float distance)
    {
        runeTimer -= Time.deltaTime;
        curseTimer -= Time.deltaTime;
        chainTimer -= Time.deltaTime;
        chantTimer -= Time.deltaTime;
        Vector3 dir = toPlayer / distance;

        if (distance < tooClose && cooldown > 0f)
        {
            // Hurries away to keep its distance
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(-dir), 720f * Time.deltaTime);
            return -dir * moveSpeed;
        }
        if (cooldown > 0f)
        {
            float along = Mathf.Clamp(distance - preferredRange, -1f, 1f);
            return dir * along * moveSpeed * 0.6f;
        }

        // The chant first, if there are monsters around that aren't already empowered
        if (chantTimer <= 0f && AlliesToEmpower() > 0)
        {
            chantTimer = chantCooldown;
            StartAction(Chant(), spellCooldown);
            return Vector3.zero;
        }
        if (distance > castRange)
            return dir * moveSpeed;

        if (chainTimer <= 0f)
        {
            chainTimer = chainCooldown;
            StartAction(SoulChains(), spellCooldown);
        }
        else if (runeTimer <= 0f)
        {
            runeTimer = runeCooldown;
            StartAction(RuneGround(), spellCooldown);
        }
        else if (curseTimer <= 0f)
        {
            curseTimer = curseCooldown;
            StartAction(CurseField(), spellCooldown);
        }
        else
            StartAction(HexOrb(), spellCooldown);
        return Vector3.zero;
    }

    private int AlliesToEmpower()
    {
        int count = 0;
        foreach (Enemy e in Enemy.Alive)
            if (e != null && e != this && !e.IsDead && !e.Empowered && FlatDistance(e.transform.position, transform.position) <= chantRadius)
                count++;
        return count;
    }

    protected override Color FeelColor => MobFx.SeidrViolet;

    // A violet magic circle at its feet while it casts (kept as the warning, so a cut-off cast clears it); the body
    // gathers itself into every cast
    private void CastCircle(float size)
    {
        VfxPiece circle = Vfx.Decal(transform.position + Vector3.up * 0.05f, size, MobFx.SeidrViolet, "runes", 90f);
        circle.transform.SetParent(transform, true);
        warning = circle.gameObject;
        EnemyFeel.Coil(transform, 0.5f, 0.07f);
    }

    // 주술 투사체
    private IEnumerator HexOrb()
    {
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(CastHash);
        CastCircle(1.6f);
        Vfx.Gather(HandPoint, 0.8f, MobFx.SeidrViolet, orbCast, 40f);
        yield return new WaitForSeconds(orbCast);
        Destroy(warning);
        FacePlayer();
        Vector3 from = HandPoint;
        Vector3 dir = player != null ? player.position + Vector3.up * 0.3f - from : transform.forward;
        EnemyFeel.Launch(transform, from, MobFx.SeidrViolet);
        Projectile.CurseOrb(from, dir, this, attackDamage);
        yield return new WaitForSeconds(0.4f);
    }

    // 룬 지대: one circle right under the player, the others round them, bursting together
    private IEnumerator RuneGround()
    {
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(GroundHash);
        CastCircle(2f);
        yield return new WaitForSeconds(0.45f);
        if (player != null)
        {
            Vector3 at = Flat(player.position);
            float spin = Random.Range(0f, 360f);
            for (int i = 0; i < runeCircles; i++)
            {
                Vector3 c = i == 0 ? at : at + Quaternion.Euler(0f, spin + i * 360f / (runeCircles - 1), 0f) * Vector3.forward * runeRadius * 1.7f;
                MobFx.DelayedBlast(c, runeRadius, runeDelay + i * 0.12f, attackDamage * runeDamageMultiplier, this, MobFx.SeidrViolet);
            }
        }
        yield return new WaitForSeconds(0.5f);
        Destroy(warning);
    }

    // 저주 영역: a pool of curse spreads where the player stands
    private IEnumerator CurseField()
    {
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(CurseHash);
        CastCircle(2f);
        yield return new WaitForSeconds(0.55f);
        if (player != null)
            MobFx.CurseField(Flat(player.position), curseRadius, curseSeconds, attackDamage * curseTickMultiplier, this);
        yield return new WaitForSeconds(0.45f);
        Destroy(warning);
    }

    // 영혼의 사슬
    private IEnumerator SoulChains()
    {
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(ChainHash);
        CastCircle(2f);
        yield return new WaitForSeconds(0.35f);
        if (player != null)
            MobFx.SoulChains(Flat(player.position), chainRadius, chainDelay, attackDamage * 0.6f, chainBind, this);
        yield return new WaitForSeconds(0.6f);
        Destroy(warning);
    }

    // 주술 강화: it stops and chants, violet light pouring into it; at the end a violet rune lights over every
    // monster near it. A hit that interrupts (단절) breaks the chant.
    private IEnumerator Chant()
    {
        if (animator != null)
            animator.SetTrigger(ChantHash);
        CastCircle(chantRadius * 0.5f);
        Vfx.Gather(transform.position + Vector3.up * 1.1f, 2.2f, MobFx.SeidrViolet, chantTime, 80f);
        // Threads of violet light reach out to the monsters it's about to empower
        float thread = 0f;
        for (float t = 0f; t < chantTime; t += Time.deltaTime)
        {
            if ((thread -= Time.deltaTime) <= 0f)
            {
                thread = 0.35f;
                foreach (Enemy e in Enemy.Alive)
                    if (e != null && e != this && !e.IsDead && FlatDistance(e.transform.position, transform.position) <= chantRadius)
                        Vfx.Bolt(transform.position + Vector3.up * 1.2f, e.transform.position + Vector3.up * 1f, MobFx.SeidrViolet, 0.12f, 0.2f);
            }
            yield return null;
        }
        Destroy(warning);
        Vfx.GroundRing(transform.position, chantRadius * 0.5f, MobFx.SeidrViolet, 0.5f, "runes");
        // the chant breaks over its allies like a war cry
        EnemyFeel.Roar(transform, MobFx.SeidrViolet, chantRadius * 0.5f, 0.18f);
        foreach (Enemy e in Enemy.Alive.ToArray())
            if (e != null && e != this && !e.IsDead && FlatDistance(e.transform.position, transform.position) <= chantRadius)
                e.Empower(empowerFactor, empowerSeconds);
        yield return new WaitForSeconds(0.4f);
    }
}
