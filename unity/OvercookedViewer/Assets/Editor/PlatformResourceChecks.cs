using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using OvercookedReplay;

public static class PlatformResourceChecks {
    public static void Prepare() {
        string directory="Assets/Resources/RecipeIcons";
        Directory.CreateDirectory(directory);
        using(var art=new RecipeArtwork())foreach(string name in new[]{"onion","tomato"}) {
            var target=art.Bake(name) as RenderTexture;
            if(!target)throw new Exception("Cannot bake recipe image: "+name);
            var previous=RenderTexture.active;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try {
                RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                string path=directory+"/"+name+".png";
                File.WriteAllBytes(path,image.EncodeToPNG());
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
                importer.SaveAndReimport();
            } finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
        var shader=Resources.Load<Shader>("InteractionSurface");
        if(!shader)throw new Exception("Missing explicit interaction surface shader");
        foreach(string name in new[]{"InteractionHintBlue","InteractionHintOrange"}) {
            var material=Resources.Load<Material>("Materials/"+name);
            material.shader=shader;material.shaderKeywords=Array.Empty<string>();
            EditorUtility.SetDirty(material);
        }
        var font=Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
        var fontImporter=(TrueTypeFontImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(font));
        fontImporter.includeFontData=true;fontImporter.fontTextureCase=FontTextureCase.Dynamic;
        fontImporter.SaveAndReimport();
        AssetDatabase.SaveAssets();
    }
    public static void Run() {
        var font=Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
        {
            if(!font)throw new Exception("Missing embedded Chinese font");
            foreach(char c in "厨房回放菜谱洋葱番茄播放暂停夜晚设置蓝方企鹅大肥鱼")
                if(!font.HasCharacter(c))throw new Exception("Missing embedded glyph: "+c);
        }
        foreach(string name in new[]{"onion","tomato"}) {
            var icon=Resources.Load<Texture2D>("RecipeIcons/"+name);
            if(!icon || icon.width<64)throw new Exception("Missing baked recipe image: "+name);
        }
        var counter=Resources.Load<Material>("Materials/CafeCounter");
        if(!counter || !counter.shader || !counter.mainTexture)
            throw new Exception("Counter shader or atlas reference missing");
        foreach(string name in new[]{"InteractionHintBlue","InteractionHintOrange"})
            if(Resources.Load<Material>("Materials/"+name).shader!=Resources.Load<Shader>("InteractionSurface"))
                throw new Exception("Counter overlay must use the explicit portable shader");
        Debug.Log("Platform resources passed: embedded Chinese glyphs, recipe images and counter references");
    }
}
