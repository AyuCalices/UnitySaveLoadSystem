using System.Collections.Generic;
using SaveLoadSystem.Core.UnityComponent.SavableConverter;
using SaveLoadSystem.Utility;
using UnityEditor;
using UnityEngine;

namespace SaveLoadSystem.Core
{
    [InitializeOnLoad]
    public class SavableScriptableObjectSetup : AssetPostprocessor
    {
        static SavableScriptableObjectSetup()
        {
            AssetRegistryManager.OnAssetRegistriesInitialized += LoadSavables;
            AssetRegistryManager.OnAssetRegistryAdded += ProcessScriptableObjectsForAssetRegistry;
        }
        
        private static void LoadSavables(List<AssetRegistry> assetRegistries)
        {
            if (assetRegistries is { Count: > 0 })
            {
                ProcessAllScriptableObjects(assetRegistries);
            }
            
            AssetRegistryManager.OnAssetRegistriesInitialized -= LoadSavables;
        }
        
        private static void ProcessAllScriptableObjects(List<AssetRegistry> assetRegistries)
        {
            foreach (var assetRegistry in assetRegistries)
            {
                ProcessScriptableObjectsForAssetRegistry(assetRegistry);
            }
        }
        
        private static void ProcessScriptableObjectsForAssetRegistry(AssetRegistry assetRegistry)
        {
            if (assetRegistry == null) return;

            var guids = AssetDatabase.FindAssets("t:ScriptableObject");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                if (asset is ISavable)
                {
                    assetRegistry.AddSavableScriptableObject(asset);
                }
            }
        }
        
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var assetRegistries = AssetRegistryManager.CachedAssetRegistries;
            if (assetRegistries is { Count: > 0 })
            {
                CleanupSavableScriptableObjects(assetRegistries);
                PostprocessScriptableObjects(assetRegistries, importedAssets);
                assetRegistries.ForEach(UnityUtility.SetDirty);
            }
        }
        
        private static void PostprocessScriptableObjects(List<AssetRegistry> assetRegistries, string[] importedAssets)
        {
            foreach (var importedAsset in importedAssets)
            {
                foreach (var assetRegistry in assetRegistries)
                {
                    if (assetRegistry.IsUnityNull()) continue;
                    
                    var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(importedAsset);
                    if (asset != null && asset is ISavable)
                    {
                        assetRegistry.AddSavableScriptableObject(asset);
                    }
                }
            }
        }
        
        private static void CleanupSavableScriptableObjects(List<AssetRegistry> assetRegistries)
        {
            foreach (var assetRegistry in assetRegistries)
            {
                if (assetRegistry.IsUnityNull()) continue;
                
                for (var i = assetRegistry.ScriptableObjectSavables.Count - 1; i >= 0; i--)
                {
                    if (assetRegistry.ScriptableObjectSavables[i].unityObject.IsUnityNull())
                    {
                        assetRegistry.ScriptableObjectSavables.RemoveAt(i);
                    }
                }
            }
        }
    }
}
