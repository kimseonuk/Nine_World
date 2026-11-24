using System.Collections;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // A huge old tree. Hit it enough and it topples away from the player: whatever lies under it is crushed
    // (ordinary enemies die outright, the guardian is left groggy), then the trunk sinks away.
    // It reads as a gimmick at a glance: hit pips float over it, and while a boss stands within its fall a glowing amber
    // ring circles its foot with "기회!" over it. A charging guardian that rams it brings it down on itself.
    public class JOT_AncientTree : JOT_HittableProp
    {
        [SerializeField] private float treeHeight = 7f;
        [SerializeField] private float fallWidth = 2.6f;
        [SerializeField] private float fallTime = 0.7f;
        [SerializeField] private int bossDamage = 10;
        [SerializeField] private float bossGroggy = 4f;
        [SerializeField] private float playerDamage = 20f;

        private static readonly Color ChanceColor = new Color(1f, 0.72f, 0.2f);

        private GameObject warning;
        private GameObject mark;
        private bool chance;            // a boss is standing where this tree would land
        private static GUIStyle pipStyle, labelStyle;

        public float Height => treeHeight;

        public override string Prompt => broken ? null
            : chance ? "고목 — 지금! 수호자 반대편에서 치면 수호자 쪽으로 쓰러집니다"
            : hitsLeft > 1 ? $"고목 — {hitsLeft}번 치면 쓰러집니다 (수호자를 아래로 유인하세요)"
            : "고목 — 다음 한 번이면 쓰러집니다! 빨간 줄 방향으로 넘어갑니다";

        // With one hit left, show where it will fall (away from whoever hits it)
        protected override void OnHit(Vector3 from)
        {
            if (hitsLeft != 1)
                return;
            if (warning != null)
                Destroy(warning);
            warning = JOT_Fx.Line(transform.position, FallDirection(from), treeHeight, fallWidth);
        }

        protected override void OnBroken(Vector3 from)
        {
            if (warning != null)
                Destroy(warning);
            ShowChance(false);
            StartCoroutine(Fall(FallDirection(from)));
        }

        // Rammed by a charging guardian: down it comes, back onto the one that rammed it
        public void ToppleOnto(Vector3 point)
        {
            if (broken)
                return;
            broken = true;
            hitsLeft = 0;
            if (warning != null)
                Destroy(warning);
            ShowChance(false);
            Vector3 dir = JOT_Area.Flat(point - transform.position);
            StartCoroutine(Fall(dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward));
        }

        private Vector3 FallDirection(Vector3 from)
        {
            Vector3 dir = JOT_Area.Flat(transform.position - from);
            return dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        }

        private void Update()
        {
            if (broken)
                return;
            // Is a boss standing where this tree could land?
            bool now = false;
            foreach (Enemy enemy in Enemy.Alive)
            {
                if (enemy == null || !enemy.IsBoss || enemy.IsDead)
                    continue;
                var body = enemy.GetComponent<CharacterController>();
                float reach = treeHeight + (body != null ? body.radius * 0.6f : 0f);
                if (JOT_Area.Flat(enemy.transform.position - transform.position).magnitude <= reach)
                {
                    now = true;
                    break;
                }
            }
            ShowChance(now);
        }

        private void ShowChance(bool on)
        {
            chance = on;
            if (on && mark == null)
                mark = JOT_Fx.Mark(transform.position, 1.6f, 2.4f, ChanceColor);
            if (mark != null)
                mark.SetActive(on);
        }

        // Over the tree: its hit pips, and "기회!" while a boss is in its reach
        private void OnGUI()
        {
            if (broken || Time.timeScale <= 0f || BossCutscene.Playing)
                return;
            Camera cam = Camera.main;
            if (cam == null)
                return;
            Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 3.2f);
            if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width || screen.y < 0f || screen.y > Screen.height)
                return;
            if (pipStyle == null)
            {
                pipStyle = UiFont.Style(15, TextAnchor.MiddleCenter, FontStyle.Bold);
                labelStyle = UiFont.Style(16, TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            float x = screen.x, y = Screen.height - screen.y;
            // Pips: one per hit still needed
            const float pip = 10f, gap = 4f;
            float width = hitsToBreak * pip + (hitsToBreak - 1) * gap;
            UiFont.Fill(new Rect(x - width / 2f - 5f, y - 9f, width + 10f, 18f), new Color(0f, 0f, 0f, 0.55f));
            for (int i = 0; i < hitsToBreak; i++)
                UiFont.Fill(new Rect(x - width / 2f + i * (pip + gap), y - 5f, pip, pip),
                    i < hitsLeft ? (chance ? ChanceColor : new Color(0.85f, 0.8f, 0.65f)) : new Color(1f, 1f, 1f, 0.15f));
            if (chance)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 8f);
                GUI.color = new Color(ChanceColor.r, ChanceColor.g, ChanceColor.b, pulse);
                GUI.Label(new Rect(x - 60f, y - 34f, 120f, 22f), "기회!", labelStyle);
                GUI.color = Color.white;
            }
        }

        private IEnumerator Fall(Vector3 direction)
        {
            Transform body = visual != null ? visual : transform;
            Quaternion start = body.rotation;
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            for (float t = 0f; t < fallTime; t += Time.deltaTime)
            {
                float k = t / fallTime;
                body.rotation = Quaternion.AngleAxis(90f * k * k, axis) * start; // speeds up as it goes
                yield return null;
            }
            body.rotation = Quaternion.AngleAxis(90f, axis) * start;

            var crushed = JOT_Area.Strip(transform.position, direction, treeHeight, fallWidth);
            JOT_Area.HitEnemies(crushed, transform.position, bossDamage, null, killMinions: true, bossGroggy: bossGroggy);
            JOT_Area.HurtPlayer(crushed, playerDamage);
            for (float d = 1.5f; d <= treeHeight; d += 2f)
                JOT_EarthBurst.Spawn(transform.position + direction * d, 0f, 1.6f, 8);
            CameraShake.Shake(0.4f, 0.35f);

            // Done its damage: out of the way so the player can get at whatever it crushed
            JOT_SinkAway.Begin(gameObject, 0.6f, 1.2f);
        }

        private void OnDestroy()
        {
            if (warning != null)
                Destroy(warning);
            if (mark != null)
                Destroy(mark);
        }
    }
}
