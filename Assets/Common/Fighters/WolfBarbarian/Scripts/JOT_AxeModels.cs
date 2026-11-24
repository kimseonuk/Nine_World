using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // The axe models in the wolf barbarian's hands, put on by the axe skill sets while that weapon is held.
    //  - Greataxe: one huge axe in the right hand.
    //  - Twin axes: a hand axe in each hand, their pommel rings joined by a sagging chain. The left one is the one
    //    thrown on a chain (E): while it is out, it is hidden in the hand and the skill draws the chain to it.
    // The barbarian model has two small axes baked into its mesh: while this is on, its mesh is swapped for a copy
    // with them cut out (Resources/JOT_Weapons/JOT_WolfBarbarian_NoAxes), and put back when it goes.
    public class JOT_AxeModels : MonoBehaviour
    {
        public const string GreataxeName = "JOT_Weapon_Greataxe", TwinAxeName = "JOT_Weapon_TwinAxe";
        public const float GreataxeScale = 0.85f;   // the model is 2 m long: about 1.7 m in hand
        public const float TwinAxeScale = 0.8f;     // 0.95 m with its hanging chain: about 0.75 m
        private const float RingBelowGrip = 0.07f;  // the twin axe's pommel ring, below the grip (model metres)
        private const string ResFolder = "JOT_Weapons/";

        private bool twin;
        private Transform right, left;
        private LineRenderer chain;
        private SkinnedMeshRenderer body;
        private UnityEngine.Mesh originalMesh;
        private bool leftThrown;

        // The greataxe follows the fist: carried upright past the shoulder while standing or moving, and turned along
        // the fist's own handle axis (where the barbarian's baked-in axe was) while an attack animation plays.
        // Measured from that baked-in axe, in the RightHand bone's space: the fist's centre, the handle along +x
        // (the end that rises in the chop), the blade toward +y.
        private static readonly Vector3 FistRight = new Vector3(-0.009f, 0.031f, 0.089f);
        private static readonly Vector3 FistLeft = new Vector3(0.008f, 0.028f, 0.087f);
        private const float GreatGripDown = 0.11f;   // the grip sits this far up the handle (model metres), the pommel below the fist
        private Animator animator;
        private float swingWeight;                   // 0 = carried, 1 = along the fist (attacking)

        public Transform Right => right;
        public Transform Left => left;

        public static JOT_AxeModels Put(GameObject player, bool twinAxes)
        {
            Remove(player);
            JOT_AxeModels m = player.AddComponent<JOT_AxeModels>();
            m.twin = twinAxes;
            m.Build();
            return m;
        }

        public static void Remove(GameObject player)
        {
            if (player == null)
                return;
            // (torn down now, not at the end of the frame: a new set may go on in the same frame)
            foreach (JOT_AxeModels m in player.GetComponents<JOT_AxeModels>())
            {
                m.Teardown();
                Destroy(m);
            }
        }

        // Takes off just this set (a weapon being put away must not take off the next weapon's set)
        public void Drop()
        {
            Teardown();
            Destroy(this);
        }

        // A loose copy of an axe (thrown): its head turned to +x, its handle along +y, like the old primitive axe
        public static Transform Loose(bool twinAxe)
        {
            var root = new GameObject("JOT_ThrownAxe").transform;
            GameObject prefab = Resources.Load<GameObject>(ResFolder + (twinAxe ? TwinAxeName : GreataxeName));
            if (prefab != null)
            {
                Transform model = Instantiate(prefab, root).transform;
                model.localRotation = Quaternion.Euler(0f, 180f, 0f);
                model.localPosition = Vector3.down * 0.15f;
                model.localScale = Vector3.one * (twinAxe ? TwinAxeScale : GreataxeScale) * 1.15f;
            }
            return root;
        }

        // The pommel ring of a held twin axe (or the hand), where a chain is fastened
        public Vector3 RingOf(Transform axe)
        {
            if (axe == null)
                return transform.position + Vector3.up * 0.6f;
            return axe.TransformPoint(Vector3.down * RingBelowGrip);
        }

        public void SetLeftThrown(bool thrown)
        {
            leftThrown = thrown;
            if (left != null)
                left.gameObject.SetActive(!thrown);
        }

        private void Build()
        {
            body = FindBody();
            if (body != null)
            {
                UnityEngine.Mesh noAxes = Resources.Load<UnityEngine.Mesh>(ResFolder + "JOT_WolfBarbarian_NoAxes");
                if (noAxes != null && body.sharedMesh != null && body.sharedMesh.vertexCount == noAxes.vertexCount)
                {
                    originalMesh = body.sharedMesh;
                    body.sharedMesh = noAxes;
                }
            }
            right = Hold(false, twin ? TwinAxeName : GreataxeName, twin ? TwinAxeScale : GreataxeScale);
            if (twin)
                left = Hold(true, TwinAxeName, TwinAxeScale);
            if (twin && right != null && left != null)
            {
                var go = new GameObject("JOT_AxeChain");
                go.transform.SetParent(transform, false);
                chain = go.AddComponent<LineRenderer>();
                chain.sharedMaterial = ChainMaterial();
                SetupChain(chain);
                chain.positionCount = 12;
                chain.numCapVertices = 0;
                chain.alignment = LineAlignment.View;
            }
        }

        // A chain line: the link texture (4:1, two links) tiled along it
        public static void SetupChain(LineRenderer line)
        {
            line.sharedMaterial = ChainMaterial();
            line.textureMode = LineTextureMode.Tile;
            line.textureScale = new Vector2(0.25f, 1f);
            line.widthMultiplier = 0.1f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public static Material ChainMaterial()
        {
            Material m = Resources.Load<Material>(ResFolder + "JOT_Chain_Mat");
            return m != null ? m : Mats.Get("JOT_fx_chain", new Color(0.5f, 0.5f, 0.55f), 0.6f, new Color(0.2f, 0.3f, 0.4f));
        }

        // The skinned body whose skeleton has the hands
        private SkinnedMeshRenderer FindBody()
        {
            SkinnedMeshRenderer best = null;
            foreach (SkinnedMeshRenderer s in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (s.sharedMesh != null && HandIndex(s, false) >= 0 && (best == null || s.gameObject.activeInHierarchy))
                    best = s;
            return best;
        }

        private static int HandIndex(SkinnedMeshRenderer s, bool leftHand)
        {
            Transform[] bones = s.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                string n = bones[i].name.ToLowerInvariant().Replace(" ", "");
                if (leftHand ? (n.EndsWith("lefthand") || n.EndsWith("hand_l") || n.EndsWith("hand.l"))
                             : (n.EndsWith("righthand") || n.EndsWith("hand_r") || n.EndsWith("hand.r")))
                    return i;
            }
            return -1;
        }

        // Puts an axe in a fist: the handle through the fist pointing forward (as the baked-in axes were),
        // the blade hanging down, worked out from the skin's bind pose so it follows every animation
        private Transform Hold(bool leftHand, string prefabName, float scale)
        {
            GameObject prefab = Resources.Load<GameObject>(ResFolder + prefabName);
            if (prefab == null || body == null)
                return null;
            int hi = HandIndex(body, leftHand);
            if (hi < 0)
                return null;
            Transform hand = body.bones[hi];
            Matrix4x4 bind = body.sharedMesh.bindposes[hi];          // mesh space -> hand space
            Vector3 wrist = bind.inverse.MultiplyPoint3x4(Vector3.zero);
            // (in the mesh's own space: z up, the model faces -y; the fist sits just outside the wrist)
            Vector3 fist = bind.MultiplyPoint3x4(new Vector3(wrist.x * 1.12f, 0f, wrist.z - 0.012f));
            Vector3 forward = bind.MultiplyVector(new Vector3(0f, -1f, 0f)).normalized;
            Vector3 down = bind.MultiplyVector(new Vector3(0f, 0f, -1f)).normalized;
            float handScale = Mathf.Max(0.0001f, hand.lossyScale.x);

            Transform axe = Instantiate(prefab, hand).transform;
            axe.name = prefabName + (leftHand ? "_L" : "_R");
            axe.localScale = Vector3.one * scale / handScale;
            if (prefabName == GreataxeName)
            {
                animator = body.GetComponentInParent<Animator>();
                swingWeight = 0f;
                PoseGreataxe(axe, leftHand);
                return axe;
            }
            // How it is carried, set from the hand as it is now (standing idle): the greataxe upright, leaning back
            // past the shoulder with its edge to the front; a hand axe pointing ahead, edge down.
            // (model: handle +y with the grip at 0, blade toward -x)
            Transform root = transform;
            Vector3 outward = leftHand ? -root.right : root.right;
            bool great = prefabName == GreataxeName;
            Vector3 haft = great ? (root.up * 0.92f - root.forward * 0.3f + outward * 0.12f).normalized
                                 : (root.forward * 0.95f - root.up * 0.25f + outward * 0.1f).normalized;
            Vector3 edge = great ? root.forward : -root.up;
            edge = Vector3.ProjectOnPlane(edge, haft).normalized;
            axe.rotation = Quaternion.LookRotation(Vector3.Cross(-edge, haft), haft);
            // the grip sits a little up the handle from the pivot, so the fist closes over it
            Vector3 haftLocal = hand.InverseTransformDirection(haft);
            axe.localPosition = fist - haftLocal * (0.05f * scale / handScale) * (great ? 2.2f : 1f);
            return axe;
        }

        // Is an attack animation playing (or being blended into)?
        private bool Attacking()
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                return false;
            AnimatorStateInfo now = animator.GetCurrentAnimatorStateInfo(0);
            if (!now.IsName("Locomotion"))
                return true;
            return animator.IsInTransition(0) && !animator.GetNextAnimatorStateInfo(0).IsName("Locomotion");
        }

        // The greataxe in the fist: the grip at the fist's centre, turned between the carry and the swing
        private void PoseGreataxe(Transform axe, bool leftHand)
        {
            Transform hand = axe.parent;
            if (hand == null)
                return;
            // Along the fist (the attack animations were made for an axe held this way)
            Vector3 swingHaft = hand.TransformDirection(leftHand ? Vector3.left : Vector3.right);
            Vector3 swingEdge = hand.TransformDirection(Vector3.up);
            // Carried: upright, leaning back past the shoulder, edge to the front
            Transform root = transform;
            Vector3 outward = leftHand ? -root.right : root.right;
            Vector3 carryHaft = (root.up * 0.9f - root.forward * 0.38f + outward * 0.14f).normalized;
            Vector3 carryEdge = root.forward;
            Quaternion swing = Quaternion.LookRotation(Vector3.Cross(-Vector3.ProjectOnPlane(swingEdge, swingHaft).normalized, swingHaft), swingHaft);
            Quaternion carry = Quaternion.LookRotation(Vector3.Cross(-Vector3.ProjectOnPlane(carryEdge, carryHaft).normalized, carryHaft), carryHaft);
            float w = swingWeight * swingWeight * (3f - 2f * swingWeight);
            axe.rotation = Quaternion.Slerp(carry, swing, w);
            Vector3 haftLocal = hand.InverseTransformDirection(axe.up);
            axe.localPosition = (leftHand ? FistLeft : FistRight) - haftLocal * GreatGripDown * axe.localScale.y;
        }

        private void LateUpdate()
        {
            if (!twin && right != null)
            {
                // quick into a swing, a little slower back onto the shoulder
                float target = Attacking() ? 1f : 0f;
                swingWeight = Mathf.MoveTowards(swingWeight, target, Time.deltaTime * (target > swingWeight ? 9f : 4f));
                PoseGreataxe(right, false);
            }
            if (chain == null)
                return;
            bool show = !leftThrown && right != null && left != null && right.gameObject.activeInHierarchy && left.gameObject.activeInHierarchy;
            chain.enabled = show;
            if (!show)
                return;
            Vector3 a = RingOf(right), b = RingOf(left);
            float sag = Mathf.Clamp(Vector3.Distance(a, b) * 0.22f, 0.06f, 0.3f);
            for (int i = 0; i < chain.positionCount; i++)
            {
                float u = i / (float)(chain.positionCount - 1);
                // (hangs in front of the thighs, not through them)
                float s = Mathf.Sin(u * Mathf.PI);
                chain.SetPosition(i, Vector3.Lerp(a, b, u) + Vector3.down * s * sag + transform.forward * s * 0.32f);
            }
        }

        private void OnDestroy() => Teardown();

        private void Teardown()
        {
            if (body != null && originalMesh != null)
                body.sharedMesh = originalMesh;
            originalMesh = null;
            if (right != null) Destroy(right.gameObject);
            if (left != null) Destroy(left.gameObject);
            if (chain != null) Destroy(chain.gameObject);
            right = left = null;
            chain = null;
        }
    }
}
