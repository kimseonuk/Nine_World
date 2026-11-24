using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Ground warnings and attack effects: circles, ring bands, arcs, straight strips and beams.
    // A warning is a see-through dark-red patch with a glowing rim; given its wind-up time, a brighter fill sweeps across
    // it (centre outward, inner edge outward, along the strip) and is full the moment the blow lands, its rim pulsing
    // faster as it nears and flaring white-hot at the end. Plain warnings carry no pattern (they must read at a glance);
    // a boss's big blows can lay a decal over theirs (CrackDecal: the ground splitting), and a safe spot carries the
    // protection rune ᛉ (SafeWardDecal). The rune circle (JOT_RuneCircle) is only the boss arena's floor.
    public static class JOT_Fx
    {
        public const string CrackDecal = "JOT_Fx/JOT_CrackDecal";
        public const string SafeWardDecal = "JOT_Fx/JOT_SafeWard";

        public static readonly Color Danger = new Color(1f, 0.32f, 0.1f);
        private static readonly Color BaseEdge = new Color(0.45f, 0.04f, 0.02f, 0.6f);
        private static readonly Color BaseMiddle = new Color(0.3f, 0.02f, 0.01f, 0.32f);

        private const float RimWidth = 0.09f;
        private const int Segments = 56;

        // (glow.png sampled at its centre is solid white: the meshes' vertex colours do all the work)
        private static Material Fade => Vfx.Material("glow", additive: false);
        private static Material Add => Vfx.Material("glow", additive: true);

        // A filled circle (decal: a Resources texture laid over it, e.g. CrackDecal for a boss's big blow)
        public static GameObject Circle(Vector3 center, float radius, float fillSeconds = 0f, string decal = null) =>
            Ring(center, 0f, radius, fillSeconds, decal);

        // A ring band (inner to outer radius around a point); inner 0 is a whole circle
        public static GameObject Ring(Vector3 center, float inner, float outer, float fillSeconds = 0f, string decal = null)
        {
            GameObject root = Root("JOT_RingTelegraph", center, Vector3.forward);
            var warn = root.AddComponent<JOT_Warning>();
            Layer(root, "Base", Sector(inner, outer, 360f, BaseMiddle, BaseEdge), Fade, 0f);
            Mesh rim = new Mesh();
            Append(rim, Sector(Mathf.Max(0f, outer - RimWidth * 1.4f), outer, 360f, Color.white, Color.white));
            if (inner > 0.05f)
                Append(rim, Sector(inner, inner + RimWidth, 360f, Color.white, Color.white));
            warn.rim = Layer(root, "Rim", rim, Add, 0.02f);
            if (decal != null)
            {
                Texture2D tex = Resources.Load<Texture2D>(decal);
                if (tex != null)
                    warn.runes = Decal(root, tex, outer * 2.05f);
            }
            if (fillSeconds > 0f)
            {
                warn.fill = Layer(root, "Fill", new Mesh(), Fade, 0.01f);
                warn.InitRadial(inner, outer, 360f, fillSeconds);
            }
            warn.Begin(fillSeconds);
            return root;
        }

        public static readonly Color SafeColor = new Color(0.3f, 1f, 0.85f);

        // A safe spot inside a wider attack: a teal circle with the protection rune ᛉ, drawn just above the danger
        public static GameObject Safe(Vector3 center, float radius, float seconds)
        {
            GameObject root = Root("JOT_SafeSpot", center + Vector3.up * 0.03f, Vector3.forward);
            var warn = root.AddComponent<JOT_Warning>();
            warn.tint = SafeColor;
            warn.safe = true;
            warn.spin = 0f;
            Layer(root, "Base", Sector(0f, radius, 360f, new Color(0.02f, 0.12f, 0.1f, 0.75f), new Color(0.05f, 0.35f, 0.3f, 0.8f)), Fade, 0f);
            warn.rim = Layer(root, "Rim", Sector(radius - RimWidth * 1.6f, radius, 360f, Color.white, Color.white), Add, 0.02f);
            Texture2D ward = Resources.Load<Texture2D>(SafeWardDecal);
            if (ward != null)
                warn.runes = Decal(root, ward, radius * 2.05f);
            warn.Begin(seconds);
            Object.Destroy(root, seconds + 0.3f);
            return root;
        }

        // A lasting glowing ring on the ground in any colour, breathing gently (a gimmick's "use me" mark).
        // It stays until destroyed; switch it on and off with SetActive.
        public static GameObject Mark(Vector3 center, float inner, float outer, Color color)
        {
            GameObject root = Root("JOT_Mark", center, Vector3.forward);
            var warn = root.AddComponent<JOT_Warning>();
            warn.tint = color;
            warn.safe = true;
            Color soft = color * 0.5f;
            soft.a = 0.25f;
            Color clear = soft;
            clear.a = 0f;
            Layer(root, "Base", Sector(inner, outer, 360f, clear, soft), Fade, 0f);
            warn.rim = Layer(root, "Rim", Sector(outer - RimWidth * 1.6f, outer, 360f, Color.white, Color.white), Add, 0.02f);
            warn.Begin(0f);
            return root;
        }

        // A pie slice in front of a point
        public static GameObject Fan(Vector3 origin, Vector3 forward, float radius, float arcDegrees, float fillSeconds = 0f)
        {
            GameObject root = Root("JOT_FanTelegraph", origin, forward);
            var warn = root.AddComponent<JOT_Warning>();
            Layer(root, "Base", Sector(0f, radius, arcDegrees, BaseMiddle, BaseEdge), Fade, 0f);
            Mesh rim = Sector(radius - RimWidth * 1.4f, radius, arcDegrees, Color.white, Color.white);
            float half = arcDegrees / 2f * Mathf.Deg2Rad;
            foreach (float a in new[] { -half, half })
            {
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Append(rim, Strip(Vector3.zero, dir, radius, RimWidth, Color.white, Color.white));
            }
            warn.rim = Layer(root, "Rim", rim, Add, 0.02f);
            if (fillSeconds > 0f)
            {
                warn.fill = Layer(root, "Fill", new Mesh(), Fade, 0.01f);
                warn.InitRadial(0f, radius, arcDegrees, fillSeconds);
            }
            warn.Begin(fillSeconds);
            return root;
        }

        // A straight strip on the ground from a point along a direction. fillDelay: the fill only starts sweeping after
        // that long (a run of blows down the strip, the first landing at fillDelay and the last at fillDelay + fillSeconds)
        public static GameObject Line(Vector3 from, Vector3 direction, float length, float width, float fillSeconds = 0f, float fillDelay = 0f)
        {
            GameObject root = Root("JOT_LineTelegraph", from, direction);
            var warn = root.AddComponent<JOT_Warning>();
            warn.delay = fillDelay;
            Layer(root, "Base", Strip(Vector3.zero, Vector3.forward, length, width, BaseMiddle, BaseEdge), Fade, 0f);
            Mesh rim = new Mesh();
            float side = width / 2f - RimWidth / 2f;
            Append(rim, Strip(Vector3.right * side, Vector3.forward, length, RimWidth, Color.white, Color.white));
            Append(rim, Strip(Vector3.left * side, Vector3.forward, length, RimWidth, Color.white, Color.white));
            Append(rim, Strip(new Vector3(-width / 2f, 0f, length - RimWidth * 1.4f), Vector3.right, width, RimWidth * 1.4f, Color.white, Color.white, alongCenter: false));
            warn.rim = Layer(root, "Rim", rim, Add, 0.02f);
            if (fillSeconds > 0f)
            {
                warn.fill = Layer(root, "Fill", new Mesh(), Fade, 0.01f);
                warn.InitStrip(length, width - RimWidth * 2f, fillSeconds);
            }
            warn.Begin(fillSeconds);
            return root;
        }

        // A short-lived glowing beam between two points
        public static void Beam(Vector3 from, Vector3 to, float width, Color color)
        {
            var root = new GameObject("JOT_Beam");
            root.transform.position = (from + to) / 2f;
            root.transform.rotation = Quaternion.LookRotation(to - from);
            Mats.Shape(PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(width, width, Vector3.Distance(from, to)),
                Mats.Get("JOT_fx_beam_" + ColorUtility.ToHtmlStringRGB(color), color, 0.8f, color * 3f));
            Object.Destroy(root, 0.25f);
        }

        // ── Building blocks ──

        private static GameObject Root(string name, Vector3 at, Vector3 forward)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(new Vector3(at.x, at.y + 0.04f, at.z), Quaternion.LookRotation(Flat(forward)));
            return root;
        }

        private static MeshRenderer Layer(GameObject root, string name, Mesh mesh, Material material, float lift)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.up * lift;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static MeshRenderer Decal(GameObject root, Texture2D texture, float size)
        {
            var go = new GameObject("Runes");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.up * 0.03f;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = RuneMaterial(texture);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static readonly System.Collections.Generic.Dictionary<Texture2D, Material> decalMats =
            new System.Collections.Generic.Dictionary<Texture2D, Material>();
        private static Material RuneMaterial(Texture2D texture)
        {
            if (decalMats.TryGetValue(texture, out Material cached) && cached != null)
                return cached;
            var mat = new Material(Vfx.Material("glow", additive: true)) { name = "JOT_fx_" + texture.name };
            mat.SetTexture("_BaseMap", texture);
            decalMats[texture] = mat;
            return mat;
        }

        // A band between two radii over an arc centred on +z: two vertices per step, inner colour inside, outer outside
        internal static Mesh Sector(float inner, float outer, float arcDegrees, Color innerColor, Color outerColor)
        {
            int steps = Mathf.Max(6, Mathf.CeilToInt(Segments * arcDegrees / 360f));
            var v = new Vector3[(steps + 1) * 2];
            var c = new Color[v.Length];
            var uv = new Vector2[v.Length];
            var t = new int[steps * 6];
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Deg2Rad * (-arcDegrees / 2f + arcDegrees * i / steps);
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v[i * 2] = dir * inner;
                v[i * 2 + 1] = dir * outer;
                c[i * 2] = innerColor;
                c[i * 2 + 1] = outerColor;
                uv[i * 2] = uv[i * 2 + 1] = new Vector2(0.5f, 0.5f);
            }
            for (int i = 0; i < steps; i++)
            {
                int a = i * 2;
                t[i * 6] = a; t[i * 6 + 1] = a + 1; t[i * 6 + 2] = a + 2;
                t[i * 6 + 3] = a + 2; t[i * 6 + 4] = a + 1; t[i * 6 + 5] = a + 3;
            }
            var mesh = new Mesh { name = "JOT_Sector", vertices = v, colors = c, uv = uv, triangles = t };
            mesh.RecalculateBounds();
            return mesh;
        }

        // A straight strip of the given width: centred on the line (edges coloured edgeColor, middle midColor),
        // or with alongCenter false running from start to the side (one colour)
        internal static Mesh Strip(Vector3 start, Vector3 direction, float length, float width, Color midColor, Color edgeColor, bool alongCenter = true)
        {
            Vector3 dir = Flat(direction).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 end = start + dir * length;
            Vector3[] v;
            Color[] c;
            int[] t;
            if (alongCenter)
            {
                // Three lengthwise lines: left edge, middle, right edge
                v = new[] { start - side * width / 2f, start, start + side * width / 2f, end - side * width / 2f, end, end + side * width / 2f };
                c = new[] { edgeColor, midColor, edgeColor, edgeColor, midColor, edgeColor };
                t = new[] { 0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5 };
            }
            else
            {
                // A box from start along direction, width across it toward +forward
                Vector3 across = Vector3.forward * width;
                v = new[] { start, start + across, end, end + across };
                c = new[] { midColor, midColor, midColor, midColor };
                t = new[] { 0, 1, 2, 2, 1, 3 };
            }
            var uv = new Vector2[v.Length];
            for (int i = 0; i < uv.Length; i++)
                uv[i] = new Vector2(0.5f, 0.5f);
            var mesh = new Mesh { name = "JOT_Strip", vertices = v, colors = c, uv = uv, triangles = t };
            mesh.RecalculateBounds();
            return mesh;
        }

        // Adds another mesh's triangles into this one
        private static void Append(Mesh into, Mesh add)
        {
            var v = new System.Collections.Generic.List<Vector3>(into.vertices);
            var c = new System.Collections.Generic.List<Color>(into.colors);
            var uv = new System.Collections.Generic.List<Vector2>(into.uv);
            var t = new System.Collections.Generic.List<int>(into.triangles);
            int offset = v.Count;
            v.AddRange(add.vertices);
            c.AddRange(add.colors);
            uv.AddRange(add.uv);
            foreach (int i in add.triangles)
                t.Add(i + offset);
            into.Clear();
            into.SetVertices(v);
            into.SetColors(c);
            into.SetUVs(0, uv);
            into.SetTriangles(t, 0);
            into.RecalculateBounds();
            Object.Destroy(add);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v : Vector3.forward;
        }
    }

    // Drives a JOT_Fx warning: the sweeping fill, the rim's quickening pulse and white-hot flare at the end, the runes' turn
    public class JOT_Warning : MonoBehaviour
    {
        internal MeshRenderer rim, fill, runes;
        internal Color tint = JOT_Fx.Danger;
        internal bool safe;   // a safe spot: a steady glow, no white-hot flare
        internal float spin;  // degrees a second the decal turns (0: still)
        internal float delay; // a strip that sweeps late: the fill starts this long after the warning appears
        private MaterialPropertyBlock block;
        private float seconds, age;
        private bool radial;
        private float inner, outer, arc, length, width;
        private Mesh fillMesh;

        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        // Its shape, and whether its blow is landing now (the fill ran its course): the enemy lays the hit's effect then
        internal bool IsRadial => radial;
        internal float Inner => inner;
        internal float Outer => outer;
        internal float Arc => arc;
        internal float Length => length;
        internal bool Landed => !safe && seconds > 0f && age >= delay + seconds - 0.2f;

        internal void InitRadial(float innerRadius, float outerRadius, float arcDegrees, float duration)
        {
            radial = true;
            inner = innerRadius;
            outer = outerRadius;
            arc = arcDegrees;
            seconds = duration;
        }

        internal void InitStrip(float stripLength, float stripWidth, float duration)
        {
            radial = false;
            length = stripLength;
            width = stripWidth;
            seconds = duration;
        }

        internal void Begin(float duration)
        {
            seconds = duration;
            block = new MaterialPropertyBlock();
            if (fill != null)
            {
                fillMesh = fill.GetComponent<MeshFilter>().sharedMesh;
                fillMesh.MarkDynamic();
            }
            Apply();
        }

        private void Update()
        {
            age += Time.deltaTime;
            Apply();
        }

        private void Apply()
        {
            // 0..1 toward the blow (a warning with no time just breathes). With a delay the rim counts down to the first
            // blow (the delay) and stays white-hot while the fill sweeps on after it.
            float k = seconds > 0f ? Mathf.Clamp01(delay > 0f ? age / delay : age / seconds) : 0.35f;
            float kFill = seconds > 0f ? Mathf.Clamp01((age - delay) / seconds) : 0f;

            // The rim pulses faster as the blow nears, and flares toward white in the last moment
            float rate = Mathf.Lerp(3f, 14f, k * k);
            float pulse = 0.65f + 0.35f * Mathf.Sin(age * rate * Mathf.PI);
            float flare = seconds > 0f && !safe ? Mathf.Clamp01((k - 0.85f) / 0.15f) : 0f;
            if (safe)
                pulse = 0.8f + 0.2f * Mathf.Sin(age * 6f);
            Color rimColor = Color.Lerp(tint * (1.4f * pulse), new Color(1f, 0.9f, 0.75f) * 2f, flare);
            rimColor.a = 1f;
            Tint(rim, rimColor);

            if (runes != null)
            {
                runes.transform.localRotation = Quaternion.Euler(90f, age * spin, 0f);
                Color c = tint * (safe ? 1.6f : 1.1f + 0.8f * k + 0.6f * flare);
                c.a = Mathf.Clamp01(age / 0.25f);
                Tint(runes, c);
            }

            if (fill != null && fillMesh != null)
            {
                float eased = kFill;   // a steady sweep: full exactly when the (last) blow lands
                // (alpha-blended, so it reads as red on a bright floor as well as a dark one)
                Color back = new Color(0.85f, 0.12f, 0.04f, 0.3f);
                Color front = Color.Lerp(new Color(1f, 0.3f, 0.08f, 0.7f), new Color(1f, 0.6f, 0.3f, 0.85f), flare);
                Mesh m = radial
                    ? JOT_Fx.Sector(inner, Mathf.Lerp(inner, outer, eased), arc, back, front)
                    : JOT_Fx.Strip(Vector3.zero, Vector3.forward, Mathf.Max(0.01f, length * eased), width, front, front);
                if (!radial)
                {
                    // Along a strip the bright part is the leading end: recolour by distance
                    var colors = m.colors;
                    var verts = m.vertices;
                    for (int i = 0; i < colors.Length; i++)
                        colors[i] = Color.Lerp(back, front, length * eased > 0.01f ? verts[i].z / (length * eased) : 1f);
                    m.colors = colors;
                }
                fillMesh.Clear();
                fillMesh.SetVertices(m.vertices);
                fillMesh.SetColors(m.colors);
                fillMesh.SetUVs(0, m.uv);
                fillMesh.SetTriangles(m.triangles, 0);
                fillMesh.RecalculateBounds();
                Destroy(m);
            }
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null)
                return;
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }

        private void OnDestroy()
        {
            foreach (MeshFilter f in GetComponentsInChildren<MeshFilter>())
                if (f != null && f.sharedMesh != null && f.sharedMesh != Vfx.QuadMesh)
                    Destroy(f.sharedMesh);
        }
    }
}
