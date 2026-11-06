using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OvercookedReplay {
// Menu illustrations use the very same Restaurant Bits mesh and palette seen in the kitchen.
// Each is drawn once off screen. No preview objects or cameras join the playing scene.
internal sealed class RecipeArtwork : IDisposable {
    readonly Dictionary<string, RenderTexture> icons = new Dictionary<string, RenderTexture>();
    Material ink;
    public Texture Find(string ingredient) {
        if (ingredient != "onion" && ingredient != "tomato") return null;
        if (icons.TryGetValue(ingredient, out var cached)) return cached;
        var model = Resources.Load<GameObject>("RestaurantBits/Models/food_ingredient_" + ingredient);
        var atlas = Resources.Load<Texture2D>("RestaurantBits/Textures/restaurantbits_extra");
        var shader = Resources.Load<Shader>("RecipeIllustration");
        RenderTexture icon = null;
        if (model && atlas && shader && shader.isSupported && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) {
            if (!ink) ink = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            icon = Paint(model, atlas);
        }
        icons[ingredient] = icon;
        return icon;
    }

    RenderTexture Paint(GameObject model, Texture2D atlas) {
        var parts = model.GetComponentsInChildren<MeshFilter>();
        var viewRotation = Quaternion.LookRotation(new Vector3(-.46f, -.43f, .78f));
        var cameraRotation = Matrix4x4.Rotate(Quaternion.Inverse(viewRotation));
        var bounds = new Bounds(); bool first = true;
        foreach (var part in parts) {
            if (!part.sharedMesh) continue;
            var matrix = cameraRotation * part.transform.localToWorldMatrix;
            var box = part.sharedMesh.bounds;
            for (int i = 0; i < 8; i++) {
                var sign = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                var point = matrix.MultiplyPoint3x4(box.center + Vector3.Scale(box.extents, sign));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        }
        if (first) return null;
        float half = Mathf.Max(.001f, Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.04f);
        float depth = Mathf.Max(half, bounds.extents.z);
        var eye = viewRotation * (bounds.center - Vector3.forward * (depth * 2));
        var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(eye, viewRotation, Vector3.one).inverse;
        var projection = Matrix4x4.Ortho(-half, half, -half, half, depth * .1f, depth * 4);
        var properties = new MaterialPropertyBlock();
        properties.SetTexture("_Atlas", atlas);
        properties.SetMatrix("_IconVP", GL.GetGPUProjectionMatrix(projection, true) * view);
        var target = new RenderTexture(128, 128, 16, RenderTextureFormat.ARGB32) {
            name = "Order illustration · " + model.name, hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
        };
        target.Create();
        using (var buffer = new CommandBuffer { name = "Illustrate recipe ingredient" }) {
            buffer.SetRenderTarget(target);
            buffer.ClearRenderTarget(true, true, Color.clear);
            foreach (var part in parts) {
                var mesh = part.sharedMesh;
                if (!mesh) continue;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    buffer.DrawMesh(mesh, part.transform.localToWorldMatrix, ink, i, 0, properties);
            }
            Graphics.ExecuteCommandBuffer(buffer);
        }
        return target;
    }
    public void Dispose() {
        foreach (var target in icons.Values) if (target) { target.Release(); UnityEngine.Object.Destroy(target); }
        if (ink) UnityEngine.Object.Destroy(ink);
        icons.Clear();
    }
}
}
