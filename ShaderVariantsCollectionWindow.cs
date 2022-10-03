using System.Text;
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEngine.Rendering;

public class ShaderVariantsCollectionWindow : EditorWindow
{
    //默认收集路径
    static List<string> defultCollectingPath = new List<string>() {
        "Assets",
    };
    //强制开启的宏
    HashSet<string> ForceEnabledGlobalKeywords = new HashSet<string>()
    {
        "_MAIN_LIGHT_SHADOWS","_MAIN_LIGHT_SHADOWS_CASCADE","LIGHTMAP_ON",
        "UNITY_HDR_ON","_SHADOWS_SOFT","_ADDITIONAL_LIGHTS",
    };
    //强制关闭的宏
    HashSet<string> ForceDisabledGlobalKeywords = new HashSet<string>()
    {

    };
    //svc 保存路径
    static string savePath = "Assets/Graphic/Shader";
    //log 保存路径
    static string shaderUsagePath = "Assets/ShaderUsage.csv";
    static List<string> collectingPath = new List<string>();
    static ShaderVariantCollection collection;
    static Shader srcShader;
    static Shader newShader;
    static string log;
    [MenuItem("Graphic/ShaderVariantsCollection &F9")]
    public static void Init()
    {
        var window = GetWindow(typeof(ShaderVariantsCollectionWindow), false, "变体收集工具", true) as ShaderVariantsCollectionWindow;
        window.position = new Rect(600, 400, 400, 130);
        collectingPath = new List<string>(defultCollectingPath);
        log = string.Empty;
        window.Show();
    }
    void OnGUI()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("添加"))
        {
            var path = collectingPath.Count > 0 ? collectingPath[collectingPath.Count - 1] : "Assets";
            var str = $"{EditorUtility.OpenFolderPanel("选择路径", path, "")}";
            if (string.IsNullOrEmpty(str)) return;
            str = str.Substring(str.LastIndexOf("Assets"));
            var flg = true;
            foreach (var p in collectingPath) if (p.Equals(str)) flg = false;
            if (flg) collectingPath.Add(str);
            log = "Not Collected...";
        }
        if (GUILayout.Button("清理"))
        {
            collectingPath.Clear();
            log = "path is null...";
        }
        if (GUILayout.Button("默认"))
        {
            collectingPath = new List<string>(defultCollectingPath);
            log = string.Empty;
        }
        GUILayout.Label($"路径:    {string.Join("/;", collectingPath.ToArray())}/;");
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        if (GUILayout.Button(EditorGUIUtility.TrTextContent("Print All Macros Used In Shader", "打印Shader中使用到的宏")))
        {
            string[] keys;
            // var material = Selection.activeObject as Material;
            // if (material == null) return;
            // keys = material.shaderKeywords;
            keys = ShaderUtilImpl.GetAllGlobalKeywords();
            // keys = ShaderUtilImpl.GetShaderLocalKeywords(material.shader);
            // keys = ShaderUtilImpl.GetShaderGlobalKeywords(material.shader);
            // Debug.Log(material.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS"));

            // string[] remainingKeys;
            // string[] filterKeys = null;
            // var passTypes = new int[] { (int)PassType.Normal, (int)PassType.ShadowCaster };
            // ShaderUtilImpl.GetShaderVariantEntriesFiltered(material.shader, 1000, filterKeys, new ShaderVariantCollection(), out passTypes, out keys, out remainingKeys);
            foreach (var key in keys)
            {
                Debug.Log(key);
            }
        }
        if (GUILayout.Button(EditorGUIUtility.TrTextContent("Print Shader Usage To File", "打印当前路径下材质球的Shader使用情况到csv文件")))
        {
            var materialGUIDs = AssetDatabase.FindAssets("t:Material", collectingPath.ToArray());
            StringBuilder str = new StringBuilder();
            foreach (var id in materialGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(id);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                var shader = mat.shader;
                str.Append($"{path},{shader.name}\n");
            }
            if (File.Exists(shaderUsagePath)) File.Delete(shaderUsagePath);
            File.WriteAllText(shaderUsagePath, str.ToString());
            AssetDatabase.Refresh();
            log = $"Saved Shader Usage To {shaderUsagePath}!";
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<TextAsset>(shaderUsagePath));
        }
        if (GUILayout.Button(EditorGUIUtility.TrTextContent("收集Shader变体", "收集当前路径中材质球用到的Shader变体")))
        {
            if (collectingPath.Count <= 0)
            {
                log = "Path is null, Select at least one !";
                EditorUtility.DisplayDialog("路径不可为空", "请点击 “添加” 指定收集路径!", "ok");
                return;
            }
            Collection();
            Save();
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(EditorGUIUtility.TrTextContent("Replace", "一键替换当前路径下所有材质球上的Shader")))
        {
            if (srcShader == null || newShader == null || collectingPath.Count <= 0) { log = "路径或Shader为空,替换无效"; return; }
            var materialGUIDs = AssetDatabase.FindAssets("t:Material", collectingPath.ToArray());
            foreach (var guid in materialGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat.shader == (srcShader)) mat.shader = newShader;
                //TODO:转换材质参数
            }
            AssetDatabase.SaveAssets();
            log = $"已将当前路径下所有材质球中{srcShader.name}替换为{newShader.name}";
        }
        if (GUILayout.Button(EditorGUIUtility.TrTextContent("Swap", "交换新旧Shader")))
        {
            var temp = srcShader;
            srcShader = newShader;
            newShader = temp;
            log = "swapped!";
        };
        srcShader = EditorGUILayout.ObjectField(srcShader, typeof(Shader), false) as Shader;
        GUILayout.Label("  To");
        newShader = EditorGUILayout.ObjectField(newShader, typeof(Shader), false) as Shader;
        GUILayout.EndHorizontal();

        GUILayout.Label(log);
    }
    void Collection()
    {
        collection = new ShaderVariantCollection();
        var materialGUIDs = AssetDatabase.FindAssets("t:Material", collectingPath.ToArray());
        foreach (var guid in materialGUIDs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            // if (path.EndsWith("FBX") || path.EndsWith("fbx") || path.EndsWith("obj")) continue;
            var material = AssetDatabase.LoadAssetAtPath(path, typeof(Material)) as Material;

            // AddVariantOfPassTypeToCollection(PassType.Normal, material);
            // AddVariantOfPassTypeToCollection(PassType.ScriptableRenderPipelineDefaultUnlit, material);
            AddVariantOfPassTypeToCollection(PassType.ScriptableRenderPipeline, material);
            if (material.FindPass("ShadowCaster") != -1) AddVariantOfPassTypeToCollection(PassType.ShadowCaster, material);
        }
        log = $"Found  {collection.shaderCount} Shaders & {collection.variantCount} Variants  Used In All Materials.";
    }
    void AddVariantOfPassTypeToCollection(PassType passType, Material material)
    {
        var shader = material.shader;
        var keywords = new List<string>();
        var shaderAllkeyworlds = GetShaderAllKeyworlds(shader);
        if (shaderAllkeyworlds.Contains("FOG_LINEAR") || shaderAllkeyworlds.Contains("FOG_EXP") || shaderAllkeyworlds.Contains("FOG_EXP2"))
        {
            if (RenderSettings.fog)
            {
                switch (RenderSettings.fogMode)
                {
                    case FogMode.Linear:
                        keywords.Add("FOG_LINEAR");
                        break;
                    case FogMode.Exponential:
                        keywords.Add("FOG_EXP");
                        break;
                    case FogMode.ExponentialSquared:
                        keywords.Add("FOG_EXP2");
                        break;
                    default:
                        break;
                }
            }
        }
        if (material.enableInstancing) keywords.Add("INSTANCING_ON");
        foreach (var key in material.shaderKeywords) keywords.Add(key);
        foreach (var key in ForceEnabledGlobalKeywords) { if (shaderAllkeyworlds.Contains(key) /*&& Shader.IsKeywordEnabled(key)*/) keywords.Add(key); }
        foreach (var key in ForceDisabledGlobalKeywords) keywords.Remove(key);

        collection.Add(CreateVariant(shader, passType, keywords.ToArray()));
    }
    ShaderVariantCollection.ShaderVariant CreateVariant(Shader shader, PassType passType, string[] keywords)
    {
        // foreach (var k in keywords)
        // {
        //     Debug.Log($"{shader.name}:{passType}:{k}");
        // }
        try
        {
            // var variant = new ShaderVariantCollection.ShaderVariant(shader, passType, keywords);//这构造函数就是个摆设,铁定抛异常(╯‵□′)╯︵┻━┻
            var variant = new ShaderVariantCollection.ShaderVariant();
            variant.shader = shader;
            variant.passType = passType;
            variant.keywords = keywords;
            return variant;
        }
        catch (System.Exception e)
        {
            Debug.LogError(e);
            return new ShaderVariantCollection.ShaderVariant();
        }
    }
    Dictionary<Shader, List<string>> shaderKeyworldsDic = new Dictionary<Shader, List<string>>();
    List<string> GetShaderAllKeyworlds(Shader shader)
    {
        List<string> keywords = null;
        shaderKeyworldsDic.TryGetValue(shader, out keywords);
        if (keywords == null)
        {
            keywords = new List<string>(ShaderUtilImpl.GetShaderGlobalKeywords(shader));
            shaderKeyworldsDic.Add(shader, keywords);
        }
        return keywords;
    }
    void Save()
    {
        var str = $"{EditorUtility.SaveFilePanel("选择保存路径", savePath, "NewShaderVariants", "shadervariants")}";
        savePath = str.Substring(str.LastIndexOf("Assets"));
        if (collection && !string.IsNullOrEmpty(savePath))
        {
            if (File.Exists(str)) AssetDatabase.DeleteAsset(savePath);
            AssetDatabase.CreateAsset(collection, savePath);
            UnityEditor.EditorUtility.FocusProjectWindow();
            UnityEditor.Selection.activeObject = collection;
            EditorGUIUtility.PingObject(collection);
            log = $"(shader:{collection.shaderCount}, variant:{collection.variantCount}) Collection Saved At: {savePath} !";
            collection = null;//overwrite goes wrong...
        }
        else log = "Not Saved, Please Collect Them First!";
    }

}
public class ShaderUtilImpl
{
    delegate string[] GetShaderGlobalKeywords_type(Shader shader);
    static GetShaderGlobalKeywords_type GetShaderGlobalKeywords_impl;
    public static string[] GetShaderGlobalKeywords(Shader shader)
    {
        if (GetShaderGlobalKeywords_impl == null) GetShaderGlobalKeywords_impl = Delegate.CreateDelegate
        (
            typeof(GetShaderGlobalKeywords_type),
            typeof(UnityEditor.ShaderUtil).GetMethod("GetShaderGlobalKeywords", BindingFlags.Static | BindingFlags.NonPublic)
        ) as GetShaderGlobalKeywords_type;
        return GetShaderGlobalKeywords_impl(shader) as string[];
    }
    delegate string[] GetAllGlobalKeywords_type();
    static GetAllGlobalKeywords_type GetAllGlobalKeywords_impl;
    public static string[] GetAllGlobalKeywords()
    {
        if (GetAllGlobalKeywords_impl == null) GetAllGlobalKeywords_impl = Delegate.CreateDelegate
       (
           typeof(GetAllGlobalKeywords_type),
           typeof(UnityEditor.ShaderUtil).GetMethod("GetAllGlobalKeywords", BindingFlags.Static | BindingFlags.NonPublic)
       ) as GetAllGlobalKeywords_type;
        return GetAllGlobalKeywords_impl() as string[];
    }
    delegate string[] GetShaderLocalKeywords_type(Shader shader);
    static GetShaderLocalKeywords_type GetShaderLocalKeywords_impl;
    public static string[] GetShaderLocalKeywords(Shader shader)
    {
        if (GetShaderLocalKeywords_impl == null) GetShaderLocalKeywords_impl = Delegate.CreateDelegate
       (
           typeof(GetShaderLocalKeywords_type),
           typeof(UnityEditor.ShaderUtil).GetMethod("GetShaderLocalKeywords", BindingFlags.Static | BindingFlags.NonPublic)
       ) as GetShaderLocalKeywords_type;
        return GetShaderLocalKeywords_impl(shader) as string[];
    }
    delegate void GetShaderVariantEntriesFiltered_type(
        Shader shader,
        int maxEntries,
        string[] filterKeywords,
        ShaderVariantCollection excludeCollection,
        out int[] passTypes,
        out string[] keywordLists,
        out string[] remainingKeywords
    );
    static GetShaderVariantEntriesFiltered_type GetShaderVariantEntriesFiltered_impl;
    public static void GetShaderVariantEntriesFiltered(
        Shader shader,
        int maxEntries,
        string[] filterKeywords,
        ShaderVariantCollection excludeCollection,
        out int[] passTypes,
        out string[] keywordLists,
        out string[] remainingKeywords)
    {
        if (GetShaderVariantEntriesFiltered_impl == null) GetShaderVariantEntriesFiltered_impl = Delegate.CreateDelegate
       (
           typeof(GetShaderVariantEntriesFiltered_type),
           typeof(UnityEditor.ShaderUtil).GetMethod("GetShaderVariantEntriesFiltered", BindingFlags.Static | BindingFlags.NonPublic)
       ) as GetShaderVariantEntriesFiltered_type;
        GetShaderVariantEntriesFiltered_impl(shader, maxEntries, filterKeywords, excludeCollection, out passTypes, out keywordLists, out remainingKeywords);
    }

    public struct ShaderVariantEntriesData
    {
        public int[] passTypes;
        public string[] keywordLists;
        public string[] remainingKeywords;
    }
    delegate ShaderVariantEntriesData GetShaderVariantEntriesFilteredInternal_type(
        Shader shader,
        int maxEntries,
        string[] filterKeywords,
        ShaderVariantCollection excludeCollection
  );
    static GetShaderVariantEntriesFilteredInternal_type GetShaderVariantEntriesFilteredInternal_impl;
    public static ShaderVariantEntriesData GetShaderVariantEntriesFilteredInternal(
        Shader shader,
        int maxEntries,
        string[] filterKeywords,
        ShaderVariantCollection excludeCollection)
    {
        if (GetShaderVariantEntriesFilteredInternal_impl == null) GetShaderVariantEntriesFilteredInternal_impl = Delegate.CreateDelegate
       (
           typeof(GetShaderVariantEntriesFilteredInternal_type),
           typeof(UnityEditor.ShaderUtil).GetMethod("GetShaderVariantEntriesFilteredInternal", BindingFlags.Static | BindingFlags.NonPublic)
       ) as GetShaderVariantEntriesFilteredInternal_type;
        return GetShaderVariantEntriesFilteredInternal_impl(shader, maxEntries, filterKeywords, excludeCollection);
    }
}
