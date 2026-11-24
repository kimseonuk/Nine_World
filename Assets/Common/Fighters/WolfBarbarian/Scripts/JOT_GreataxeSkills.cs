using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // The greataxe tree: a warrior who stands firm and pushes through.
    // Right click shoulder charge, Q spinning cleave, E war cry, R earth smash.
    public class JOT_GreataxeSkills : JOT_SkillSet
    {
        [Header("Right click: shoulder charge")]
        [SerializeField] private float chargeDistance = 7f;
        [SerializeField] private float chargeSeconds = 0.4f;
        [SerializeField] private float chargeWidth = 1.6f;
        [SerializeField] private float chargeDamage = 1.5f;     // × the hero's damage
        [SerializeField] private float chargeStun = 1.2f;

        [Header("Q: spinning cleave")]
        [SerializeField] private float spinRadius = 3.2f;
        [SerializeField] private float spinDamage = 2f;
        [SerializeField] private float spinPush = 14f;

        [Header("E: war cry")]
        [SerializeField] private float cryRadius = 9f;
        [SerializeField] private float crySeconds = 5f;
        [SerializeField] private float cryDamageTaken = 0.7f;   // -30%
        [SerializeField] private float cryDamageDealt = 1.3f;
        [SerializeField] private float cryBossInterrupt = 0.4f;

        [Header("R: earth smash")]
        [SerializeField] private float smashReach = 8f;
        [SerializeField] private float smashSeconds = 0.55f;
        [SerializeField] private float smashHeight = 3f;
        [SerializeField] private float smashRadius = 5f;
        [SerializeField] private float smashDamage = 3f;
        [SerializeField] private float smashAirborne = 1.4f;
        [SerializeField] private float smashBossGroggy = 1.5f;

        private static readonly Color ChargeColor = new Color(1f, 0.6f, 0.25f);
        private static readonly Color CryColor = new Color(0.95f, 0.3f, 0.2f);
        private static readonly Color SmashColor = new Color(0.85f, 0.7f, 0.45f);

        private float cryUntil;
        private RunStats cryStats;      // the stats the war cry raised (runes rebuild them from scratch)
        private Transform cryAura;
        private VfxPiece cryRing;           // the painted roar pulsing at the feet
        private ParticleSystem cryEmbers;   // embers rising off the hero while the war cry lasts

        protected override Skill[] Build()
        {
            Skill charge = null, spin = null, cry = null, smash = null;
            charge = new Skill { key = "우클릭", name = "어깨 돌진", icon = "JOT_Icon_Greataxe_Charge", cooldown = 6f, use = () => Begin(Charge(charge)) };
            spin = new Skill { key = "Q", name = "회전베기", icon = "JOT_Icon_Greataxe_Spin", cooldown = 5f, use = () => Begin(Spin(spin)) };
            cry = new Skill { key = "E", name = "전사의 포효", icon = "JOT_Icon_Greataxe_WarCry", cooldown = 14f, use = () => WarCry(cry), usableWhileActing = true,
                note = () => Time.time < cryUntil ? $"포효 {cryUntil - Time.time:0.0}초" : null };
            smash = new Skill { key = "R", name = "지진분쇄", icon = "JOT_Icon_Greataxe_Smash", cooldown = 16f, use = () => Begin(Smash(smash)) };
            Skill step = null;
            step = new Skill { key = "Space", name = "늑대 발걸음", icon = "JOT_Icon_Greataxe_Step", cooldown = 3.5f, use = () => Begin(WolfStep(step)) };
            return new[] { charge, spin, cry, smash, step };
        }

        [Header("Space: wolf step")]
        [SerializeField] private float stepDistance = 4.5f;
        [SerializeField] private float stepSeconds = 0.28f;
        [SerializeField] private float stepDodgeSeconds = 0.2f;   // untouchable at the start, then just immovable

        // ---- Space: a heavy step the way the keys push — dodges the first moment, then braces through the rest
        private IEnumerator WolfStep(Skill skill)
        {
            StartCooldown(skill, CdScale(skill, 1f));
            Vector3 dir = MoveKeysDirection();
            Face(dir);
            Lock(true);
            Untouchable = true;
            SuperArmor = true;
            JOT_SkillFx.Dust(transform.position, 1.1f, 14);
            TrailRenderer trail = JOT_SkillFx.Trail(transform, Vector3.up * 0.4f, new Color(0.9f, 0.75f, 0.5f, 0.3f), 0.6f, 0.15f);
            float started = Time.time;
            Vector3 stepFrom = transform.position;
            yield return Slide(dir, stepDistance, stepSeconds, at =>
            {
                if (Untouchable && Time.time - started >= stepDodgeSeconds)
                    Untouchable = false;
            });
            Untouchable = false;
            JOT_SkillFx.Stop(trail);
            JOT_SheetFx.Streak(stepFrom, transform.position, new Color(0.9f, 0.7f, 0.4f), 0.35f);
            JOT_SheetFx.Smoke(JOT_SheetFx.Feet(transform) + Vector3.up * 0.4f, 1.8f, new Color(0.55f, 0.48f, 0.4f, 0.7f));
            JOT_SkillFx.Dust(transform.position + dir * 0.4f, 1.3f, 16);
            JOT_SkillFx.Ring(transform.position, 0.2f, 1.6f, 0.35f, SmashColor, 0.25f);
            CameraShake.Shake(0.06f, 0.08f);
            // 늑대 발걸음 강화: the landing sends out a shock
            if (Has(JOT_AxeTrees.GaStepQuake))
            {
                Shockwave.Spawn(transform.position, 2.6f, SmashColor, 16);
                JOT_SkillFx.Ring(transform.position, 0.4f, 2.6f, 0.7f, SmashColor, 0.3f);
                foreach (Enemy e in EnemiesNear(transform.position, 2.6f))
                    Strike(e, 1.2f, SmashColor);
            }
            yield return new WaitForSeconds(0.08f);
            SuperArmor = false;
            Lock(false);
        }

        private bool Begin(IEnumerator routine)
        {
            StartCoroutine(routine);
            return true;
        }

        // ---- Right click: charge toward the cursor, shrugging off attacks, stunning whatever is in the way
        private static bool Has(string id) => JOT_AxeTrees.Greataxe.Has(id);
        private float CdScale(Skill skill, float less) => Has(JOT_AxeTrees.GaUnstoppable) ? Mathf.Max(0.2f, (skill.cooldown - less) / skill.cooldown) : 1f;

        private IEnumerator Charge(Skill skill)
        {
            StartCooldown(skill, CdScale(skill, 2f));
            float chargeWidth = this.chargeWidth * (Has(JOT_AxeTrees.GaChargeStun) ? 1.3f : 1f);
            float chargeStun = this.chargeStun + (Has(JOT_AxeTrees.GaChargeStun) ? 0.8f : 0f);
            Vector3 dir = AimDirection();
            Face(dir);
            Lock(true);
            SuperArmor = true;
            PlayState("Attack");

            // Kick-off dust, a burning streak behind, and a wedge of wind in front of the shoulder
            JOT_SkillFx.Dust(transform.position - dir * 0.5f, 1.2f, 16);
            JOT_SkillFx.Flash(transform.position + dir * 0.6f, ChargeColor, 2f);
            TrailRenderer trail = JOT_SkillFx.Trail(transform, Vector3.zero, new Color(1f, 0.75f, 0.4f, 0.35f), 0.8f, 0.2f);
            Quaternion ahead = Quaternion.LookRotation(dir);
            foreach (float side in new[] { -1f, 1f })
                JOT_SkillFx.Slash(transform.position + dir * 0.3f, ahead, 1.2f, 0.7f, 80f * side, -80f * side, 80f,
                    ChargeColor, chargeSeconds, 0.15f, transform).Progress = 1f;
            float nextDust = 0f;
            Vector3 chargeFrom = transform.position;

            var struck = new List<Enemy>();
            yield return Slide(dir, chargeDistance, chargeSeconds, at =>
            {
                if (Time.time >= nextDust)
                {
                    nextDust = Time.time + 0.05f;
                    JOT_SkillFx.Dust(at, 0.6f, 4);
                }
                foreach (Enemy e in EnemiesNear(at, chargeWidth))
                {
                    if (struck.Contains(e))
                        continue;
                    struck.Add(e);
                    Strike(e, chargeDamage, ChargeColor);
                    Stun(e, chargeStun);
                    if (e is JOT_EnemyBase body && !e.IsBoss)
                        body.Shove(dir * 8f + Vector3.Cross(Vector3.up, dir) * Mathf.Sign(Vector3.Dot(Vector3.Cross(Vector3.up, dir), e.transform.position - at) + 0.001f) * 6f);
                    if (IsCreature(e) && !e.IsBoss)
                        JOT_PlayerFeedback.Popup(e.transform.position + Vector3.up * 2.2f, "기절", ChargeColor, 1f);
                    Vector3 contact = Vector3.Lerp(at, e.transform.position, 0.6f) + Vector3.up * 0.6f;
                    JOT_SkillFx.Flash(contact, Color.Lerp(ChargeColor, Color.white, 0.4f), 2.2f, 0.15f);
                    JOT_SkillFx.Sparks(contact, ChargeColor, 14, 8f);
                    JOT_SheetFx.Hit(contact, 2.2f, ChargeColor);
                    CameraShake.Shake(0.08f, 0.1f);
                }
            });
            JOT_SkillFx.Stop(trail);
            JOT_SheetFx.Streak(chargeFrom, transform.position, ChargeColor, 0.45f);
            JOT_SkillFx.Dust(transform.position + dir * 0.6f, 1f, 10);
            JOT_SkillFx.Ring(transform.position + dir * 0.8f, 0.3f, 2f, 0.5f, ChargeColor, 0.3f);
            Shockwave.Spawn(transform.position + dir * 0.8f, 1.6f, ChargeColor, 10);
            SuperArmor = false;
            Lock(false);
        }

        // ---- Q: one big turn of the axe, pushing everything around away
        private IEnumerator Spin(Skill skill)
        {
            StartCooldown(skill);
            float spinRadius = this.spinRadius * (Has(JOT_AxeTrees.GaSpinWide) ? 1.3f : 1f);
            float spinDamage = this.spinDamage * (Has(JOT_AxeTrees.GaSpinWide) ? 1.25f : 1f);
            Lock(true);
            PlayState("Swing");
            Animator model = ActiveAnimator();
            Transform turn = model != null ? model.transform : null;
            Quaternion start = turn != null ? turn.localRotation : Quaternion.identity;
            bool hit = false;
            const float seconds = 0.42f;
            // The axe's path: a bright crescent turning with the hero (same easing as the model), and a fainter inner one
            Quaternion facing = Quaternion.LookRotation(Flat(transform.forward).sqrMagnitude > 0.01f ? Flat(transform.forward) : Vector3.forward);
            JOT_SkillFx.Slash(transform.position, facing, spinRadius * 0.9f, 1.4f, 40f, 360f, 230f,
                new Color(1f, 0.7f, 0.35f, 1f), seconds, 0.2f, transform);
            JOT_SkillFx.Slash(transform.position + Vector3.down * 0.3f, facing, spinRadius * 0.6f, 0.7f, 40f, 360f, 180f,
                new Color(1f, 0.6f, 0.25f, 0.7f), seconds, 0.15f, transform);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                if (turn != null)
                    turn.localRotation = start * Quaternion.Euler(0f, 360f * (1f - (1f - k) * (1f - k)), 0f);
                if (!hit && k >= 0.3f)
                {
                    hit = true;
                    Vector3 center = transform.position;
                    Shockwave.Spawn(center, spinRadius, ChargeColor, 18);
                    JOT_SkillFx.Ring(center, 0.6f, spinRadius, 0.9f, new Color(1f, 0.6f, 0.25f, 0.6f), 0.3f);
                    JOT_SkillFx.Dust(center, 1.3f, 18);
                    JOT_SheetFx.Whirl(center, spinRadius, new Color(1f, 0.65f, 0.3f));
                    CameraShake.Shake(0.1f, 0.12f);
                    foreach (Enemy e in EnemiesNear(center, spinRadius))
                    {
                        JOT_SkillFx.Sparks(e.transform.position + Vector3.up * 0.8f, ChargeColor, 10, 7f);
                        JOT_SheetFx.Hit(e.transform.position + Vector3.up * 0.9f, 1.8f, ChargeColor);
                        Strike(e, spinDamage, HitColor);
                        if (e is JOT_EnemyBase body && !e.IsBoss)
                            body.Shove(Flat(e.transform.position - center).normalized * spinPush);
                    }
                }
                yield return null;
            }
            if (turn != null)
                turn.localRotation = start;
            Lock(false);
        }

        // ---- E: taunt everything near, take less and deal more for a while; a boss drops what it was doing
        private bool WarCry(Skill skill)
        {
            StartCooldown(skill);
            Vector3 center = transform.position;
            JOT_PlayerFeedback.Popup(center + Vector3.up * 2.4f, "포효!", CryColor, 1.4f);
            Shockwave.Spawn(center, cryRadius * 0.6f, CryColor, 16);
            Shockwave.Spawn(center, cryRadius, CryColor, 24);
            // The roar itself: a red burst at the hero, three sound waves rolling out to the edge of its reach, sparks flung up
            JOT_SkillFx.Flash(center + Vector3.up * 0.5f, CryColor, 4.5f, 0.25f);
            for (int i = 0; i < 3; i++)
                JOT_SkillFx.Ring(center, 0.6f, cryRadius, 1.4f - i * 0.3f, new Color(CryColor.r, CryColor.g, CryColor.b, 0.75f - i * 0.15f), 0.5f, i * 0.12f);
            JOT_SkillFx.Burst(center, new JOT_SkillFx.Puff
            {
                count = 30, speed = 6f, size = 0.3f, life = 0.7f, gravity = -0.2f, spread = 0.55f, color = new Color(1f, 0.55f, 0.25f)
            });
            JOT_SheetFx.Roar(JOT_SheetFx.Feet(transform), cryRadius, CryColor, 0.7f);
            CameraShake.Shake(0.2f, 0.12f);
            foreach (Enemy e in EnemiesNear(center, cryRadius))
            {
                if (!IsCreature(e))
                    continue;
                JOT_SkillFx.Flash(e.transform.position + Vector3.up * 1.6f, CryColor, 1.4f, 0.3f);   // caught by the taunt
                if (e is JOT_EnemyBase body)
                    body.Taunt();
                if (e.IsBoss)
                {
                    e.Knockdown(cryBossInterrupt);
                    JOT_PlayerFeedback.Popup(e.transform.position + Vector3.up * 3f, "도발!", CryColor, 1.2f);
                }
            }

            EndCry();
            bool longer = Has(JOT_AxeTrees.GaCryLong);
            cryTakenApplied = longer ? 0.6f : cryDamageTaken;
            cryStats = RunState.Stats;
            cryStats.damageTakenMultiplier *= cryTakenApplied;
            cryStats.damageMultiplier *= cryDamageDealt;
            cryUntil = Time.time + crySeconds + (longer ? 3f : 0f);
            return true;
        }

        private float cryTakenApplied = 1f;

        private void EndCry()
        {
            // If the runes were rebuilt meanwhile, the boost went with the old stats
            if (cryStats != null && cryStats == RunState.Stats)
            {
                cryStats.damageTakenMultiplier /= cryTakenApplied;
                cryStats.damageMultiplier /= cryDamageDealt;
            }
            cryStats = null;
        }

        // ---- R: leap to the cursor and smash the ground: everything around is thrown up, a boss is left groggy
        private IEnumerator Smash(Skill skill)
        {
            Vector3 to = Flat(AimPointWithin(smashReach) - transform.position);
            StartCooldown(skill);
            float smashRadius = this.smashRadius * (Has(JOT_AxeTrees.GaSmashGroggy) ? 1.3f : 1f);
            float smashBossGroggy = this.smashBossGroggy + (Has(JOT_AxeTrees.GaSmashGroggy) ? 1.5f : 0f);
            Face(to.sqrMagnitude > 0.01f ? to : transform.forward);
            Lock(true);
            Untouchable = true;
            SuperArmor = true;
            PlayState("Swing");
            JOT_SkillFx.Dust(transform.position, 1.3f, 18);
            TrailRenderer trail = JOT_SkillFx.Trail(transform, Vector3.zero, new Color(0.95f, 0.8f, 0.5f, 0.55f), 1.2f, 0.35f);
            StartCoroutine(OverheadChop(smashSeconds * 0.6f));
            yield return Slide(to.sqrMagnitude > 0.01f ? to : transform.forward, to.magnitude, smashSeconds, null, smashHeight);
            Untouchable = false;
            JOT_SkillFx.Stop(trail);

            Vector3 center = transform.position;
            CameraShake.Shake(0.3f, 0.3f);
            // The impact: a flash, the ground splitting in glowing cracks, a dust wall and a wave rolling out
            Vector3 ground = JOT_SkillFx.OnGround(center, 0.3f);
            JOT_SkillFx.Flash(ground + Vector3.up * 0.4f, Color.Lerp(SmashColor, Color.white, 0.5f), 6f, 0.25f);
            JOT_SkillFx.Ring(center, 0.5f, smashRadius, 1.6f, new Color(SmashColor.r, SmashColor.g, SmashColor.b, 0.8f), 0.5f);
            JOT_SkillFx.Ring(center, 0.3f, smashRadius * 0.6f, 0.8f, new Color(1f, 0.95f, 0.8f, 0.7f), 0.3f, 0.06f);
            JOT_SkillFx.Cracks(center, smashRadius * 0.9f, 9, new Color(1f, 0.55f, 0.15f, 0.9f), 1.8f);
            JOT_SkillFx.Dust(center, 1.6f, 26);
            JOT_SkillFx.Sparks(ground + Vector3.up * 0.3f, new Color(1f, 0.75f, 0.35f), 24, 10f);
            Shockwave.Spawn(center, smashRadius, SmashColor, 28);
            JOT_EarthBurst.Spawn(center, 0.6f, smashRadius, 14, 0.8f);
            JOT_GroundHeave.Spawn(center, 0.3f, smashRadius);
            JOT_SheetFx.Roar(ground, smashRadius, SmashColor, 0.75f);
            JOT_SheetFx.Hit(ground + Vector3.up * 0.6f, 3.2f, SmashColor);
            JOT_SheetFx.Smoke(ground + Vector3.up * 0.5f, 3.6f, new Color(0.5f, 0.42f, 0.32f, 0.75f), 0.7f);
            foreach (Enemy e in EnemiesNear(center, smashRadius))
            {
                Strike(e, smashDamage, SmashColor, 0.3f);
                if (!IsCreature(e))
                    continue;
                if (e.IsBoss)
                {
                    Daze(e, smashBossGroggy);
                    JOT_PlayerFeedback.Popup(e.transform.position + Vector3.up * 3f, "그로기!", SmashColor, 1.3f);
                }
                else
                {
                    Stun(e, smashAirborne);
                    if (e is JOT_EnemyBase body)
                        body.Launch(1.6f);
                }
            }
            yield return new WaitForSeconds(0.2f);
            SuperArmor = false;
            Lock(false);
        }

        // On the way down: the axe's arc from over the head down into the ground in front, in the hero's facing plane
        private IEnumerator OverheadChop(float delay)
        {
            yield return new WaitForSeconds(delay);
            Vector3 forward = Flat(transform.forward).sqrMagnitude > 0.01f ? Flat(transform.forward).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            // local z = up (angle 0), local x = forward, so the head sweeps up -> forward -> down
            Quaternion plane = Quaternion.LookRotation(Vector3.up, right);
            JOT_SkillFx.Slash(transform.position + Vector3.up * 0.3f, plane, 1.7f, 1f, -30f, 190f, 170f,
                new Color(1f, 0.85f, 0.55f, 1f), smashSeconds * 0.4f, 0.15f, transform);
        }

        private Vector3 AimPointWithin(float reach)
        {
            CursorPoint(out Vector3 point);
            Vector3 to = Flat(point - transform.position);
            if (to.magnitude > reach)
                to = to.normalized * reach;
            return transform.position + to;
        }

        protected override void Update()
        {
            base.Update();
            if (cryStats != null && Time.time >= cryUntil)
                EndCry();
            UpdateAura();
        }

        // A red ring at the feet while the war cry lasts
        private void UpdateAura()
        {
            bool on = Time.time < cryUntil;
            if (cryAura == null)
            {
                if (!on)
                    return;
                cryAura = Mats.Shape(PrimitiveType.Cylinder, transform, Vector3.down * 0.95f, Vector3.one,
                    Mats.Get("JOT_fx_warcry", CryColor, 0.6f, CryColor * 2f), false, "JOT_WarCryAura").transform;
            }
            cryAura.gameObject.SetActive(on);
            // ...and a painted shockwave that keeps pulsing at the feet while it lasts
            if (on && cryRing == null)
                cryRing = JOT_SheetFx.GroundLoop(JOT_SheetFx.Feet(transform), 3.2f, CryColor);
            if (cryRing != null)
            {
                if (on) cryRing.transform.position = JOT_SheetFx.Feet(transform) + Vector3.up * 0.04f;
                else { Destroy(cryRing.gameObject); cryRing = null; }
            }
            if (cryEmbers == null)
                cryEmbers = JOT_SkillFx.Embers(transform, new Color(1f, 0.4f, 0.2f, 0.9f));
            var emission = cryEmbers.emission;
            emission.enabled = on;
            if (on)
            {
                float pulse = 1.6f + Mathf.Sin(Time.time * 8f) * 0.12f;
                cryAura.localScale = new Vector3(pulse, 0.02f, pulse);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (cryEmbers != null)
            {
                var emission = cryEmbers.emission;
                emission.enabled = false;
            }
            EndCry();
            cryUntil = 0f;
            if (cryRing != null)
            {
                Destroy(cryRing.gameObject);
                cryRing = null;
            }
        }

        // The huge greataxe model in the right hand while this weapon is held
        private JOT_AxeModels models;

        private void Start() => models = JOT_AxeModels.Put(gameObject, false);

        private void OnDestroy()
        {
            if (models != null)
                models.Drop();
        }
    }
}
