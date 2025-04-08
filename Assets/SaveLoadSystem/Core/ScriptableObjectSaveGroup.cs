using System.Collections.Generic;
using SaveLoadSystem.Core.DataTransferObject;
using SaveLoadSystem.Core.EventHandler;
using SaveLoadSystem.Core.UnityComponent.SavableConverter;
using SaveLoadSystem.Utility;
using UnityEditor;
using UnityEngine;

namespace SaveLoadSystem.Core
{
    [CreateAssetMenu]
    public class ScriptableObjectSaveGroupElement : ScriptableObject, 
        ICaptureSnapshotGroupElement, IBeforeCaptureSnapshotHandler, IAfterCaptureSnapshotHandler, 
        IRestoreSnapshotGroupElement, ISaveMateBeforeLoadHandler, ISaveMateAfterLoadHandler
    {
        [SerializeField] private List<string> searchInFolders = new();
        [SerializeField] private List<ScriptableObject> pathBasedScriptableObjects = new();
        [SerializeField] private List<ScriptableObject> customAddedScriptableObjects = new();
        
        public string SceneName => RootSaveData.GlobalSaveDataName;
        
        private static readonly HashSet<ScriptableObject> _savedScriptableObjectsLookup = new();

        
        #region Editor Behaviour

        
        private void OnValidate()
        {
            UpdateFolderSelectScriptableObject();
            
            UnityUtility.SetDirty(this);
        }

        private void UpdateFolderSelectScriptableObject()
        {
            var newScriptableObjects = GetScriptableObjectSavables(searchInFolders.ToArray());

            foreach (var newScriptableObject in newScriptableObjects)
            {
                if (!pathBasedScriptableObjects.Contains(newScriptableObject))
                {
                    pathBasedScriptableObjects.Add(newScriptableObject);
                }
            }

            for (var index = pathBasedScriptableObjects.Count - 1; index >= 0; index--)
            {
                var currentScriptableObject = pathBasedScriptableObjects[index];
                if (!newScriptableObjects.Contains(currentScriptableObject))
                {
                    pathBasedScriptableObjects.Remove(currentScriptableObject);
                }
            }
        }
        
        private static List<ScriptableObject> GetScriptableObjectSavables(string[] filter)
        {
            List<ScriptableObject> foundObjects = new();
            
            var guids = AssetDatabase.FindAssets("t:ScriptableObject", filter);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                if (asset is ISavable)
                {
                    foundObjects.Add(asset);
                }
            }

            return foundObjects;
        }

        
        #endregion

        #region CaptureSnapshot

        
        public void OnBeforeCaptureSnapshot()
        {
            foreach (var pathBasedScriptableObject in pathBasedScriptableObjects)
            {
                if (pathBasedScriptableObject is IBeforeCaptureSnapshotHandler handler)
                {
                    handler.OnBeforeCaptureSnapshot();
                }
            }

            foreach (var customAddedScriptableObject in customAddedScriptableObjects)
            {
                if (customAddedScriptableObject is IBeforeCaptureSnapshotHandler handler)
                {
                    handler.OnBeforeCaptureSnapshot();
                }
            }
        }
        

        public void CaptureSnapshot(SaveLoadManager saveLoadManager)
        {
            foreach (var pathBasedScriptableObject in pathBasedScriptableObjects)
            {
                CaptureScriptableObjectSnapshot(saveLoadManager, pathBasedScriptableObject);
            }

            foreach (var customAddedScriptableObject in customAddedScriptableObjects)
            {
                CaptureScriptableObjectSnapshot(saveLoadManager, customAddedScriptableObject);
            }
        }
        
        public void OnAfterCaptureSnapshot()
        {
            _savedScriptableObjectsLookup.Clear();
            
            foreach (var pathBasedScriptableObject in pathBasedScriptableObjects)
            {
                if (pathBasedScriptableObject is IAfterCaptureSnapshotHandler handler)
                {
                    handler.OnAfterCaptureSnapshot();
                }
            }

            foreach (var customAddedScriptableObject in customAddedScriptableObjects)
            {
                if (customAddedScriptableObject is IAfterCaptureSnapshotHandler handler)
                {
                    handler.OnAfterCaptureSnapshot();
                }
            }
        }

        private void CaptureScriptableObjectSnapshot(SaveLoadManager saveLoadManager, ScriptableObject scriptableObject)
        {
            //make sure scriptable objects are only saved once each snapshot
            if (!_savedScriptableObjectsLookup.Add(scriptableObject)) return;

            if (!saveLoadManager.ScriptableObjectToGuidLookup.TryGetValue(scriptableObject, out var guidPath))
            {
                //TODO: error handling
                return;
            }
            
            var saveLink = saveLoadManager.CurrentSaveFileContext;
            
            if (!TypeUtility.TryConvertTo(scriptableObject, out ISavable targetSavable)) return;
                
            var leafSaveData = new LeafSaveData();
            saveLink.RootSaveData.GlobalSaveData.UpsertLeafSaveData(guidPath, leafSaveData);

            targetSavable.OnSave(new SaveDataHandler(saveLink.RootSaveData, leafSaveData, guidPath, RootSaveData.GlobalSaveDataName, 
                saveLink, saveLoadManager));
                
            saveLink.SoftLoadedObjects.Remove(scriptableObject);
        }

        
        #endregion
        
        #region RestoreSnapshot

        
        public void OnBeforeRestoreSnapshot()
        {
            foreach (var pathBasedScriptableObject in pathBasedScriptableObjects)
            {
                if (pathBasedScriptableObject is ISaveMateBeforeLoadHandler handler)
                {
                    handler.OnBeforeRestoreSnapshot();
                }
            }

            foreach (var customAddedScriptableObject in customAddedScriptableObjects)
            {
                if (customAddedScriptableObject is ISaveMateBeforeLoadHandler handler)
                {
                    handler.OnBeforeRestoreSnapshot();
                }
            }
        }

        public void OnPrepareSnapshotObjects(SaveLoadManager saveLoadManager, LoadType loadType) { }

        public void RestoreSnapshot(SaveLoadManager saveLoadManager, LoadType loadType)
        {
            foreach (var pathBasedScriptableObject in pathBasedScriptableObjects)
            {
                RestoreScriptableObjectSnapshot(saveLoadManager, loadType, pathBasedScriptableObject);
            }

            foreach (var customAddedScriptableObject in customAddedScriptableObjects)
            {
                RestoreScriptableObjectSnapshot(saveLoadManager, loadType, customAddedScriptableObject);
            }
        }

        private void RestoreScriptableObjectSnapshot(SaveLoadManager saveLoadManager, LoadType loadType, ScriptableObject scriptableObject)
        {
            var saveLink = saveLoadManager.CurrentSaveFileContext;

            //return if it cant be loaded due to soft loading
            if (loadType != LoadType.Hard && saveLink.SoftLoadedObjects.Contains(scriptableObject)) return;

            if (!saveLoadManager.ScriptableObjectToGuidLookup.TryGetValue(scriptableObject, out var guidPath))
            {
                //TODO: error handling
                return;
            }
            
            var rootSaveData = saveLink.RootSaveData;
            
            // Skip the scriptable object, if it contains references to scene's, that arent active
            if (rootSaveData.GlobalSaveData.Elements.TryGetValue(guidPath, out var leafSaveData))
            {
                if (!ScenesForGlobalLeafSaveDataAreLoaded(saveLoadManager.GetTrackedSaveSceneManagers(), leafSaveData))
                {
                    Debug.LogWarning($"Skipped ScriptableObject '{scriptableObject.name}' for saving, because of a scene requirement. ScriptableObject GUID: '{guidPath.ToString()}'");
                    return;
                }
            }

            //restore snapshot data
            if (!rootSaveData.GlobalSaveData.TryGetLeafSaveData(guidPath, out var instanceSaveData)) return;
            
            if (!TypeUtility.TryConvertTo(scriptableObject, out ISavable targetSavable)) return;
                    
            var loadDataHandler = new LoadDataHandler(rootSaveData, rootSaveData.GlobalSaveData, instanceSaveData, loadType, 
                RootSaveData.GlobalSaveDataName, saveLink, saveLoadManager);
            
            targetSavable.OnLoad(loadDataHandler);
                    
            saveLink.SoftLoadedObjects.Add(scriptableObject);
        }

        public void OnAfterRestoreSnapshot()
        {
            foreach (var pathBasedScriptableObject in pathBasedScriptableObjects)
            {
                if (pathBasedScriptableObject is ISaveMateAfterLoadHandler handler)
                {
                    handler.OnAfterRestoreSnapshot();
                }
            }

            foreach (var customAddedScriptableObject in customAddedScriptableObjects)
            {
                if (customAddedScriptableObject is ISaveMateAfterLoadHandler handler)
                {
                    handler.OnAfterRestoreSnapshot();
                }
            }
        }
        
        private bool ScenesForGlobalLeafSaveDataAreLoaded(List<SimpleSceneSaveManager> requiredScenes, LeafSaveData leafSaveData)
        {
            foreach (var referenceGuidPath in leafSaveData.References.Values)
            {
                if (!requiredScenes.Exists(x => x.SceneName == referenceGuidPath.Scene) && 
                    referenceGuidPath.Scene != RootSaveData.GlobalSaveDataName) return false;
            }

            return true;
        }

        
        #endregion
    }
}
