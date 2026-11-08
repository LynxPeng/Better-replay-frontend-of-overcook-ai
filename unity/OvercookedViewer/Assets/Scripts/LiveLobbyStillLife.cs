using UnityEngine;
using UnityEngine.Rendering;

namespace OvercookedReplay {
// Photograph the same props that furnish the dining room. Keep only the rendered
// menu picture, so opening the lobby does not add a second camera to a live match.
internal static class LiveLobbyStillLife {
    const int Layer = 30;

    public static RenderTexture Photograph() {
        const int side = 768;
        var image = new RenderTexture(side, side, 24, RenderTextureFormat.ARGB32) {
            name = "Dining room menu photograph", hideFlags = HideFlags.DontSave,
            antiAliasing = 4, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
        };
        var stage = new GameObject("Temporary menu portrait");
        stage.transform.position = new Vector3(512, 40, 512);
        var root = stage.transform;
        try {
            KitchenFactory.Part("Backdrop", root, PrimitiveType.Cube, new Vector3(0, -.39f, 0),
                new Vector3(30,.1f,30), KitchenFactory.Material("#9CBDAF"));
            KitchenFactory.Part("Edge of floor", root, PrimitiveType.Cylinder, new Vector3(0,-.185f,0),
                new Vector3(3.20f,.14f,3.20f), KitchenFactory.Material("#BA8256"));
            KitchenFactory.Part("Stone floor", root, PrimitiveType.Cylinder, new Vector3(0,-.058f,0),
                new Vector3(3.25f,.058f,3.25f), KitchenFactory.Material("#E9D8AE"));
            var table = KitchenFactory.RestaurantProp("table_round_B_tablecloth_red", root, 1.8f, Vector3.zero);
            if (!table) throw new System.InvalidOperationException("Table model unavailable");
            float top = KitchenFactory.LocalBounds(table, root).max.y;
            KitchenFactory.RestaurantProp("chair_B", root, .85f, new Vector3(-1.02f,0,-.44f), -1, 68);
            KitchenFactory.RestaurantProp("chair_B", root, .85f, new Vector3(1.02f,0,.44f), -1, 248);
            var plateAt = new Vector3(-.34f,top+.003f,-.12f);
            var plate = KitchenFactory.RestaurantProp("plate_small", root, .64f, plateAt);
            float mealY = plate ? KitchenFactory.LocalBounds(plate, root).max.y : top;
            KitchenFactory.RestaurantProp("food_burger", root, .53f, new Vector3(plateAt.x,mealY-.004f,plateAt.z), -1, 24);
            KitchenFactory.RestaurantProp("icecream_bowl_decorated_B", root, .56f, new Vector3(.32f,top,.04f));
            KitchenFactory.RestaurantProp("menu", root, .36f, new Vector3(-.12f,top,.49f), -1, 190);
            foreach (var part in stage.GetComponentsInChildren<Transform>()) part.gameObject.layer = Layer;

            var camera = new GameObject("Menu camera").AddComponent<Camera>();
            camera.transform.SetParent(root,false);
            camera.transform.localPosition = new Vector3(2.0f,2.75f,-5f);
            camera.transform.LookAt(root.TransformPoint(new Vector3(0,.33f,0)));
            camera.orthographic=true;camera.orthographicSize=1.79f;camera.aspect=1;
            camera.nearClipPlane=.04f;camera.farClipPlane=25;camera.cullingMask=1<<Layer;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.58f,.72f,.66f);
            camera.allowHDR=false;camera.allowMSAA=true;camera.enabled=false;
            var key=new GameObject("Menu softbox").AddComponent<Light>();
            key.transform.SetParent(root,false);key.transform.localPosition=new Vector3(-2.2f,3.8f,-2.4f);
            key.transform.LookAt(root.position);key.type=LightType.Spot;key.spotAngle=85;
            key.range=11;key.intensity=8;key.color=new Color(1,.95f,.84f);key.cullingMask=1<<Layer;
            key.shadows=LightShadows.Soft;key.shadowStrength=.5f;
            image.Create();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=image});
            return image;
        } catch {
            image.Release();
            Object.Destroy(image);
            throw;
        } finally {
            stage.SetActive(false);
            Object.Destroy(stage);
        }
    }
}
}
