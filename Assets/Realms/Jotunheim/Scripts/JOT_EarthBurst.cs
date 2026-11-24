using System.Collections.Generic;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Something heavy hits the ground: chunks of earth and stone jump out of a ring and tumble back down,
    // and puffs of dust roll up and fade. Used for landing rocks and weapons, falling trees and the troll's slam.
    public class JOT_EarthBurst : MonoBehaviour
    {
        private const float Gravity = -18f;
        private const float Lifetime = 1.8f;

        private static Material debrisMat, dustMat;

        private class Chunk { public Transform t; public Vector3 v; public Vector3 spin; public bool landed; }
        private class Puff { public Transform t; public Renderer r; public float size; public Vector3 drift; }

        private readonly List<Chunk> chunks = new List<Chunk>();
        private readonly List<Puff> puffs = new List<Puff>();
        private MaterialPropertyBlock block;
        private Color dustColor;
        private float age;

        // A burst over a ring band (inner 0 = a full disc); chunks scales the amount of debris
        public static void Spawn(Vector3 center, float inner, float outer, int chunkCount, float size = 1f)
        {
            LoadMaterials();
            var root = new GameObject("JOT_EarthBurst");
            root.transform.position = new Vector3(center.x, 0f, center.z);
            var burst = root.AddComponent<JOT_EarthBurst>();
            burst.block = new MaterialPropertyBlock();
            burst.dustColor = dustMat.HasProperty("_BaseColor") ? dustMat.GetColor("_BaseColor") : new Color(0.6f, 0.5f, 0.38f, 0.5f);

            for (int i = 0; i < chunkCount; i++)
            {
                Vector3 spot = RandomInBand(inner, outer, out Vector3 outward);
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(piece.GetComponent<Collider>());
                piece.name = "Chunk";
                piece.transform.SetParent(root.transform, false);
                piece.transform.localPosition = spot + Vector3.up * 0.1f;
                piece.transform.localRotation = Random.rotation;
                piece.transform.localScale = new Vector3(Random.Range(0.15f, 0.45f), Random.Range(0.12f, 0.35f), Random.Range(0.15f, 0.45f)) * size;
                piece.GetComponent<Renderer>().sharedMaterial = debrisMat;
                burst.chunks.Add(new Chunk
                {
                    t = piece.transform,
                    v = Vector3.up * Random.Range(3.5f, 7f) * Mathf.Sqrt(size) + outward * Random.Range(0.5f, 2.5f),
                    spin = Random.insideUnitSphere * 540f,
                });
            }

            int puffCount = Mathf.Max(3, chunkCount / 2);
            for (int i = 0; i < puffCount; i++)
            {
                Vector3 spot = RandomInBand(inner, outer, out Vector3 outward);
                var cloud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(cloud.GetComponent<Collider>());
                cloud.name = "Dust";
                cloud.transform.SetParent(root.transform, false);
                cloud.transform.localPosition = spot + Vector3.up * 0.3f;
                var r = cloud.GetComponent<Renderer>();
                r.sharedMaterial = dustMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float puffSize = Random.Range(0.9f, 1.6f) * size;
                cloud.transform.localScale = Vector3.one * puffSize * 0.4f;
                burst.puffs.Add(new Puff { t = cloud.transform, r = r, size = puffSize, drift = outward * Random.Range(0.3f, 1f) + Vector3.up * Random.Range(0.4f, 0.9f) });
            }
        }

        private static Vector3 RandomInBand(float inner, float outer, out Vector3 outward)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float radius = Mathf.Sqrt(Random.Range(inner * inner, outer * outer));
            return outward * radius;
        }

        // Materials live in a Resources folder so a build keeps their shader variants (the dust is transparent)
        private static void LoadMaterials()
        {
            if (debrisMat == null)
                debrisMat = Resources.Load<Material>("JOT_EarthDebris") ?? Mats.Get("JOT_fx_debris", new Color(0.42f, 0.36f, 0.3f), 0.1f);
            if (dustMat == null)
                dustMat = Resources.Load<Material>("JOT_EarthDust") ?? Mats.Get("JOT_fx_dust", new Color(0.62f, 0.54f, 0.42f), 0f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float dt = Time.deltaTime;

            foreach (Chunk c in chunks)
            {
                if (c.landed)
                {
                    // Settled bits sink away in the last part of the burst
                    if (age > Lifetime * 0.6f)
                        c.t.localScale *= 1f - dt * 2.5f;
                    continue;
                }
                c.v.y += Gravity * dt;
                c.t.localPosition += c.v * dt;
                c.t.Rotate(c.spin * dt, Space.World);
                if (c.t.position.y < 0.05f && c.v.y < 0f)
                {
                    c.t.position = new Vector3(c.t.position.x, 0.05f, c.t.position.z);
                    c.landed = true;
                }
            }

            float k = Mathf.Clamp01(age / Lifetime);
            foreach (Puff p in puffs)
            {
                p.t.localPosition += p.drift * dt;
                p.t.localScale = Vector3.one * p.size * Mathf.Lerp(0.4f, 1.6f, 1f - (1f - k) * (1f - k));
                p.r.GetPropertyBlock(block);
                Color c = dustColor;
                c.a = dustColor.a * (1f - k);
                block.SetColor("_BaseColor", c);
                p.r.SetPropertyBlock(block);
            }

            if (age >= Lifetime)
                Destroy(gameObject);
        }
    }
}
