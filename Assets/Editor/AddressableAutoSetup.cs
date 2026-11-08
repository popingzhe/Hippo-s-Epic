using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public static class AddressableAutoSetup
{
    struct DirConfig
    {
        public string dir;
        public string addressPrefix;
        public string label;
        public string filter;
        public bool forceSprite;
        public string groupName;
        public string excludeSubDir;
        public string removeLabel;
    }

    static readonly DirConfig[] _configs = new[]
    {
        new DirConfig { dir = "Assets/Art/Backgrounds", addressPrefix = "Assets/Art/Backgrounds", label = "background", filter = "t:Texture2D", forceSprite = false, groupName = null, excludeSubDir = null },
        new DirConfig { dir = "Assets/Art/Sprites/Battle/bg", addressPrefix = "Assets/Art/Sprites/Battle/bg", label = "battle_bg", filter = "t:Texture2D", forceSprite = true, groupName = null, excludeSubDir = null },
        new DirConfig { dir = "Assets/Art/DialogImages", addressPrefix = "Assets/Art/DialogImages", label = null, filter = "t:Texture2D", forceSprite = false, groupName = null, excludeSubDir = null },
        new DirConfig { dir = "Assets/Art/Sprites/UI/Item", addressPrefix = "Assets/Art/Sprites/UI/Item", label = "icon", filter = "t:Texture2D", forceSprite = true, groupName = null, excludeSubDir = null },
        new DirConfig { dir = "Assets/Art/Sprites/UI/battle/skill_icon", addressPrefix = "Assets/Art/Sprites/UI/battle/skill_icon", label = "skill_icon", filter = "t:Texture2D", forceSprite = true, groupName = null, excludeSubDir = null },
        new DirConfig { dir = "Assets/Art/Sprites/Head", addressPrefix = "Assets/Art/Sprites/Head", label = "avatar", filter = "t:Texture2D", forceSprite = true, groupName = null, excludeSubDir = null },
        new DirConfig { dir = "Assets/Prefabs/UI", addressPrefix = "Assets/Prefabs/UI", label = "ui_prefab", filter = "t:Prefab", forceSprite = false, groupName = null, excludeSubDir = null },
        new DirConfig { dir = "Assets/Prefabs/Maps", addressPrefix = "Assets/Prefabs/Maps", label = "map_prefab", filter = "t:Prefab", forceSprite = false, groupName = "Maps", excludeSubDir = null },
        new DirConfig { dir = "Assets/Prefabs/MapObjects", addressPrefix = "Assets/Prefabs/MapObjects", label = "mapobject_prefab", filter = "t:Prefab", forceSprite = false, groupName = "MapObjects", excludeSubDir = null },
        new DirConfig { dir = "Assets/Prefabs/player", addressPrefix = "Assets/Prefabs/player", label = "player_prefab", filter = "t:Prefab", forceSprite = false, groupName = "DialogCharacters", excludeSubDir = null },
        new DirConfig { dir = "Assets/Prefabs/npc", addressPrefix = "Assets/Prefabs/npc", label = "npc_prefab", filter = "t:Prefab", forceSprite = false, groupName = "DialogCharacters", excludeSubDir = "talk" },
        new DirConfig { dir = "Assets/Prefabs/npc/talk", addressPrefix = "Assets/Prefabs/npc/talk", label = "dialog_character", filter = "t:Prefab", forceSprite = false, groupName = "DialogCharacters", excludeSubDir = null, removeLabel = "npc_prefab" },
        new DirConfig { dir = "Assets/Prefabs/Battle", addressPrefix = "Assets/Prefabs/Battle", label = "battle_prefab", filter = "t:Prefab", forceSprite = false, groupName = "DialogCharacters", excludeSubDir = null },
        new DirConfig { dir = "Assets/Scenes", addressPrefix = "Assets/Scenes", label = "scene", filter = "t:Scene", forceSprite = false, groupName = "Scenes", excludeSubDir = "Test" },
    };

    static readonly HashSet<string> _excludeSceneNames = new HashSet<string>
    {
        "Scene_Dialogue",
    };

    const string MapRootPrefabDir = "Assets/Prefabs/UI/map";
    const string MapRootAddressPrefix = "Assets/Prefabs/UI/map";

    [MenuItem("Tools/自动设置 Addressable")]
    public static void Setup()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[AddressableAutoSetup] Addressable Settings 未找到，请先创建");
            return;
        }

        EnsureDirectories();
        FixStartupScenes(settings);
        EnsureGroupSchemas(settings);

        int added = 0;

        foreach (var cfg in _configs)
        {
            if (!Directory.Exists(cfg.dir))
            {
                Debug.LogWarning($"[AddressableAutoSetup] 目录不存在，跳过: {cfg.dir}");
                continue;
            }

            var group = string.IsNullOrEmpty(cfg.groupName)
                ? settings.DefaultGroup
                : FindOrCreateGroup(settings, cfg.groupName);

            added += SetupDirectory(settings, group, cfg.dir, cfg.addressPrefix, cfg.filter, cfg.label, cfg.forceSprite, cfg.excludeSubDir, cfg.removeLabel);
        }

        added += SetupMapRoots(settings);

        AssetDatabase.Refresh();
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AddressableAutoSetup] 完成，新增/更新 {added} 个 Addressable 条目");
    }

    static void EnsureDirectories()
    {
        string[] dirs = { "Assets/Art/DialogImages", "Assets/Art/Sprites/Head", "Assets/Prefabs/Maps", "Assets/Prefabs/MapObjects" };
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[AddressableAutoSetup] 创建目录: {dir}");
            }
        }
    }

    static AddressableAssetGroup FindOrCreateGroup(AddressableAssetSettings settings, string groupName)
    {
        var group = settings.FindGroup(groupName);
        if (group != null)
            return group;

        group = settings.CreateGroup(groupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

        var bundledSchema = group.GetSchema<BundledAssetGroupSchema>();
        if (bundledSchema != null)
        {
            bundledSchema.BuildPath.SetVariableByName(settings, "Local.BuildPath");
            bundledSchema.LoadPath.SetVariableByName(settings, "Local.LoadPath");
            bundledSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            bundledSchema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
        }

        Debug.Log($"[AddressableAutoSetup] 创建组: {groupName}");
        return group;
    }

    static void EnsureGroupSchemas(AddressableAssetSettings settings)
    {
        string[] groupsToCheck = { "DialogCharacters", "Maps", "MapObjects", "Scenes" };
        foreach (var groupName in groupsToCheck)
        {
            var group = settings.FindGroup(groupName);
            if (group == null)
                continue;

            bool hasBundled = group.Schemas.Any(s => s is BundledAssetGroupSchema);

            if (!hasBundled)
            {
                var bundledSchema = group.AddSchema<BundledAssetGroupSchema>();
                bundledSchema.BuildPath.SetVariableByName(settings, "Local.BuildPath");
                bundledSchema.LoadPath.SetVariableByName(settings, "Local.LoadPath");
                bundledSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                bundledSchema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;

                if (!group.HasSchema<ContentUpdateGroupSchema>())
                    group.AddSchema<ContentUpdateGroupSchema>();

                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, group, true);
                Debug.Log($"[AddressableAutoSetup] 已为 {groupName} 组添加 BundledAssetGroupSchema + ContentUpdateGroupSchema");
            }
        }
    }

    static int SetupDirectory(AddressableAssetSettings settings, AddressableAssetGroup group,
        string dir, string addressPrefix, string filter, string label, bool forceSprite, string excludeSubDir = null, string removeLabel = null)
    {
        int count = 0;
        var guids = AssetDatabase.FindAssets(filter, new[] { dir });

        if (label != null && !settings.GetLabels().Contains(label))
            settings.AddLabel(label);

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);

            if (excludeSubDir != null && path.Contains($"/{excludeSubDir}/"))
                continue;

            if (filter == "t:Scene")
            {
                string sceneName = Path.GetFileNameWithoutExtension(path);
                if (_excludeSceneNames.Contains(sceneName))
                {
                    Debug.Log($"  跳过场景（需保留在 Build Settings）: {sceneName}");
                    continue;
                }
            }

            var relativePath = path.Substring(dir.Length + 1);
            var relativeNoExt = Path.ChangeExtension(relativePath, null);
            var address = $"{addressPrefix}/{relativeNoExt}";

            if (forceSprite)
                EnsureSpriteImport(path);

            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = address;

            if (label != null)
                entry.SetLabel(label, true);

            if (removeLabel != null)
                entry.SetLabel(removeLabel, false);

            count++;
            Debug.Log($"  设置: {address} → {path}" + (label != null ? $" [label={label}]" : "") + $" [group={group.Name}]");
        }

        return count;
    }

    static void EnsureSpriteImport(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        if (importer.textureType == TextureImporterType.Sprite)
            return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();

        Debug.Log($"  修正导入: {assetPath} → Sprite");
    }

    static void FixStartupScenes(AddressableAssetSettings settings)
    {
        foreach (var sceneName in _excludeSceneNames)
        {
            string scenePath = $"Assets/Scenes/{sceneName}.unity";
            var guid = AssetDatabase.AssetPathToGUID(scenePath);
            if (string.IsNullOrEmpty(guid)) continue;

            var entry = settings.FindAssetEntry(guid);
            if (entry != null)
            {
                settings.RemoveAssetEntry(guid);
                Debug.Log($"[AddressableAutoSetup] 从 Addressables 移除启动场景: {sceneName}");
            }
        }
    }

    static int SetupMapRoots(AddressableAssetSettings settings)
    {
        if (!Directory.Exists(MapRootPrefabDir))
            return 0;

        int count = 0;
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { MapRootPrefabDir });
        var group = settings.DefaultGroup;

        if (!settings.GetLabels().Contains("maproot"))
            settings.AddLabel("maproot");

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            if (prefab.GetComponent<MapRoot>() == null) continue;

            var prefabName = prefab.name;
            var address = $"{MapRootAddressPrefix}/{prefabName}";

            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = address;
            entry.SetLabel("maproot", true);

            count++;
            Debug.Log($"  [MapRoot] 设置: {address} → {path}");
        }

        return count;
    }
}
