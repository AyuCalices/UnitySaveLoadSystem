using System.Collections.Generic;
using SaveLoadSystem.Core.Component;
using SaveLoadSystem.Core.Component.SavableConverter;
using SaveLoadSystem.Utility;
using UnityEditor;
using UnityEngine;

namespace SaveLoadSystem.Core
{
#if UNITY_EDITOR
    
    [InitializeOnLoad]
    public class AssetRegistryGenerator : AssetPostprocessor
    {
        private static List<AssetRegistry> _cachedAssetRegistries;
        
        static AssetRegistryGenerator()
        {
            EditorApplication.delayCall += LoadSavables;
        }
        
        private static void LoadSavables()
        {
            var assetRegistries = GetAssetRegistries();

            ProcessAll(assetRegistries);
            
            EditorApplication.delayCall -= LoadSavables;
        }
        
        //help of fishnet code
        private static List<AssetRegistry> GetAssetRegistries()
        {
            //If cached is null try to get it.
            if (_cachedAssetRegistries == null)
            {
                var guids = AssetDatabase.FindAssets($"t:{nameof(AssetRegistry)}");

                var assetRegistry = new List<AssetRegistry>();
                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    assetRegistry.Add(AssetDatabase.LoadAssetAtPath<AssetRegistry>(assetPath));
                }

                _cachedAssetRegistries = assetRegistry;
            }

            return _cachedAssetRegistries;
        }

        private static void ProcessAll(List<AssetRegistry> assetRegistries)
        {
            if (assetRegistries == null || assetRegistries.Count == 0) 
                return;
            
            ProcessAllPrefabs(assetRegistries);
            ProcessAllScriptableObjects(assetRegistries);
        }
        
        private static void ProcessAllPrefabs(List<AssetRegistry> assetRegistries)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab");
            
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var savablePrefab = AssetDatabase.LoadAssetAtPath<Savable>(assetPath);
                
                if (savablePrefab != null)
                {
                    foreach (var registry in assetRegistries)
                    {
                        registry.AddSavablePrefab(savablePrefab, assetPath);
                    }
                }
            }
        }

        private static void ProcessAllScriptableObjects(List<AssetRegistry> assetRegistries)
        {
            // Get all ScriptableObject asset paths
            var guids = AssetDatabase.FindAssets("t:ScriptableObject");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                if (asset is ISavable)
                {
                    foreach (var assetRegistry in assetRegistries)
                    {
                        assetRegistry.AddSavableScriptableObject(asset, path);
                    }
                }
            }
        }
        
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (Application.isPlaying) return;
            
            /* Don't iterate if updating or compiling as that could cause an infinite loop
             * due to the prefabs being generated during an update, which causes the update
             * to start over, which causes the generator to run again, which... you get the idea. */
            if (EditorApplication.isCompiling)
                return;
            
            var assetRegistries = GetAssetRegistries();

            CleanupCachedAssetRegistries(assetRegistries);
            
            if (assetRegistries is { Count: > 0 })
            {
                PostprocessPrefabs(assetRegistries, importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
                PostprocessScriptableObjects(assetRegistries, importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
            }

            UpdateCachedAssetRegistries(assetRegistries, importedAssets);
        }

        private static void PostprocessPrefabs(List<AssetRegistry> assetRegistries, string[] importedAssets, 
            string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var importedAsset in importedAssets)
            {
                var savablePrefab = AssetDatabase.LoadAssetAtPath<Savable>(importedAsset);
                if (savablePrefab != null)
                {
                    foreach (var assetRegistry in assetRegistries)
                    {
                        assetRegistry.AddSavablePrefab(savablePrefab, importedAsset);
                    }
                }
            }

            foreach (var deletedAsset in deletedAssets)
            {
                foreach (var assetRegistry in assetRegistries)
                {
                    assetRegistry.RemoveSavablePrefab(deletedAsset);
                }
            }

            for (var i = 0; i < movedAssets.Length; i++)
            {
                foreach (var assetRegistry in assetRegistries)
                {
                    assetRegistry.ChangePrefabGuid(movedFromAssetPaths[i], movedAssets[i]);
                }
            }
        }
        
        private static void PostprocessScriptableObjects(List<AssetRegistry> assetRegistries, string[] importedAssets, 
            string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var importedAsset in importedAssets)
            {
                var savablePrefab = AssetDatabase.LoadAssetAtPath<ScriptableObject>(importedAsset);
                if (savablePrefab != null && savablePrefab is ISavable)
                {
                    foreach (var assetRegistry in assetRegistries)
                    {
                        assetRegistry.AddSavableScriptableObject(savablePrefab, importedAsset);
                    }
                }
            }

            foreach (var deletedAsset in deletedAssets)
            {
                foreach (var assetRegistry in assetRegistries)
                {
                    assetRegistry.RemoveSavableScriptableObject(deletedAsset);
                }
            }

            for (var i = 0; i < movedAssets.Length; i++)
            {
                foreach (var assetRegistry in assetRegistries)
                {
                    assetRegistry.ChangeScriptableObjectGuid(movedFromAssetPaths[i], movedAssets[i]);
                }
            }
        }

        /// <summary>
        /// Remove deleted asset registries
        /// </summary>
        /// <param name="assetRegistries"></param>
        private static void CleanupCachedAssetRegistries(List<AssetRegistry> assetRegistries)
        {
            if (assetRegistries == null) return;
            
            for (var index = assetRegistries.Count - 1; index >= 0; index--)
            {
                if (assetRegistries[index].IsUnityNull())
                {
                    //Debug.Log($"Removing deleted AssetRegistry at index {index}");
                    assetRegistries.RemoveAt(index);
                }
            }
        }

        private static void UpdateCachedAssetRegistries(List<AssetRegistry> assetRegistries, string[] importedAssets)
        {
            // Check for newly created AssetRegistry instances
            var newAssetRegistries = new List<AssetRegistry>();
            foreach (var importedAsset in importedAssets)
            {
                var newAssetRegistry = AssetDatabase.LoadAssetAtPath<AssetRegistry>(importedAsset);
                
                if (newAssetRegistry != null)
                {
                    newAssetRegistries.Add(newAssetRegistry);
                    
                }
            }

            if (newAssetRegistries.Count > 0)
            {
                //Debug.Log($"Processing {newAssetRegistries.Count} new AssetRegistry instances");
                ProcessAll(newAssetRegistries);
                
                assetRegistries ??= new List<AssetRegistry>();
                assetRegistries.AddRange(newAssetRegistries);
            }
        }
    }
    
#endif
}
