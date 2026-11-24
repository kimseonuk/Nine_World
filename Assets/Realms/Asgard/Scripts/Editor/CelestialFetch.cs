using System.IO;
using System.Net;
using UnityEditor;
using UnityEngine;

// Editor helper: brings the celestial start hall's VARCO pieces into the project. Each model is downloaded to
// Assets/Realms/Asgard/Models/Celestial/<name>/<name>.glb and wrapped as Resources/Celestial/<name>.prefab
// (base centred on the origin, 1 unit tall, a box collider round it); the floor painting goes straight to Resources.
public static class CelestialFetch
{
    public const string ModelFolder = "Assets/Realms/Asgard/Models/Celestial";
    public const string PrefabFolder = "Assets/Realms/Asgard/Resources/Celestial";

    // Downloads a file (skipped if already there). Returns "" or the error.
    public static string Download(string url, string assetPath)
    {
        try
        {
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
            if (File.Exists(full) && new FileInfo(full).Length > 0)
                return "";
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            using (var client = new WebClient())
                client.DownloadFile(url, full);
            return "";
        }
        catch (System.Exception e)
        {
            return e.Message;
        }
    }

    public static string DownloadModel(string name, string objectFile) =>
        Download("https://3d.varco.ai/api/objects/" + objectFile, ModelFolder + "/" + name + "/" + name + ".glb");

    // Wraps an imported model as a prop prefab in Resources/Celestial
    public static string MakePrefab(string name)
    {
        string modelPath = ModelFolder + "/" + name + "/" + name + ".glb";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
            return "no model at " + modelPath;
        var root = new GameObject(name);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model) ?? Object.Instantiate(model);
        inst.transform.SetParent(root.transform, false);
        var renderers = inst.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Object.DestroyImmediate(root);
            return "no renderers in " + modelPath;
        }
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers)
            b.Encapsulate(r.bounds);
        float k = 1f / Mathf.Max(0.001f, b.size.y);
        inst.transform.localScale *= k;
        inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * k;
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.5f, 0f);
        box.size = new Vector3(b.size.x * k, 1f, b.size.z * k);
        Directory.CreateDirectory(PrefabFolder);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + name + ".prefab");
        Object.DestroyImmediate(root);
        return "";
    }
}
