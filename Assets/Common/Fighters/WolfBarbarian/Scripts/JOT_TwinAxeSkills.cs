using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // The chained twin axes tree: a hunter who gets behind things and makes them bleed.
    // Right click shadow hunt, Q twin throw, E hunter's leap (throw the chained axe, press again to follow it),
    // R phantom execution. A second axe is held in the left hand.
    public class JOT_TwinAxeSkills : JOT_SkillSet
    {
        [Header("Right click: shadow hunt")]
        [SerializeField] private float huntReach = 8f;
        [SerializeField] private float huntDamage = 2f;
        [SerializeField] private float passDamage = 1f;         // to enemies passed through on the way to an empty spot

        [Header("Q: twin throw")]
        [SerializeField] private float throwRange = 12f;
        [SerializeField] private float throwSpeed = 26f;
        [SerializeField] private float throwDamage = 1.2f;
        [SerializeField] private float bothAxesBonus = 1f;

        [Header("E: hunter's leap")]
        [SerializeField] private float leapRange = 11f;
        [SerializeField] private float leapBleedingBonus = 1.3f; // +30% reach toward a bleeding target
        [SerializeField] private float chainSpeed = 34f;
        [SerializeField] private float leapWindow = 3f;          // seconds to press E again
        [SerializeField] private float firstHitDamage = 1f;
        [SerializeField] private float secondHitDamage = 1.5f;
        [SerializeField] private float terrainCooldownScale = 0.5f;

        [Header("R: phantom execution")]
        [SerializeField] private float executeReach = 9f;
        [SerializeField] private int executeHits = 10;
        [SerializeField] private float executeSeconds = 1f;
        [SerializeField] private float executeDamage = 0.5f;
        [SerializeField] private float executeBleedingBonus = 0.4f; // per hit on a target with 5+ stacks

        private static readonly Color AxeMetal = new Color(0.7f, 0.85f, 0.95f);
        private static readonly Color HuntColor = new Color(0.55f, 0.45f, 0.95f);
        private static readonly Color ThrowColor = new Color(0.6f, 0.85f, 1f);
        private static readonly Color BleedHitColor = new Color(1f, 0.35f, 0.35f);

        private Skill leapSkill;
        private bool leapHalf;
        private static bool Has(string id) => JOT_AxeTrees.TwinAxes.Has(id);
        private Transform chainAxe;           // the thrown chained axe, waiting for the second press
        private Enemy chainTarget;            // stuck in this enemy, or
        private bool chainOnTerrain;          // stuck in a rock or tree
        private float chainUntil;
        private LineRenderer chainLine;
        private Transform offHand;

        protected override Skill[] Build()
        {
            Skill hunt = null, twin = null, execute = null;
            hunt = new Skill { key = "우클릭", name = "그림자 사냥", icon = "JOT_Icon_Twin_ShadowHunt", cooldown = 5f, use = () => ShadowHunt(hunt) };
            twin = new Skill { key = "Q", name = "쌍도 투척", icon = "JOT_Icon_Twin_Throw", cooldown = 6f, use = () => { StartCoroutine(TwinThrow(twin)); return true; } };
            leapSkill = new Skill { key = "E", name = "사냥꾼의 도약", icon = "JOT_Icon_Twin_Leap", cooldown = 8f, use = HuntersLeap, usableWhileActing = true,
                note = () => chainAxe != null ? "E 다시: 도약" : null };
            execute = new Skill { key = "R", name = "환영 사형집행", icon = "JOT_Icon_Twin_Execution", cooldown = 18f, use = () => PhantomExecution(execute) };
            Skill dodge = null;
            dodge = new Skill { key = "Space", name = "그림자 회피", icon = "JOT_Icon_Twin_Dodge", cooldown = 2.5f, use = () => { StartCoroutine(ShadowDodge(dodge)); return true; } };
            return new[] { hunt, twin, leapSkill, execute, dodge };
        }

        [Header("Space: shadow dodge")]
        [SerializeField] private float dodgeDistance = 5.5f;
        [SerializeField] private float dodgeSeconds = 0.22f;

        // ---- Space: a quick untouchable dash the way the keys push, a violet ghost left where the hunter stood
        private IEnumerator ShadowDodge(Skill skill)
        {
            StartCooldown(skill);
            Vector3 dir = MoveKeysDirection();
            Face(dir);
            Lock(true);
            Untouchable = true;
            Transform model = ActiveAnimator() != null ? ActiveAnimator().transform : transform;
            JOT_SkillFx.Ghost(model, HuntColor, 0.35f);
            JOT_SkillFx.Smoke(transform.position + Vector3.up * 0.6f, HuntColor, 0.8f, 10);
            TrailRenderer trail = JOT_SkillFx.Trail(transform, Vector3.up * 0.8f, new Color(0.55f, 0.45f, 0.95f, 0.45f), 0.7f, 0.18f);
            // 그림자 회피 강화: whatever it slips past is cut and bleeds
            bool cuts = Has(JOT_AxeTrees.TwDodgeCut);
            var passed = new List<Enemy>();
            Vector3 dodgeFrom = transform.position;
            JOT_SheetFx.Smoke(transform.position - Vector3.up * 0.3f, 2.2f, new Color(0.55f, 0.35f, 0.8f, 0.85f));
            yield return Slide(dir, dodgeDistance, dodgeSeconds, at =>
            {
                if (!cuts) return;
                foreach (Enemy e in EnemiesNear(at, 1.4f))
                {
                    if (passed.Contains(e)) continue;
                    passed.Add(e);
                    Strike(e, 1f, HuntColor);
                    JOT_Bleed.Add(e, 1, stats.Damage);
                    JOT_SkillFx.Sparks(e.transform.position + Vector3.up * 0.7f, BleedHitColor, 8, 6f);
                }
            });
            JOT_SkillFx.Stop(trail);
            JOT_SkillFx.Smoke(transform.position + Vector3.up * 0.6f, HuntColor, 0.6f, 8);
            JOT_SheetFx.Streak(dodgeFrom, transform.position, HuntColor, 0.35f);
            Untouchable = false;
            Lock(false);
        }

        private void Start()
        {
            // The chained twin axe models in both hands; the old primitive axe only if the model has no such hands
            models = JOT_AxeModels.Put(gameObject, true);
            if (models.Left != null)
                offHand = models.Left;
            else
                HoldSecondAxe();
        }

        private JOT_AxeModels models;

        // The chain runs from the axe still in hand (the right one) to the thrown one
        private Vector3 ChainStart => models != null && models.Right != null ? models.RingOf(models.Right)
            : offHand != null ? offHand.position : transform.position + Vector3.up * 0.3f;

        private void ShowHeld(Transform axe, bool show)
        {
            if (axe != null && models != null)
                axe.gameObject.SetActive(show);
        }

        // A second axe in the left hand (fallback: a model without the barbarian's hands)
        private void HoldSecondAxe()
        {
            Transform hand = FindBone("lefthand") ?? FindBone("hand_l") ?? FindBone("l_hand") ?? FindBone("hand.l");
            if (hand == null)
                return;
            offHand = MakeAxe(hand, 1.4f, AxeMetal).transform;
            offHand.localRotation = Quaternion.Euler(0f, 0f, 90f);
            float scale = hand.lossyScale.x > 0.0001f ? 1f / hand.lossyScale.x : 1f;
            offHand.localScale = Vector3.one * scale;
        }

        private Transform FindBone(string part)
        {
            Transform best = null;
            foreach (Transform t in GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant().Replace(" ", "");
                if (n.EndsWith(part) && (best == null || t.name.Length < best.name.Length))
                    best = t;
            }
            return best;
        }

        // ---- Right click: appear behind (or beside) the enemy nearest the cursor and strike its back
        private bool ShadowHunt(Skill skill)
        {
            Enemy target = TargetNearCursor(huntReach);
            StartCooldown(skill, Has(JOT_AxeTrees.TwHuntBleed) ? (skill.cooldown - 1f) / skill.cooldown : 1f);
            Shockwave.Spawn(transform.position, 1.2f, HuntColor, 10);
            Vector3 from = transform.position;
            JOT_SkillFx.Smoke(from, ShadowSmoke, 1f, 14);   // gone in a puff of shadow
            if (target == null)
            {
                // Nothing under the cursor: slip through as a shadow to the cursor itself, cutting whatever is in the way
                StartCoroutine(ShadowDash());
                return true;
            }

            Vector3 behind = target.transform.position - target.transform.forward * 1.3f;
            if (!ClearPathBetween(target.transform.position, behind))
            {
                // Its back is against a wall: take its side instead
                Vector3 side = target.transform.right * (Random.value < 0.5f ? 1.3f : -1.3f);
                behind = target.transform.position + side;
                if (!ClearPathBetween(target.transform.position, behind))
                    behind = target.transform.position + Flat(transform.position - target.transform.position).normalized * 1.3f;
            }
            Teleport(behind);
            Face(target.transform.position - transform.position);
            Shockwave.Spawn(transform.position, 1.2f, HuntColor, 10);
            // A dark streak along the way, shadow where the hero comes out, and a quick slash down the target's back
            JOT_SkillFx.Streak(from + Vector3.up * 0.3f, transform.position + Vector3.up * 0.3f, new Color(0.35f, 0.25f, 0.7f, 0.7f), 0.5f, 0.25f);
            JOT_SkillFx.Smoke(transform.position, ShadowSmoke, 0.8f, 10);
            Vector3 back = target.transform.position + Vector3.up * 0.7f;
            JOT_SkillFx.TiltedSlash(back, target.transform.position - transform.position, 40f, 1.3f, 0.6f, 160f, SlashGlow(HuntColor), 0.1f);
            JOT_SheetFx.Smoke(from - Vector3.up * 0.3f, 2.2f, new Color(0.55f, 0.35f, 0.8f, 0.85f));
            JOT_SheetFx.Smoke(transform.position - Vector3.up * 0.3f, 2f, new Color(0.55f, 0.35f, 0.8f, 0.85f));
            JOT_SheetFx.Crescent(back, target.transform.position - transform.position, 2.8f, BleedHitColor, 30f);
            PlayState("Attack");
            if (Strike(target, huntDamage, HuntColor) > 0)
            {
                JOT_SkillFx.Sparks(back, BleedHitColor, 12, 6f);
                JOT_PlayerFeedback.Popup(target.transform.position + Vector3.up * 2.6f, "백어택!", HuntColor, 1.2f);
                JOT_Bleed.Add(target, Has(JOT_AxeTrees.TwHuntBleed) ? 3 : 1, stats.Damage);
            }
            CameraShake.Shake(0.08f, 0.08f);
            return true;
        }

        // Straight to the cursor (up to the reach), passing through monsters, rocks and trees like a shadow.
        // It comes out at the furthest free spot on the way, never inside a wall or a body.
        private IEnumerator ShadowDash()
        {
            CursorPoint(out Vector3 point);
            Vector3 start = transform.position;
            Vector3 to = Flat(point - start);
            if (to.magnitude > huntReach)
                to = to.normalized * huntReach;
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
            Vector3 goal = FreeSpotToward(start, start + to);
            Face(dir);
            Lock(true);
            Untouchable = true;
            PlayState("Attack");
            TrailRenderer trail = JOT_SkillFx.Trail(transform, Vector3.zero, new Color(0.35f, 0.25f, 0.7f, 0.6f), 1f, 0.25f);

            var cut = new List<Enemy>();
            body.enabled = false;   // through everything
            const float seconds = 0.14f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, goal, Mathf.Clamp01(t / seconds));
                foreach (Enemy e in EnemiesNear(transform.position, 1.1f))
                {
                    if (!IsCreature(e) || cut.Contains(e))
                        continue;
                    cut.Add(e);
                    Vector3 hitAt = e.transform.position + Vector3.up * 0.7f;
                    JOT_SkillFx.TiltedSlash(hitAt, dir, 30f, 1.1f, 0.5f, 140f, SlashGlow(HuntColor), 0.08f);
                    JOT_SkillFx.Sparks(hitAt, BleedHitColor, 8, 6f);
                    JOT_SheetFx.Crescent(hitAt, dir, 2.6f, BleedHitColor, Random.Range(-30f, 30f));
                    if (Strike(e, passDamage, HuntColor) > 0)
                        JOT_Bleed.Add(e, 1, stats.Damage);
                }
                yield return null;
            }
            transform.position = goal;
            body.enabled = true;

            JOT_SkillFx.Stop(trail);
            JOT_SkillFx.Streak(start + Vector3.up * 0.3f, goal + Vector3.up * 0.3f, new Color(0.35f, 0.25f, 0.7f, 0.7f), 0.5f, 0.25f);
            JOT_SkillFx.Smoke(goal, ShadowSmoke, 0.8f, 10);
            JOT_SheetFx.Streak(start, goal, HuntColor, 0.4f);
            JOT_SheetFx.Smoke(goal - Vector3.up * 0.3f, 2f, new Color(0.55f, 0.35f, 0.8f, 0.85f));
            Untouchable = false;
            Lock(false);
        }

        // The furthest spot from `from` toward `to` where the hero fits (no wall, rock or body there) and has ground underfoot
        private Vector3 FreeSpotToward(Vector3 from, Vector3 to)
        {
            Vector3 line = to - from;
            float length = line.magnitude;
            for (float d = length; d > 0.2f; d -= 0.25f)
            {
                Vector3 spot = from + line * (d / length);
                if (Fits(spot))
                    return spot;
            }
            return from;
        }

        private bool Fits(Vector3 spot)
        {
            float scale = transform.lossyScale.y;
            float radius = body.radius * scale, half = Mathf.Max(radius, body.height * scale * 0.5f - radius);
            Vector3 center = spot + body.center * scale + Vector3.up * 0.12f;   // a hair up so the floor itself doesn't count
            foreach (Collider c in Physics.OverlapCapsule(center - Vector3.up * half, center + Vector3.up * half, radius * 0.95f, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(transform))
                    return false;
            // Something to stand on
            foreach (RaycastHit hit in Physics.RaycastAll(center, Vector3.down, body.height * scale + 1.5f, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<Enemy>() == null)
                    return true;
            return false;
        }

        private static readonly Color ShadowSmoke = new Color(0.22f, 0.16f, 0.4f, 0.65f);
        private static readonly Vector3 AxeHead = new Vector3(0.28f, 0.64f, 0f);   // MakeAxe(size 2)'s head, for trails

        // A slash's colour: the skill's, lifted toward white so it reads as a blade's gleam
        private static Color SlashGlow(Color c) => new Color(Mathf.Lerp(c.r, 1f, 0.35f), Mathf.Lerp(c.g, 1f, 0.35f), Mathf.Lerp(c.b, 1f, 0.35f), 1f);

        // Two crossing cuts on a target
        private static void CrossCut(Vector3 at, Vector3 facing, Color color, float size)
        {
            JOT_SkillFx.TiltedSlash(at, facing, 45f, size, size * 0.45f, 150f, SlashGlow(color), 0.1f);
            JOT_SkillFx.TiltedSlash(at, facing, -45f, size, size * 0.45f, -150f, SlashGlow(color), 0.14f);
        }

        private static bool ClearPathBetween(Vector3 a, Vector3 b)
        {
            Vector3 from = a + Vector3.up * 0.6f, to = b + Vector3.up * 0.6f;
            foreach (RaycastHit hit in Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to) + 0.4f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<Enemy>() == null && hit.collider.GetComponentInParent<PlayerStats>() == null)
                    return false;
            return true;
        }

        // ---- Q: throw both axes one after the other; they pass through, and a target hit by both bleeds
        private IEnumerator TwinThrow(Skill skill)
        {
            StartCooldown(skill);
            float throwRange = this.throwRange * (Has(JOT_AxeTrees.TwThrowFar) ? 1.3f : 1f);
            Vector3 dir = AimDirection();
            Face(dir);
            PlayState("Attack");
            var first = new List<Enemy>();
            var second = new List<Enemy>();
            StartCoroutine(Fly(dir, first, null, models != null ? models.Right : null));
            yield return new WaitForSeconds(0.16f);
            PlayState("Attack");
            yield return Fly(dir, second, first, models != null ? models.Left : null);
        }

        private IEnumerator Fly(Vector3 dir, List<Enemy> struck, List<Enemy> otherAxe, Transform held = null)
        {
            ShowHeld(held, false);
            Transform axe = models != null ? JOT_AxeModels.Loose(true) : MakeAxe(null, 2f, AxeMetal).transform;
            Vector3 start = transform.position + Vector3.up * 0.2f + dir * 0.6f;
            axe.position = start;
            axe.rotation = ThrownPose(dir, 0f);
            // The tumbling head draws a spinning ribbon of light
            TrailRenderer trail = JOT_SkillFx.Trail(axe, AxeHead, new Color(0.6f, 0.85f, 1f, 0.75f), 0.35f, 0.14f);
            for (float travelled = 0f; travelled < throwRange; travelled += throwSpeed * Time.deltaTime)
            {
                axe.position = start + dir * travelled;
                axe.rotation = ThrownPose(dir, travelled);
                Vector3 at = axe.position;
                foreach (Enemy e in EnemiesNear(at, 1.1f))
                {
                    if (struck.Contains(e))
                        continue;
                    struck.Add(e);
                    Vector3 hitAt = e.transform.position + Vector3.up * 0.7f;
                    JOT_SkillFx.Sparks(hitAt, ThrowColor, 8, 6f);
                    JOT_SheetFx.Hit(hitAt, 1.6f, ThrowColor);
                    Strike(e, throwDamage, ThrowColor);
                    if (otherAxe != null && otherAxe.Contains(e) && !e.IsDead)
                    {
                        // Both axes in the same body: a red cross and a spray of blood
                        CrossCut(hitAt, dir, BleedHitColor, 1.1f);
                        JOT_SkillFx.Sparks(hitAt, BleedHitColor, 16, 7f);
                        Strike(e, bothAxesBonus, BleedHitColor, 0.45f);
                        JOT_Bleed.Add(e, Has(JOT_AxeTrees.TwThrowFar) ? 3 : 2, stats.Damage);
                    }
                }
                if (!ClearPathBetween(at - dir * 0.5f, at))
                {
                    JOT_SkillFx.Sparks(at, new Color(1f, 0.9f, 0.7f), 10, 6f);   // a wall or rock stops it
                    break;
                }
                yield return null;
            }
            JOT_SkillFx.Stop(trail);
            Destroy(axe.gameObject);
            ShowHeld(held, true);   // back in hand
        }

        // A thrown axe stands upright (blade in the vertical plane of its path) and tumbles end over end,
        // head first over the top, like a tomahawk. MakeAxe's handle runs along y, its head out along x.
        private static Quaternion ThrownPose(Vector3 dir, float travelled) =>
            Quaternion.LookRotation(dir) * Quaternion.Euler(travelled * 60f, 0f, 0f) * Quaternion.Euler(0f, -90f, 0f);

        // ---- E: throw the chained axe; press again within the window to fly to it
        private bool HuntersLeap()
        {
            if (chainAxe != null)
            {
                StartCoroutine(Leap());
                return true;
            }
            if (acting)
                return false;
            StartCoroutine(ThrowChain());
            return true;
        }

        private IEnumerator ThrowChain()
        {
            Vector3 dir = AimDirection();
            Enemy aimed = TargetNearCursor(leapRange * leapBleedingBonus);
            if (aimed != null)
                dir = Flat(aimed.transform.position - transform.position).normalized;
            float range = aimed != null && JOT_Bleed.StacksOn(aimed) > 0 ? leapRange * leapBleedingBonus : leapRange;
            Face(dir);
            PlayState("Attack");
            leapSkill.readyAt = Time.time + 0.3f;   // no double tap before it's out

            chainAxe = models != null ? JOT_AxeModels.Loose(true) : MakeAxe(null, 2f, AxeMetal).transform;
            if (models != null) models.SetLeftThrown(true);
            chainTarget = null;
            chainOnTerrain = false;
            Vector3 start = transform.position + Vector3.up * 0.2f;
            chainAxe.position = start;
            chainAxe.rotation = ThrownPose(dir, 0f);
            TrailRenderer trail = JOT_SkillFx.Trail(chainAxe, AxeHead, new Color(0.6f, 0.85f, 1f, 0.7f), 0.3f, 0.12f);
            for (float travelled = 0f; travelled < range; travelled += chainSpeed * Time.deltaTime)
            {
                if (chainAxe == null)
                {
                    JOT_SkillFx.Stop(trail);
                    yield break;
                }
                Vector3 at = start + dir * travelled;
                chainAxe.position = at;
                chainAxe.rotation = ThrownPose(dir, travelled);
                DrawChain();
                List<Enemy> hit = EnemiesNear(at, 1.1f);
                hit.RemoveAll(e => !IsCreature(e));
                if (hit.Count > 0)
                {
                    chainTarget = hit[0];
                    JOT_SkillFx.Sparks(at + Vector3.up * 0.4f, ThrowColor, 12, 6f);
                    JOT_SkillFx.Flash(at + Vector3.up * 0.4f, ThrowColor, 1.4f, 0.15f);
                    JOT_SheetFx.Hit(at + Vector3.up * 0.4f, 1.8f, ThrowColor);
                    Strike(chainTarget, firstHitDamage, ThrowColor);
                    break;
                }
                if (!ClearPathBetween(at - dir * 0.5f, at))
                {
                    chainOnTerrain = true;
                    Shockwave.Spawn(at, 0.8f, ThrowColor, 8);
                    JOT_SkillFx.Sparks(at, new Color(1f, 0.9f, 0.7f), 14, 6f);   // bites into the rock
                    JOT_SkillFx.Smoke(at, new Color(0.55f, 0.5f, 0.45f, 0.5f), 0.6f, 8);
                    break;
                }
                yield return null;
            }
            JOT_SkillFx.Stop(trail);
            chainUntil = Time.time + leapWindow;
            if (chainTarget != null && chainTarget.IsDead)
                chainTarget = null;
        }

        private IEnumerator Leap()
        {
            if (chainAxe == null)
                yield break;
            Vector3 goal = chainTarget != null ? chainTarget.transform.position : chainAxe.position;
            Vector3 to = Flat(goal - transform.position);
            float distance = Mathf.Max(0f, to.magnitude - (chainTarget != null ? 1.2f : 0.3f));
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
            Face(dir);
            Lock(true);
            Untouchable = true;
            PlayState("Attack");
            var passed = new List<Enemy>();
            JOT_SkillFx.Dust(transform.position, 0.8f, 8);
            TrailRenderer trail = JOT_SkillFx.Trail(transform, Vector3.zero, new Color(0.6f, 0.85f, 1f, 0.6f), 1.1f, 0.25f);
            yield return Slide(dir, distance, 0.25f, at =>
            {
                DrawChain();
                foreach (Enemy e in EnemiesNear(at, 1.3f))
                    if (e != chainTarget && !passed.Contains(e) && IsCreature(e))
                    {
                        passed.Add(e);
                        Stun(e, 0.35f);   // a light stagger on the way
                        JOT_SkillFx.Sparks(e.transform.position + Vector3.up * 0.7f, ThrowColor, 6, 5f);
                    }
            }, 0.8f);
            Untouchable = false;
            JOT_SkillFx.Stop(trail);

            if (chainTarget != null && !chainTarget.IsDead)
            {
                // Landing on it: a cross of cuts and a spray of blood
                Vector3 hitAt = chainTarget.transform.position + Vector3.up * 0.7f;
                CrossCut(hitAt, dir, BleedHitColor, 1.3f);
                JOT_SkillFx.Flash(hitAt, BleedHitColor, 2f, 0.15f);
                JOT_SheetFx.Crescent(hitAt, dir, 3f, BleedHitColor, 45f);
                JOT_SheetFx.Crescent(hitAt, dir, 3f, BleedHitColor, -45f, true);
                JOT_SkillFx.Sparks(hitAt, BleedHitColor, 16, 7f);
                bool wasBleeding = JOT_Bleed.StacksOn(chainTarget) > 0;
                Strike(chainTarget, secondHitDamage, BleedHitColor, 0.3f);
                JOT_Bleed.Add(chainTarget, 1, stats.Damage);
                CameraShake.Shake(0.1f, 0.12f);
                // 사냥꾼의 도약 강화: onto a bleeding target, half the wait
                if (wasBleeding && Has(JOT_AxeTrees.TwLeapReset))
                    leapHalf = true;
            }
            Shockwave.Spawn(transform.position, 1.6f, ThrowColor, 12);
            JOT_SkillFx.Dust(transform.position, 1f, 10);
            JOT_SheetFx.Whirl(transform.position, 1.4f, ThrowColor, false, 0.32f);
            StartCooldown(leapSkill, (chainOnTerrain ? terrainCooldownScale : 1f) * (leapHalf ? 0.5f : 1f));
            leapHalf = false;
            ClearChain();
            Lock(false);
        }

        private void DrawChain()
        {
            if (chainAxe == null)
                return;
            if (chainLine == null)
            {
                var go = new GameObject("JOT_Chain");
                chainLine = go.AddComponent<LineRenderer>();
                JOT_AxeModels.SetupChain(chainLine);
                chainLine.positionCount = 10;
            }
            // Taut while flying or pulling, a little slack while it waits in the target
            Vector3 a = ChainStart, b = chainAxe.position;
            float slack = acting ? 0f : Mathf.Min(0.5f, Vector3.Distance(a, b) * 0.05f);
            for (int i = 0; i < chainLine.positionCount; i++)
            {
                float u = i / (float)(chainLine.positionCount - 1);
                chainLine.SetPosition(i, Vector3.Lerp(a, b, u) + Vector3.down * Mathf.Sin(u * Mathf.PI) * slack);
            }
        }

        private void ClearChain()
        {
            if (chainAxe != null)
                Destroy(chainAxe.gameObject);
            if (chainLine != null)
                Destroy(chainLine.gameObject);
            chainAxe = null;
            chainLine = null;
            chainTarget = null;
            chainUntil = 0f;
            if (models != null) models.SetLeftThrown(false);   // reeled back into the left hand
        }

        // ---- R: dash in and cut the target ten times from all sides, faster than the eye
        private bool PhantomExecution(Skill skill)
        {
            Enemy target = TargetNearCursor(executeReach);
            if (target == null)
            {
                JOT_PlayerFeedback.Popup(transform.position + Vector3.up * 2.2f, "대상 없음", HuntColor, 0.9f);
                return false;
            }
            StartCooldown(skill);
            StartCoroutine(Execute(target));
            return true;
        }

        private IEnumerator Execute(Enemy target)
        {
            Lock(true);
            Untouchable = true;
            SuperArmor = true;
            Vector3 home = transform.position;
            int executeHits = this.executeHits + (Has(JOT_AxeTrees.TwExecMore) ? 4 : 0);
            float gap = executeSeconds / executeHits;
            for (int i = 0; i < executeHits && target != null && !target.IsDead; i++)
            {
                // Each cut from another side of the target, leaving an afterimage where the hero was
                Afterimage(transform.position);
                float angle = i * 137.5f;
                Vector3 side = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 1.3f;
                Vector3 spot = target.transform.position + side;
                if (ClearPathBetween(target.transform.position, spot))
                    Teleport(spot);
                Face(target.transform.position - transform.position);
                PlayState("Attack");
                bool deep = JOT_Bleed.StacksOn(target) >= 5;
                // Each cut: a long thin gleam straight through the target at a new angle (red once it's bleeding hard)
                Vector3 c = target.transform.position + Vector3.up * 0.7f;
                Vector3 cut = (Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward + Vector3.up * Random.Range(-0.7f, 0.7f)).normalized;
                Color tint = deep ? BleedHitColor : HuntColor;
                JOT_SkillFx.Streak(c - cut * 1.9f, c + cut * 1.9f, SlashGlow(tint), 0.32f, 0.22f);
                JOT_SkillFx.Sparks(c, deep ? BleedHitColor : new Color(0.8f, 0.7f, 1f), 6, 6f);
                JOT_SheetFx.Flurry(c, Random.Range(1.8f, 2.6f), tint);
                Strike(target, executeDamage + (deep ? executeBleedingBonus : 0f), tint, (i % 3) * 0.25f);
                CameraShake.Shake(0.05f, 0.06f);
                yield return new WaitForSeconds(gap);
            }
            if (target != null)
            {
                // The finish: a bright flash, a big cross through it, a shadow ring and (if it bled) a red spray
                Vector3 c = target.transform.position + Vector3.up * 0.7f;
                JOT_SkillFx.Flash(c, SlashGlow(HuntColor), 3.5f, 0.22f);
                Vector3 across = Flat(c - transform.position).sqrMagnitude > 0.01f ? Flat(c - transform.position).normalized : transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, across);
                JOT_SkillFx.Streak(c - (side + Vector3.up) * 1.8f, c + (side + Vector3.up) * 1.8f, Color.white, 0.35f, 0.35f);
                JOT_SkillFx.Streak(c - (side - Vector3.up) * 1.8f, c + (side - Vector3.up) * 1.8f, Color.white, 0.35f, 0.35f);
                JOT_SkillFx.Ring(c, 0.4f, 2.6f, 0.8f, new Color(HuntColor.r, HuntColor.g, HuntColor.b, 0.7f), 0.35f);
                JOT_SkillFx.Sparks(c, BleedHitColor, 20, 8f);
                JOT_SheetFx.Mark(c, 3f, BleedHitColor);
                JOT_SheetFx.Hit(c, 3.4f, BleedHitColor);
                JOT_SheetFx.Smoke(c, 3f, new Color(0.55f, 0.35f, 0.75f, 0.9f), 0.7f);
            }
            if (target != null && !target.IsDead)
                Shockwave.Spawn(target.transform.position, 2f, HuntColor, 16);
            Untouchable = false;
            SuperArmor = false;
            Lock(false);
        }

        private void Afterimage(Vector3 at)
        {
            // A see-through purple copy of the hero, frozen mid-swing with both axes, that fades; plus a wisp of smoke
            Animator model = ActiveAnimator();
            JOT_SkillFx.Ghost(model != null ? model.transform : transform, new Color(0.55f, 0.4f, 1f, 0.55f), 0.35f);
            JOT_SkillFx.Smoke(at, ShadowSmoke, 0.5f, 4);
        }

        protected override void Update()
        {
            base.Update();
            if (chainAxe != null && !acting)
            {
                if (chainTarget != null)
                {
                    if (chainTarget.IsDead)
                        chainTarget = null;
                    else
                        chainAxe.position = chainTarget.transform.position + Vector3.up * 0.8f;
                }
                DrawChain();
                // Not followed in time: the chain is pulled back
                if (Time.time >= chainUntil && chainUntil > 0f)
                {
                    ClearChain();
                    StartCooldown(leapSkill, chainOnTerrain ? terrainCooldownScale : 1f);
                }
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ClearChain();
            if (acting)
                Lock(false);
        }

        private void OnDestroy()
        {
            if (models != null)
                models.Drop();
            else if (offHand != null)
                Destroy(offHand.gameObject);
        }
    }
}
