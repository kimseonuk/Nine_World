using System.IO;
using System.Net;
using UnityEditor;
using UnityEngine;

// Editor helper: downloads images made in VARCO's image studio (public object URLs) into the project
public static class VarcoImageFetch
{
    // Saves url to assetPath (e.g. "Assets/Common/Resources/UI/Route/rm_map.png") and imports it as a UI texture.
    // Returns "" on success, else the error.
    public static string Fetch(string url, string assetPath, int maxSize = 4096)
    {
        try
        {
            byte[] data;
            using (var client = new WebClient())
                data = client.DownloadData(url);
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, data);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = maxSize;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            return "";
        }
        catch (System.Exception e)
        {
            return e.Message;
        }
    }
}
