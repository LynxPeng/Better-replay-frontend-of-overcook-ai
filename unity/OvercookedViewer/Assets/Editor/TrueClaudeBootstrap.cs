using System;
using System.IO;
using System.Linq;
using OvercookedReplay;
using UnityEditor;
using UnityEngine;

public static class TrueClaudeBootstrap {
    public const string ModelPath = "Assets/LocalCharacters/TrueClaude/TrueClaude.glb";
    public const string ActorPath = "Assets/Resources/LocalCharacters/TrueClaude.prefab";
    const float ActorHeight = 1.30f;

    [MenuItem("Tools/Overcooked/True Claude/Prepare local replay actor")]
    public static void Prepare() {
        if (!File.Exists(ModelPath))
            throw new FileNotFoundException("Run scripts/import-true-claude.py to acquire the local model.", ModelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(ActorPath));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(ModelPath);
        if (!importer) throw new InvalidOperationException("Unity could not import " + ModelPath);
        var settings = new SerializedObject(importer);
        SetImportOption(settings, "m_reverseAxis", "Z");
        SetImportOption(settings, "m_renderPipeline", "UniversalRenderPipeline");
        if (settings.ApplyModifiedPropertiesWithoutUndo()) importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!source) throw new InvalidOperationException("UniGLTF did not produce the True Claude model.");

        var root = new GameObject("TrueClaude");
        try {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            model.name = "Model";
            // The source faces glTF +Z. Restore replay forward after UniGLTF reverses Z.
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 3) throw new InvalidOperationException("Expected True Claude's body, head/hair and arms.");
            Bounds? bounds = null;
            foreach (var renderer in renderers) {
                var mesh = renderer.sharedMesh;
                if (!mesh || renderer.bones.Length == 0 || renderer.bones.Any(bone => !bone))
                    throw new InvalidOperationException("Missing skinned geometry on " + renderer.name);
                var material = renderer.sharedMaterial;
                if (!material || !material.GetTexture("_BaseMap") || !material.GetTexture("_BumpMap"))
                    throw new InvalidOperationException("Missing True Claude color or normal texture on " + renderer.name);
                renderer.updateWhenOffscreen = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                var meshBounds = bounds ?? new Bounds(renderer.transform.TransformPoint(mesh.vertices[0]), Vector3.zero);
                foreach (var vertex in mesh.vertices) meshBounds.Encapsulate(renderer.transform.TransformPoint(vertex));
                bounds = meshBounds;
            }
            var bindBounds = bounds.Value;
            float scale = ActorHeight / bindBounds.size.y;
            model.transform.localScale = Vector3.one * scale;
            // Center across the shoulders and put the soles on the floor. The authored
            // root supplies the forward pivot; long hair extends behind the body.
            model.transform.localPosition = new Vector3(-bindBounds.center.x, -bindBounds.min.y, 0) * scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(model);
            PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
            root.AddComponent<TrueClaudeActor>();
            if (!PrefabUtility.SaveAsPrefabAsset(root, ActorPath))
                throw new InvalidOperationException("Unity could not save " + ActorPath);
            AssetDatabase.SaveAssets();
            int joints = renderers.SelectMany(renderer => renderer.bones).Distinct().Count();
            Debug.Log($"Prepared True Claude: {renderers.Length} skinned meshes, {joints} joints, " +
                $"{ActorHeight:F2} units high, color and normal textures verified.");
        } finally {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void SetImportOption(SerializedObject settings, string field, string value) {
        var property = settings.FindProperty(field);
        int index = property == null ? -1 : Array.IndexOf(property.enumNames, value);
        if (index < 0) throw new InvalidOperationException($"UniGLTF option {field}.{value} is unavailable.");
        if (property.enumValueIndex != index) property.enumValueIndex = index;
    }
}
