using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using OvercookedReplay;

// Hand-authored four-by-four tiles with rounded bevel normals and a pale rim.
public static class InteriorMaterialAssets {
    const string Root="Assets/Art/CafeSurfaces/";
    const int Resolution=1024,Tile=256;
    public static void Prepare() {
        Directory.CreateDirectory(Root);Directory.CreateDirectory("Assets/Resources/Materials");
        var colours=new Color32[Resolution*Resolution];var normals=new Color32[colours.Length];
        for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++) {
            int tx=x/Tile,ty=y/Tile;
            float inset=Inset(x,y),bevel=Height(x,y);
            float variation=(Hash(tx,ty)-.5f)*56;
            float mottling=(Mathf.PerlinNoise(x*.033f+13,y*.033f+7)-.5f)*28;
            float grain=(Hash(x+117,y+39)-.5f)*1.6f;
            var stone=new Color((181+variation+mottling+grain)/255,(177+variation+mottling+grain)/255,(163+variation+mottling+grain)/255);
            var grout=new Color(114/255f,108/255f,95/255f);
            float rim=inset>0?Mathf.Exp(-Mathf.Pow((inset-.012f)/.004f,2)):0;
            colours[y*Resolution+x]=Color.Lerp(grout,stone,bevel)+new Color(.08f,.078f,.07f,0)*rim;
            float dx=Height(x+1,y)-Height(x-1,y),dy=Height(x,y+1)-Height(x,y-1);
            Vector3 n=new Vector3(-dx*.55f,-dy*.55f,1).normalized;
            normals[y*Resolution+x]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
        }
        Texture("CafeTile_BaseColor.png",colours,Resolution,Resolution,false);
        Texture("CafeTile_Normal.png",normals,Resolution,Resolution,true);
        var floor=Material("RestaurantTile");
        floor.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"CafeTile_BaseColor.png"));
        floor.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"CafeTile_Normal.png"));
        floor.EnableKeyword("_NORMALMAP");floor.SetFloat("_BumpScale",.65f);EditorUtility.SetDirty(floor);
        CounterPalette();
        HintMaterial("InteractionHintBlue",new Color(.12f,.65f,1,.52f));
        HintMaterial("InteractionHintOrange",new Color(1,.67f,.12f,.52f));
        // Retain the previous optional floor outside Resources, preserving GUIDs.
        const string previous="Assets/Resources/Materials/RestaurantWood.mat";
        if(AssetDatabase.LoadAssetAtPath<Material>(previous)) {
            string error=AssetDatabase.MoveAsset(previous,"Assets/Art/WoodPlanksGrey/RestaurantWood.mat");
            if(!string.IsNullOrEmpty(error))throw new InvalidOperationException(error);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Cafe tiles, bevel normals and isolated honey counter palette generated");
    }
    static void HintMaterial(string name,Color colour) {
        string path="Assets/Resources/Materials/"+name+".mat";var fresh=InteractionHints.Tint(colour);
        var old=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(old){EditorUtility.CopySerialized(fresh,old);EditorUtility.SetDirty(old);UnityEngine.Object.DestroyImmediate(fresh);}
        else AssetDatabase.CreateAsset(fresh,path);
    }
    static float Inset(int x,int y) {
        x=(x%Tile+Tile)%Tile;y=(y%Tile+Tile)%Tile;
        float radius=CafeSurfaceStyle.TileCornerRadius,half=(1-CafeSurfaceStyle.JointWidth)*.5f;
        var q=new Vector2(Mathf.Abs((x+.5f)/Tile-.5f),Mathf.Abs((y+.5f)/Tile-.5f))-Vector2.one*(half-radius);
        float distance=new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0)-radius;
        return -distance;
    }
    static float Height(int x,int y)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,CafeSurfaceStyle.TileBevelWidth,Inset(x,y)));
    static float Hash(int x,int y) {
        unchecked {uint n=(uint)(x*374761393+y*668265263+173);n=(n^(n>>13))*1274126177;return (n^(n>>16))/(float)uint.MaxValue;}
    }
    static void CounterPalette() {
        var original=Resources.Load<Texture2D>("RestaurantBits/Textures/restaurantbits_extra");
        if(!original)throw new InvalidOperationException("Native Restaurant Bits atlas is missing");
        // Decode the local PNG without changing its importer or depending on
        // GPU colour conversions. The licensed source stays untouched.
        var copy=new Texture2D(2,2,TextureFormat.RGBA32,false);
        try {
            if(!ImageConversion.LoadImage(copy,File.ReadAllBytes(AssetDatabase.GetAssetPath(original))))throw new InvalidOperationException("Cannot decode native palette PNG");
            var pixels=copy.GetPixels32();
            for(int i=0;i<pixels.Length;i++) {
                Color c=pixels[i];Color.RGBToHSV(c,out float h,out float s,out float v);
                if(h>.025f&&h<.12f&&s>.35f&&v>.25f) {
                    var honey=Color.HSVToRGB(.09f,s*.84f,v*.98f);honey.a=c.a;pixels[i]=honey;
                }
            }
            Texture("CafeCounter_Atlas.png",pixels,copy.width,copy.height,false);
        }finally {UnityEngine.Object.DestroyImmediate(copy);}
        var counter=Material("CafeCounter");counter.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"CafeCounter_Atlas.png"));EditorUtility.SetDirty(counter);
    }
    static Material Material(string name) {
        string path="Assets/Resources/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",Color.white);m.SetFloat("_Smoothness",.14f);m.SetFloat("_Metallic",0);
        m.SetFloat("_SpecularHighlights",0);m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_EnvironmentReflections",0);m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");return m;
    }
    static void Texture(string name,Color32[] pixels,int width,int height,bool normal) {
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,normal);texture.SetPixels32(pixels);texture.Apply();
        byte[] bytes=texture.EncodeToPNG();UnityEngine.Object.DestroyImmediate(texture);string path=Root+name;
        if(!File.Exists(path)||!File.ReadAllBytes(path).SequenceEqual(bytes))File.WriteAllBytes(path,bytes);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;importer.sRGBTexture=!normal;
        importer.wrapMode=TextureWrapMode.Repeat;importer.mipmapEnabled=true;importer.anisoLevel=8;importer.maxTextureSize=2048;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
    }
}
