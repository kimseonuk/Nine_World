using System.Collections;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // A giant's weapon (axe, hammer) hung on a ruined wall by rusty chains. Each chain takes a few blows to snap;
    // once the last one goes the weapon drops forward. It is big enough to hurt the guardian badly: whatever is
    // under it gets crushed (the guardian is left groggy), then rings of force spread from where it landed,
    // hurting the player too (move between them), and the weapon sinks away. Left hanging, the guardian tears it
    // down in its second phase.
    public class JOT_ChainedWeapon : JOT_HittableProp
    {
        [SerializeField] private Transform weapon;                                 // the hanging weapon itself
        [SerializeField] private Transform[] chains;                               // wall-to-weapon chains
        [SerializeField] private int hitsPerChain = 2;                             // blows to snap one chain
        [SerializeField] private Transform swing;                                  // optional: weapon and chains sway around this
        [SerializeField] private float swayDegrees = 2f;                           // grows as chains snap
        [SerializeField] private Vector3 landingOffset = new Vector3(0f, 0f, 3f);  // where it lands, in front of the wall
        [SerializeField] private float fallTime = 0.6f;
        [SerializeField] private float crushRadius = 2.4f;
        [SerializeField] private float crushPlayerDamage = 22f;
        [SerializeField] private int crushEnemyDamage = 14;
        [SerializeField] private float bossGroggy = 2.5f;
        [SerializeField] private float waveScale = 1f;
        [SerializeField] private float wavePlayerDamage = 12f;
        [SerializeField] private int waveEnemyDamage = 8;

        private static readonly Vector2[] Bands = { new Vector2(3f, 4.2f), new Vector2(5.2f, 6.4f), new Vector2(7.4f, 8.6f) };
        private static readonly Color SparkColor = new Color(1f, 0.6f, 0.25f);

        private bool taken;
        private GameObject warning;
        private Quaternion swingRest = Quaternion.identity;
        private float swayPhase;

        public bool Available => !broken && !taken;
        public Vector3 LandingPoint { get { Vector3 p = transform.TransformPoint(landingOffset); p.y = 0f; return p; } }
        public int ChainsLeft => Mathf.CeilToInt(hitsLeft / (float)Mathf.Max(1, hitsPerChain));

        public override string Prompt
        {
            get
            {
                if (!Available) return null;
                int toNext = hitsLeft - (ChainsLeft - 1) * hitsPerChain;
                return ChainsLeft > 1
                    ? $"{DisplayName} — 녹슨 사슬 {ChainsLeft}개 (다음 사슬까지 {toNext}번). 모두 끊으면 무기가 떨어집니다"
                    : $"{DisplayName} — 마지막 사슬! {toNext}번 더 치면 빨간 곳에 떨어집니다 (거인을 유인하세요)";
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (swing != null)
                swingRest = swing.localRotation;
            swayPhase = Random.Range(0f, 10f);
        }

        // Hanging by a thread: the weapon creaks back and forth, more with every snapped chain
        private void Update()
        {
            if (swing == null || !Available)
                return;
            int snapped = (chains != null ? chains.Length : 0) - ChainsLeft;
            float loose = 1f + snapped * 1.4f;
            float t = Time.time * 0.9f + swayPhase;
            swing.localRotation = swingRest * Quaternion.Euler(Mathf.Sin(t * 0.7f) * swayDegrees * 0.5f * loose, 0f, Mathf.Sin(t) * swayDegrees * loose);
        }

        protected override void OnHit(Vector3 from)
        {
            int done = hitsToBreak - hitsLeft;
            int index = (done - 1) / Mathf.Max(1, hitsPerChain);
            Transform chain = chains != null && index < chains.Length ? chains[index] : null;
            if (done % hitsPerChain != 0)
            {
                // Not yet: the chain creaks and throws sparks
                if (chain != null)
                    Shockwave.Spawn(ChainMiddle(chain), 0.8f, SparkColor, 8);
                return;
            }

            if (chain != null)
                StartCoroutine(DropChain(chain));
            // With fewer chains holding it, the weapon hangs crooked
            if (weapon != null)
                weapon.Rotate(transform.forward, index % 2 == 0 ? 6f : -4f, Space.World);
            if (ChainsLeft == 1 && warning == null)
                warning = JOT_Fx.Ring(LandingPoint, 0f, crushRadius);
        }

        protected override void OnBroken(Vector3 from)
        {
            ClearWarning();
            StartCoroutine(Fall());
        }

        private static Vector3 ChainMiddle(Transform chain)
        {
            int n = chain.childCount;
            return n > 0 ? chain.GetChild(n / 2).position : chain.position;
        }

        // A snapped chain drops off the wall
        private IEnumerator DropChain(Transform chain)
        {
            chain.SetParent(null, true);
            Vector3 velocity = Vector3.up * 1.5f + Random.insideUnitSphere;
            for (float t = 0f; t < 1.2f && chain.position.y > 0f; t += Time.deltaTime)
            {
                velocity += Physics.gravity * Time.deltaTime;
                chain.position += velocity * Time.deltaTime;
                chain.Rotate(Random.insideUnitSphere * 400f * Time.deltaTime);
                yield return null;
            }
            Destroy(chain.gameObject);
        }

        private IEnumerator Fall()
        {
            if (weapon == null)
                yield break;
            if (chains != null)
                foreach (Transform chain in chains)
                    if (chain != null && chain.parent != null) // not already falling
                        StartCoroutine(DropChain(chain));
            weapon.SetParent(transform, true); // off the swinging hanger before it drops
            Vector3 startPos = weapon.position;
            Quaternion startRot = weapon.rotation;
            Vector3 endPos = LandingPoint + Vector3.up * 0.4f;
            Quaternion endRot = Quaternion.AngleAxis(85f, transform.right) * startRot; // topples forward, away from the wall
            for (float t = 0f; t < fallTime; t += Time.deltaTime)
            {
                float k = t / fallTime;
                k *= k;
                weapon.SetPositionAndRotation(Vector3.Lerp(startPos, endPos, k), Quaternion.Slerp(startRot, endRot, k));
                yield return null;
            }
            weapon.SetPositionAndRotation(endPos, endRot);

            Vector3 land = LandingPoint;
            var crushed = JOT_Area.Circle(land, crushRadius);
            JOT_Area.HitEnemies(crushed, land, crushEnemyDamage, null, bossGroggy: bossGroggy);
            JOT_Area.HurtPlayer(crushed, crushPlayerDamage);
            JOT_EarthBurst.Spawn(land, 0f, crushRadius, 30, 1.6f);
            CameraShake.Shake(0.6f, 0.45f);
            var bands = new Vector2[Bands.Length];
            for (int i = 0; i < Bands.Length; i++)
                bands[i] = Bands[i] * waveScale;
            JOT_RingWaves.Spawn(land, bands, 0.6f, 0.45f, wavePlayerDamage, waveEnemyDamage, null);

            // Done its damage: it sinks away rather than lie between the player and the guardian
            JOT_SinkAway.Begin(weapon.gameObject, 0.8f, 1.2f);
        }

        // The guardian tears the weapon off the wall and takes it in both hands
        public Transform TakeBy(Transform holder)
        {
            taken = true;
            broken = true;
            Alive.Remove(this);
            ClearWarning();
            if (chains != null)
                foreach (Transform chain in chains)
                    if (chain != null && chain.parent != null)
                        StartCoroutine(DropChain(chain));
            if (weapon == null)
                return null;
            weapon.SetParent(holder, true);
            return weapon;
        }

        private void ClearWarning()
        {
            if (warning != null)
                Destroy(warning);
            warning = null;
        }

        private void OnDestroy() => ClearWarning();
    }
}
