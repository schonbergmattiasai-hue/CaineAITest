using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SceneSnapshotToAsset : MonoBehaviour
{
    [Header("Asset output (Editor only)")]
    public string outputFolderUnderAssets = "Assets/Remote/Snapshots";
    public string fileName = "scene_snapshot.json";

    [Tooltip("If true, saves automatically when you press Play (Start).")]
    public bool saveOnStart = true;

    [Tooltip("If true, includes inactive GameObjects too.")]
    public bool includeInactive = true;

    void Start()
    {
        if (saveOnStart)
            SaveSnapshotAsAsset();
    }

    [ContextMenu("Save Snapshot As Asset (Editor)")]
    public void SaveSnapshotAsAsset()
    {
#if UNITY_EDITOR
        var snapshot = BuildSnapshot();
        var json = JsonUtility.ToJson(snapshot, prettyPrint: true);

        Directory.CreateDirectory(outputFolderUnderAssets);

        var assetPath = Path.Combine(outputFolderUnderAssets, fileName).Replace("\\", "/");
        File.WriteAllText(assetPath, json);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        Debug.Log($"Scene snapshot saved as asset: {assetPath}");
#else
        Debug.LogWarning("SceneSnapshotToAsset only works in the Unity Editor. In builds, save to Application.persistentDataPath instead.");
#endif
    }

    private SceneSnapshot BuildSnapshot()
    {
        var snap = new SceneSnapshot
        {
            createdUtc = DateTime.UtcNow.ToString("o"),
            unityVersion = Application.unityVersion,
            activeScene = SceneManager.GetActiveScene().name,
            objects = new List<ObjectSnapshot>()
        };

        Transform[] all = includeInactive
            ? FindObjectsOfType<Transform>(true)
            : FindObjectsOfType<Transform>();

        foreach (var t in all)
        {
            if (t == null) continue;
            var go = t.gameObject;

            snap.objects.Add(new ObjectSnapshot
            {
                name = go.name,
                path = GetHierarchyPath(t),
                tag = go.tag,
                layer = go.layer,
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,

                position = new V3(t.position),
                rotationEuler = new V3(t.rotation.eulerAngles),

                localPosition = new V3(t.localPosition),
                localRotationEuler = new V3(t.localRotation.eulerAngles),
                localScale = new V3(t.localScale),
            });
        }

        return snap;
    }

    private static string GetHierarchyPath(Transform t)
    {
        var stack = new Stack<string>();
        var cur = t;
        while (cur != null)
        {
            stack.Push(cur.name);
            cur = cur.parent;
        }
        return string.Join("/", stack);
    }

    [Serializable]
    private class SceneSnapshot
    {
        public string createdUtc;
        public string unityVersion;
        public string activeScene;
        public List<ObjectSnapshot> objects;
    }

    [Serializable]
    private class ObjectSnapshot
    {
        public string name;
        public string path;
        public string tag;
        public int layer;
        public bool activeSelf;
        public bool activeInHierarchy;

        public V3 position;
        public V3 rotationEuler;

        public V3 localPosition;
        public V3 localRotationEuler;
        public V3 localScale;
    }

    [Serializable]
    private struct V3
    {
        public float x, y, z;
        public V3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
    }
}