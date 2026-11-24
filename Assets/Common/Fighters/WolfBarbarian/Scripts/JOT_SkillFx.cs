using System.Collections.Generic;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Skill effects built at runtime (no textures or prefabs): sweeping slash arcs, cut streaks,
    // expanding ground rings, ground cracks, trails, and soft particle bursts (dust, sparks, smoke, flashes).
    // Everything is unlit and see-through (Sprites/Default, which every build includes) and cleans itself up.
    public static class JOT_SkillFx
    {
        private static Material plain;     // vertex colours, no texture
        private static Material soft;      // a round soft blob, for particles and flashes

        public static Material Plain
        {
            get
            {
                if (plain == null)
                    plain = new Material(Shader.Find("Sprites/Default")) { name = "JOT_fx_skill_plain" };
                return plain;
            }
        }

        public static Material Soft
        {
            get
            {
                if (soft == null)
                {
                    const int size = 64;
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "JOT_fx_soft", wrapMode = TextureWrapMode.Clamp };
                    var px = new Color32[size * size];
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                            float d = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                            px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Pow(d, 1.6f) * 255f));
                        }
                    tex.SetPixels32(px);
                    tex.Apply();
                    soft = new Material(Shader.Find("Sprites/Default")) { name = "JOT_fx_skill_soft", mainTexture = tex };
                }
                return soft;
            }
        }

        // Mesh, line and trail vertex colours aren't converted for a linear-space project (they'd come out washed pale)
        public static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        // The floor under a point (ignores bodies); falls back to a little under the point
        public static float GroundY(Vector3 p)
        {
            float best = float.MaxValue, y = p.y - 0.95f;
            foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider is CharacterController || hit.collider.GetComponentInParent<Enemy>() != null
                    || hit.collider.GetComponentInParent<PlayerStats>() != null)
                    continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    y = hit.point.y;
                }
            }
            return y;
        }

        public static Vector3 OnGround(Vector3 p, float lift = 0.06f) => new Vector3(p.x, GroundY(p) + lift, p.z);

        // ---- Slash arcs: a crescent whose bright head sweeps around, its tail fading behind

        // center/rotation give the arc's plane (local y is its axis, local z where angle 0 points).
        // The head sweeps from startAngle by sweep degrees (signed) in sweepSeconds, then the whole thing fades.
        public static JOT_FxSlash Slash(Vector3 center, Quaternion rotation, float radius, float width, float startAngle, float sweep,
            float tail, Color color, float sweepSeconds, float fadeSeconds = 0.18f, Transform follow = null)
        {
            var go = new GameObject("JOT_FxSlash");
            go.transform.SetPositionAndRotation(center, rotation);
            if (follow != null)
                go.transform.SetParent(follow, true);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = Plain;
            var slash = go.AddComponent<JOT_FxSlash>();
            slash.Init(radius, width, startAngle, sweep, tail, color, sweepSeconds, fadeSeconds);
            return slash;
        }

        // A flat slash lying on a plane tilted by `tilt` degrees about the facing direction (0 = horizontal)
        public static JOT_FxSlash TiltedSlash(Vector3 center, Vector3 facing, float tilt, float radius, float width, float sweep, Color color, float seconds)
        {
            facing = Flat(facing);
            if (facing.sqrMagnitude < 0.001f)
                facing = Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(facing) * Quaternion.Euler(0f, 0f, tilt);
            return Slash(center, rot, radius, width, -sweep * 0.5f, sweep, sweep * 0.8f, color, seconds);
        }

        // ---- Cut streaks: a thin bright line through a point that thins out and fades

        public static void Streak(Vector3 from, Vector3 to, Color color, float width, float seconds)
        {
            var go = new GameObject("JOT_FxStreak");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Plain;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.numCapVertices = 2;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
            line.widthMultiplier = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<JOT_FxFade>().InitLine(line, color, seconds);
        }

        // ---- Ground rings: a flat band growing outward from a point and fading

        public static void Ring(Vector3 center, float fromRadius, float toRadius, float width, Color color, float seconds, float delay = 0f)
        {
            var go = new GameObject("JOT_FxRing");
            go.transform.position = OnGround(center, 0.07f);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = Plain;
            go.AddComponent<JOT_FxRing>().Init(fromRadius, toRadius, width, color, seconds, delay);
        }

        // ---- Ground cracks: jagged dark lines out from a point with a hot glow inside, fading after a while

        public static void Cracks(Vector3 center, float radius, int count, Color glow, float seconds)
        {
            Vector3 c = OnGround(center, 0.05f);
            float offset = Random.value * 360f;
            for (int i = 0; i < count; i++)
            {
                float angle = offset + i * 360f / count + Random.Range(-12f, 12f);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                float length = radius * Random.Range(0.55f, 1f);
                const int points = 6;
                var path = new Vector3[points];
                for (int p = 0; p < points; p++)
                {
                    float k = p / (points - 1f);
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * (p == 0 ? 0f : Random.Range(-0.35f, 0.35f));
                    path[p] = c + dir * (0.4f + length * k) + side;
                }
                CrackLine(path, new Color(0.08f, 0.05f, 0.03f, 0.9f), 0.32f, seconds, 0f);
                CrackLine(path, glow, 0.12f, seconds * 0.6f, 0.01f);
            }
        }

        private static void CrackLine(Vector3[] path, Color color, float width, float seconds, float lift)
        {
            var go = new GameObject("JOT_FxCrack");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Plain;
            line.positionCount = path.Length;
            for (int i = 0; i < path.Length; i++)
                line.SetPosition(i, path[i] + Vector3.up * lift);
            line.alignment = LineAlignment.TransformZ;   // lies flat on the ground
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
            line.widthMultiplier = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<JOT_FxFade>().InitLine(line, color, seconds, 0.7f);
        }

        // ---- Trails behind something moving; Stop lets one finish fading on its own

        public static TrailRenderer Trail(Transform follow, Vector3 localOffset, Color color, float width, float seconds)
        {
            var go = new GameObject("JOT_FxTrail");
            go.transform.SetParent(follow, false);
            go.transform.localPosition = localOffset;
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = Plain;
            trail.time = seconds;
            trail.minVertexDistance = 0.08f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = width;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Lin(Color.Lerp(color, Color.white, 0.3f)), 0f), new GradientColorKey(Lin(color), 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = grad;
            return trail;
        }

        public static void Stop(TrailRenderer trail)
        {
            if (trail == null)
                return;
            trail.transform.SetParent(null, true);
            trail.emitting = false;
            Object.Destroy(trail.gameObject, trail.time + 0.05f);
        }

        // ---- Particle bursts

        public struct Puff
        {
            public int count;
            public float speed, size, life, gravity, spread;   // spread: 0 = straight up, 1 = all around
            public Color color;
            public float upward;                                // extra upward velocity
            public bool flat;                                   // only sideways (a ring of dust along the ground)
        }

        public static ParticleSystem Burst(Vector3 at, Puff p)
        {
            var go = new GameObject("JOT_FxBurst");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(p.life * 0.6f, p.life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(p.speed * 0.4f, p.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(p.size * 0.6f, p.size);
            main.startColor = p.color;
            main.gravityModifier = p.gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.maxParticles = Mathf.Max(1, p.count);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)p.count) });

            var shape = ps.shape;
            shape.enabled = true;
            if (p.flat)
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.3f;
                shape.rotation = new Vector3(90f, 0f, 0f);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = Mathf.Lerp(5f, 90f, p.spread);
                shape.radius = 0.2f;
                shape.rotation = new Vector3(-90f, 0f, 0f);   // cone opens upward
            }

            if (p.upward != 0f)
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(0f);
                vel.y = new ParticleSystem.MinMaxCurve(p.upward);
                vel.z = new ParticleSystem.MinMaxCurve(0f);
            }

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fade.color = grad;

            var shrink = ps.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.4f)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Soft;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // Shorthands for the common ones
        public static void Dust(Vector3 at, float scale = 1f, int count = 14) => Burst(OnGround(at, 0.15f), new Puff
        {
            count = count, speed = 2.5f * scale, size = 0.9f * scale, life = 0.7f, gravity = -0.05f, spread = 1f,
            color = new Color(0.62f, 0.55f, 0.45f, 0.55f), flat = true, upward = 0.6f
        });

        public static void Sparks(Vector3 at, Color color, int count = 12, float speed = 7f) => Burst(at, new Puff
        {
            count = count, speed = speed, size = 0.22f, life = 0.35f, gravity = 1.2f, spread = 1f, color = color
        });

        public static void Smoke(Vector3 at, Color color, float scale = 1f, int count = 12) => Burst(at, new Puff
        {
            count = count, speed = 1.6f * scale, size = 1.2f * scale, life = 0.55f, gravity = -0.1f, spread = 1f, color = color
        });

        public static void Flash(Vector3 at, Color color, float size, float seconds = 0.18f) => Burst(at, new Puff
        {
            count = 1, speed = 0f, size = size, life = seconds, color = color, spread = 0f
        });

        // A looping trickle of glowing embers rising around something (a buff); turn it on and off with emission
        public static ParticleSystem Embers(Transform follow, Color color)
        {
            var go = new GameObject("JOT_FxEmbers");
            go.transform.SetParent(follow, false);
            go.transform.localPosition = Vector3.down * 0.8f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var emission = ps.emission;
            emission.rateOverTime = 28f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.8f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // straight up from a ring at the feet
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            fade.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Soft;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        // ---- Afterimages: a see-through copy of a character frozen in its current pose (axes and all), fading out

        public static void Ghost(Transform model, Color color, float seconds)
        {
            if (model == null)
                return;
            var root = new GameObject("JOT_FxGhost");
            var shapes = new List<Renderer>();
            var meshes = new List<UnityEngine.Mesh>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.name.StartsWith("JOT_Fx") || r.name.StartsWith("JOT_WarCry"))
                    continue;
                UnityEngine.Mesh mesh;
                if (r is SkinnedMeshRenderer skin)
                {
                    mesh = new UnityEngine.Mesh { name = "JOT_FxGhostMesh" };
                    skin.BakeMesh(mesh, true);   // the pose right now, in the renderer's unscaled local space
                    meshes.Add(mesh);
                }
                else if (r is MeshRenderer && r.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                    mesh = filter.sharedMesh;
                else
                    continue;

                var piece = new GameObject("JOT_FxGhostPart");
                piece.transform.SetParent(root.transform, false);
                piece.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                piece.transform.localScale = r.transform.lossyScale;
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                var view = piece.AddComponent<MeshRenderer>();
                var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = Plain;
                view.sharedMaterials = mats;
                view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                view.receiveShadows = false;
                shapes.Add(view);
            }
            root.AddComponent<JOT_FxFade>().InitShapes(shapes.ToArray(), color, seconds, meshes.ToArray());
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }

    // The sweeping crescent (see JOT_SkillFx.Slash)
    public class JOT_FxSlash : MonoBehaviour
    {
        private const int Segments = 28;
        private float radius, width, startAngle, sweep, tail, sweepSeconds, fadeSeconds, t;
        private Color color;
        private UnityEngine.Mesh mesh;
        private readonly Vector3[] verts = new Vector3[(Segments + 1) * 3];
        private readonly Color[] colors = new Color[(Segments + 1) * 3];

        // How far the head has got (0..1); set from outside to follow something turning (the spinning hero)
        public float? Progress { get; set; }

        public void Init(float r, float w, float start, float sweepDegrees, float tailDegrees, Color c, float seconds, float fade)
        {
            radius = r;
            width = w;
            startAngle = start;
            sweep = sweepDegrees;
            tail = Mathf.Max(10f, Mathf.Abs(tailDegrees));
            color = JOT_SkillFx.Lin(c);
            sweepSeconds = Mathf.Max(0.01f, seconds);
            fadeSeconds = Mathf.Max(0.01f, fade);
            mesh = new UnityEngine.Mesh { name = "JOT_FxSlashMesh" };
            mesh.MarkDynamic();
            var tris = new int[Segments * 12];
            for (int i = 0, n = 0; i < Segments; i++)
                for (int row = 0; row < 2; row++)
                {
                    int a = i * 3 + row, b = a + 3;
                    tris[n++] = a; tris[n++] = b; tris[n++] = a + 1;
                    tris[n++] = a + 1; tris[n++] = b; tris[n++] = b + 1;
                }
            Build(0f, 1f);
            mesh.triangles = tris;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = Progress ?? 1f - Mathf.Pow(1f - Mathf.Clamp01(t / sweepSeconds), 2f);
            float alpha = t <= sweepSeconds ? 1f : 1f - (t - sweepSeconds) / fadeSeconds;
            if (alpha <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            Build(Mathf.Clamp01(k), alpha);
        }

        private void Build(float k, float alpha)
        {
            float sign = Mathf.Sign(sweep == 0f ? 1f : sweep);
            float head = startAngle + sweep * k;
            float length = Mathf.Min(tail, Mathf.Abs(sweep) * k + 1f);
            Color core = Color.Lerp(color, Color.white, 0.35f);
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;                 // 0 = tail end, 1 = head
                float angle = (head - sign * length * (1f - u)) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                float thick = width * Mathf.Lerp(0.25f, 1f, u);  // thin tail, full head
                float a = Mathf.Pow(u, 1.4f) * alpha;
                verts[i * 3] = dir * (radius - thick);
                verts[i * 3 + 1] = dir * (radius - thick * 0.25f);
                verts[i * 3 + 2] = dir * radius;
                colors[i * 3] = new Color(color.r, color.g, color.b, 0f);
                colors[i * 3 + 1] = new Color(core.r, core.g, core.b, a);
                colors[i * 3 + 2] = new Color(color.r, color.g, color.b, a * 0.55f);
            }
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }

    // The expanding ground band (see JOT_SkillFx.Ring)
    public class JOT_FxRing : MonoBehaviour
    {
        private const int Segments = 48;
        private float from, to, width, seconds, delay, t;
        private Color color;
        private UnityEngine.Mesh mesh;
        private MeshRenderer view;
        private readonly Vector3[] verts = new Vector3[(Segments + 1) * 2];
        private readonly Color[] colors = new Color[(Segments + 1) * 2];

        public void Init(float fromRadius, float toRadius, float bandWidth, Color c, float duration, float wait)
        {
            from = fromRadius;
            to = toRadius;
            width = bandWidth;
            color = JOT_SkillFx.Lin(c);
            seconds = Mathf.Max(0.01f, duration);
            delay = wait;
            mesh = new UnityEngine.Mesh { name = "JOT_FxRingMesh" };
            mesh.MarkDynamic();
            var tris = new int[Segments * 6];
            for (int i = 0, n = 0; i < Segments; i++)
            {
                int a = i * 2, b = a + 2;
                tris[n++] = a; tris[n++] = b; tris[n++] = a + 1;
                tris[n++] = a + 1; tris[n++] = b; tris[n++] = b + 1;
            }
            Build(0f);
            mesh.triangles = tris;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            view = GetComponent<MeshRenderer>();
            view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.enabled = delay <= 0f;
        }

        private void Update()
        {
            t += Time.deltaTime;
            if (t < delay)
                return;
            view.enabled = true;
            float k = Mathf.Clamp01((t - delay) / seconds);
            Build(k);
            if (k >= 1f)
                Destroy(gameObject);
        }

        private void Build(float k)
        {
            float r = Mathf.Lerp(from, to, 1f - (1f - k) * (1f - k));
            float w = width * (1f - k * 0.6f);
            float a = color.a * (1f - k * k);
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                verts[i * 2] = dir * Mathf.Max(0f, r - w);
                verts[i * 2 + 1] = dir * r;
                colors[i * 2] = new Color(color.r, color.g, color.b, 0f);
                colors[i * 2 + 1] = new Color(color.r, color.g, color.b, a);
            }
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }

    // Fades a line (or anything with a renderer colour) out and removes it
    public class JOT_FxFade : MonoBehaviour
    {
        private LineRenderer line;
        private Renderer[] shapes;
        private UnityEngine.Mesh[] owned;
        private Color color;
        private float seconds, hold, t;
        private MaterialPropertyBlock block;

        // hold: the share of the time it stays fully visible before fading
        public void InitLine(LineRenderer l, Color c, float duration, float holdShare = 0f)
        {
            line = l;
            color = JOT_SkillFx.Lin(c);
            seconds = Mathf.Max(0.01f, duration);
            hold = holdShare;
            Apply(1f);
        }

        public void InitShape(Renderer r, Color c, float duration) => InitShapes(new[] { r }, c, duration, null);

        // Several renderers fading together; owned meshes (baked poses) are freed with it
        public void InitShapes(Renderer[] r, Color c, float duration, UnityEngine.Mesh[] meshes)
        {
            shapes = r;
            owned = meshes;
            color = c;
            seconds = Mathf.Max(0.01f, duration);
            block = new MaterialPropertyBlock();
            Apply(1f);
        }

        private void OnDestroy()
        {
            if (owned != null)
                foreach (UnityEngine.Mesh m in owned)
                    if (m != null) Destroy(m);
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            Apply(k <= hold ? 1f : 1f - (k - hold) / Mathf.Max(0.001f, 1f - hold));
            if (k >= 1f)
                Destroy(gameObject);
        }

        private void Apply(float a)
        {
            var c = new Color(color.r, color.g, color.b, color.a * a);
            if (line != null)
            {
                line.startColor = c;
                line.endColor = c;
            }
            if (shapes != null)
            {
                block.SetColor("_Color", c);
                foreach (Renderer s in shapes)
                    if (s != null) s.SetPropertyBlock(block);
            }
        }
    }
}
