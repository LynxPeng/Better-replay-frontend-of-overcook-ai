using UnityEngine;
using UnityEngine.Rendering;

namespace OvercookedReplay {
// A stove owns its effects for the lifetime of the kitchen. All motion is sampled
// from the replay clock so pausing, seeking and network snapshots agree.
internal sealed class PotSimmer {
    const int PuffCount = 4;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static Material vapour;
    readonly Transform root, broth;
    readonly Renderer[] puffs = new Renderer[PuffCount];
    readonly MaterialPropertyBlock colour = new MaterialPropertyBlock();
    readonly float offset;
    bool hot, ready;

    public PotSimmer(Transform station, int x, int y) {
        offset = Mathf.Repeat(x * .27f + y * .43f, 1f);
        root = new GameObject("Simmering pot").transform;
        root.SetParent(station, false);
        // Use the imported pot's rim as the reference, including on custom layouts.
        var pot = station.Find("pot_A");
        float rim = pot ? KitchenFactory.LocalBounds(pot.gameObject, station).max.y : KitchenFactory.CounterTopHeight + .25f;
        root.localPosition = Vector3.up * (rim - .045f);
        var insert = KitchenFactory.RestaurantProp("stew_pot", root, .395f, Vector3.zero, .035f);
        if (insert) broth = insert.transform;
        if (!vapour) vapour = SteamMaterial();
        for (int i = 0; i < PuffCount; i++) {
            var puff = KitchenFactory.Part("Steam", root, PrimitiveType.Quad, Vector3.zero, Vector3.one, vapour);
            puffs[i] = puff.GetComponent<Renderer>();
            puffs[i].shadowCastingMode = ShadowCastingMode.Off;
            puffs[i].receiveShadows = false;
        }
        SetSoup(null);
    }

    public void SetSoup(Item soup) {
        hot = soup != null && (soup.is_cooking || soup.is_ready);
        ready = soup != null && soup.is_ready;
        for (int i = 0; i < PuffCount; i++) puffs[i].gameObject.SetActive(hot);
        if (broth) broth.gameObject.SetActive(hot);
    }

    public void Sample(float seconds, Quaternion view) {
        if (!hot) return;
        float cycle = seconds * (ready ? .62f : .79f) + offset;
        if (broth) broth.localRotation = Quaternion.Euler(0, 5 * Mathf.Sin(cycle * 4.5f), 0);
        for (int i = 0; i < PuffCount; i++) {
            float t = Mathf.Repeat(cycle + i / (float)PuffCount, 1);
            float lane = (i % 2 == 0 ? -.06f : .06f);
            var puff = puffs[i].transform;
            puff.localPosition = new Vector3(lane + t * .08f + Mathf.Sin(i + t * 4) * t * .12f,
                .065f + t * .76f, Mathf.Cos(i + t * 3) * .055f);
            puff.rotation = view * Quaternion.AngleAxis((i % 2 == 0 ? 1 : -1) * (12 + t * 18), Vector3.forward);
            float width = Mathf.Lerp(.14f, .39f, t);
            puff.localScale = new Vector3(width, width * 1.2f, 1);
            float alpha = Mathf.Sin(t * Mathf.PI) * (ready ? .74f : .63f);
            colour.SetColor(BaseColor, new Color(1, 1, 1, alpha));
            puffs[i].SetPropertyBlock(colour);
        }
    }

    static Material SteamMaterial() {
        // Reuse the explicit transparent shader included for counter hints.
        var material = new Material(Resources.Load<Material>("Materials/InteractionHintBlue"));
        var texture = new Texture2D(48, 48, TextureFormat.RGBA32, true) {
            name = "Soft steam", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[48 * 48];
        for (int y = 0; y < 48; y++) for (int x = 0; x < 48; x++) {
            float v = (y + .5f) / 24f - 1;
            float u = (x + .5f) / 24f - 1 - .10f * Mathf.Sin(v * 4);
            float density = Mathf.Clamp01(1 - u * u * 1.65f - v * v * 1.08f);
            pixels[y * 48 + x] = new Color(1, 1, 1, density * density);
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        material.name = "White cooking steam";
        material.hideFlags = HideFlags.HideAndDontSave;
        material.SetColor(BaseColor, Color.white);
        material.SetTexture("_BaseMap", texture);
        return material;
    }
}
}
