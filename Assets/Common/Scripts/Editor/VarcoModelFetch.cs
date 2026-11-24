using System.IO;
using System.Net;
using UnityEditor;
using UnityEngine;

// Editor helper: brings VARCO 3D models (GLB) into the project and turns them into route props
// (Resources/Props/<name>.prefab: base centred on the origin, 1 unit tall, a box collider round it)
public static class VarcoModelFetch
{
    public const string PropsFolder = "Assets/Common/Fighters/Shared/Resources/Props";

    // Downloads url to Assets/Common/Models/Forge/<name>/<name>.glb and makes the prop. Returns "" or the error.
    public static string Fetch(string url, string name)
    {
        try
        {
            byte[] data;
            using (var client = new WebClient())
                data = client.DownloadData(url);
            string assetPath = "Assets/Common/Models/Forge/" + name + "/" + name + ".glb";
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, data);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return MakeProp(assetPath, name);
        }
        catch (System.Exception e)
        {
            return e.Message;
        }
    }

    // Wraps an imported model asset as a prop prefab
    public static string MakeProp(string modelPath, string name)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
            return "no model at " + modelPath;
        var root = new GameObject(name);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        if (inst == null)
            inst = Object.Instantiate(model);
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
        float height = Mathf.Max(0.001f, b.size.y);
        float k = 1f / height;
        inst.transform.localScale *= k;
        inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * k;
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.5f, 0f);
        box.size = new Vector3(b.size.x * k, 1f, b.size.z * k);
        Directory.CreateDirectory(PropsFolder);
        PrefabUtility.SaveAsPrefabAsset(root, PropsFolder + "/" + name + ".prefab");
        Object.DestroyImmediate(root);
        return "";
    }
}
