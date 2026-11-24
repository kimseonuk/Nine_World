using UnityEngine;

// A frozen, glowing copy of a character's current pose that fades out, left behind along a dodge.
// Skinned meshes are baked in their current pose; plain meshes (the hammer) are copied as they are.
public class Afterimage : MonoBehaviour
{
    public float lifetime = 0.3f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    MeshRenderer[] parts;
    readonly System.Collections.Generic.List<Mesh> bakedMeshes = new System.Collections.Generic.List<Mesh>();
    MaterialPropertyBlock block;
    Color startColor;
    float age;

    // Copies every skinned mesh under `source` (may be null), plus the plain meshes under `carried` (a held weapon).
    public static void Spawn(GameObject source, Material material, Transform carried)
    {
        var root = new GameObject("Afterimage");
        var ghost = root.AddComponent<Afterimage>();
        ghost.startColor = material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;

        var skinned = source != null ? source.GetComponentsInChildren<SkinnedMeshRenderer>() : new SkinnedMeshRenderer[0];
        foreach (var smr in skinned)
        {
            if (!smr.enabled || !smr.gameObject.activeInHierarchy) continue;
            var mesh = new Mesh();
            smr.BakeMesh(mesh, false);   // false keeps the renderer's world scale in the vertices
            ghost.bakedMeshes.Add(mesh);
            AddPart(root.transform, smr.transform, mesh, Vector3.one, material);
        }
        var plain = carried != null ? carried.GetComponentsInChildren<MeshRenderer>() : new MeshRenderer[0];
        foreach (var mr in plain)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (!mr.enabled || mf == null || mf.sharedMesh == null) continue;
            AddPart(root.transform, mr.transform, mf.sharedMesh, mr.transform.lossyScale, material);
        }
        ghost.parts = root.GetComponentsInChildren<MeshRenderer>();
        ghost.block = new MaterialPropertyBlock();
    }

    static void AddPart(Transform root, Transform from, Mesh mesh, Vector3 scale, Material material)
    {
        var part = new GameObject(from.name);
        part.transform.SetParent(root, false);
        part.transform.SetPositionAndRotation(from.position, from.rotation);
        part.transform.localScale = scale;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = part.AddComponent<MeshRenderer>();
        var mats = new Material[mesh.subMeshCount];
        for (int i = 0; i < mats.Length; i++) mats[i] = material;
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = 1f - age / lifetime;
        if (k <= 0f)
        {
            Destroy(gameObject);
            return;
        }
        // the material is additive, so fading the colour toward black fades the ghost out
        block.SetColor(BaseColorId, startColor * k * k);
        foreach (var p in parts) p.SetPropertyBlock(block);
    }

    void OnDestroy()
    {
        // only the meshes baked for this ghost are its own; the hammer's mesh is the imported asset and stays
        foreach (var mesh in bakedMeshes) if (mesh != null) Destroy(mesh);
    }
}
