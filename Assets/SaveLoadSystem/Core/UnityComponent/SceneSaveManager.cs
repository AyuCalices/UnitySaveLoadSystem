using System;
using System.Collections.Generic;
using System.Linq;
using SaveLoadSystem.Utility;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace SaveLoadSystem.Core.UnityComponent
{
    public class SceneSaveManager : SimpleSceneSaveManager, IGetCaptureSnapshotGroupElementHandler, IGetRestoreSnapshotGroupElementHandler
    {
        [SerializeField] private SaveLoadManager saveLoadManager;
        [SerializeField] private LoadType defaultLoadType;
        
        [Header("Save and Load Link")]
        [SerializeField] private ScriptableObjectSaveGroupElement scriptableObjectsToSave;
        [SerializeField] private bool additionallySaveDontDestroyOnLoad;

        [Header("Unity Lifecycle Events")] 
        [SerializeField] private bool loadSceneOnEnable;
        [SerializeField] private SaveSceneManagerDestroyType saveSceneOnDisable;
        [SerializeField] private bool saveActiveScenesOnApplicationQuit;
        
        [Header("Save Events")]
        [SerializeField] private SceneManagerEvents sceneManagerEvents;
        
        //snapshot and loading
        private static bool _hasSavedActiveScenesThisFrame;
        
        
        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            
            saveLoadManager.RegisterSaveSceneManager(this);
        }
        
        private void OnEnable()
        {
            if (loadSceneOnEnable)
            {
                LoadScene();
            }
        }
        
        protected override void OnValidate()
        {
            base.OnValidate();
            
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            
            saveLoadManager?.RegisterSaveSceneManager(this);
        }
        
        protected override void Update()
        {
            base.Update();
            
            if (_hasSavedActiveScenesThisFrame)
            {
                _hasSavedActiveScenesThisFrame = false;
            }
        }

        private void OnDisable()
        {
            switch (saveSceneOnDisable)
            {
                case SaveSceneManagerDestroyType.SnapshotSingleScene:
                    CaptureSceneSnapshot();
                    break;
                case SaveSceneManagerDestroyType.SnapshotActiveScenes:
                    saveLoadManager.CurrentSaveFileContext.CaptureSnapshotForActiveScenes();
                    break;
                case SaveSceneManagerDestroyType.SaveSingleScene:
                    saveLoadManager.CurrentSaveFileContext.Save(this);
                    break;
                case SaveSceneManagerDestroyType.SaveActiveScenes:
                    if (!_hasSavedActiveScenesThisFrame)
                    {
                        saveLoadManager.CurrentSaveFileContext.SaveActiveScenes();
                        _hasSavedActiveScenesThisFrame = true;
                    }
                    break;
                case SaveSceneManagerDestroyType.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        private void OnDestroy()
        {
            saveLoadManager.UnregisterSaveSceneManager(this);
        }

        private void OnApplicationQuit()
        {
            if (saveActiveScenesOnApplicationQuit && !_hasSavedActiveScenesThisFrame)
            {
                saveLoadManager.CurrentSaveFileContext.SaveActiveScenes();
                _hasSavedActiveScenesThisFrame = true;
            }
        }

        #endregion

        public List<ICaptureSnapshotGroupElement> GetCaptureSnapshotGroupElements()
        {
            var captureSnapshotGroupElements = new List<ICaptureSnapshotGroupElement>();
            
            if (scriptableObjectsToSave)
            {
                captureSnapshotGroupElements.Add(scriptableObjectsToSave);
            }
            
            if (additionallySaveDontDestroyOnLoad)
            {
                captureSnapshotGroupElements.Add(SaveLoadManager.GetDontDestroyOnLoadSceneManager());
            }
            
            return captureSnapshotGroupElements;
        }

        public List<IRestoreSnapshotGroupElement> GetRestoreSnapshotGroupElements()
        {
            var restoreSnapshotGroupElements = new List<IRestoreSnapshotGroupElement>();
            
            if (additionallySaveDontDestroyOnLoad)
            {
                restoreSnapshotGroupElements.Add(SaveLoadManager.GetDontDestroyOnLoadSceneManager());
            }

            if (scriptableObjectsToSave)
            {
                restoreSnapshotGroupElements.Add(scriptableObjectsToSave);
            }
            
            return restoreSnapshotGroupElements;
        }
        
        #region SaveLoad Methods


        [ContextMenu("Capture Scene Snapshot")]
        public void CaptureSceneSnapshot()
        {
            saveLoadManager.CurrentSaveFileContext.CaptureSnapshot(this);
        }

        [ContextMenu("Write To Disk")]
        public void WriteToDisk()
        {
            saveLoadManager.CurrentSaveFileContext.WriteToDisk();
        }
        
        [ContextMenu("Save Scene")]
        public void SaveScene()
        {
            saveLoadManager.CurrentSaveFileContext.Save(this);
        }

        [ContextMenu("Restore Scene Snapshot")]
        public void RestoreSceneSnapshot()
        {
            saveLoadManager.CurrentSaveFileContext.RestoreSnapshot(defaultLoadType, this);
        }
        
        public void RestoreSceneSnapshot(LoadType loadType)
        {
            saveLoadManager.CurrentSaveFileContext.RestoreSnapshot(loadType, this);
        }
        
        [ContextMenu("Load Scene")]
        public void LoadScene()
        {
            saveLoadManager.CurrentSaveFileContext.Load(defaultLoadType, this);
        }
        
        public void LoadScene(LoadType loadType)
        {
            saveLoadManager.CurrentSaveFileContext.Load(loadType, this);
        }
        
        [ContextMenu("Delete Scene Snapshot Data")]
        public void DeleteSceneSnapshotData()
        {
            saveLoadManager.CurrentSaveFileContext.DeleteSnapshotData(this);
        }
        
        [ContextMenu("Delete Scene Data")]
        public void DeleteSceneData()
        {
            saveLoadManager.CurrentSaveFileContext.Delete(this);
        }

        [ContextMenu("Reload Scene")]
        public void ReloadScene()
        {
            saveLoadManager.CurrentSaveFileContext.ReloadScenes(this);
        }
        
        [ContextMenu("UnloadScene")]
        public void UnloadSceneAsync()
        {
            SceneManager.UnloadSceneAsync(SceneName);
        }
        
        
        #endregion
        
        #region Event System
        
        //TODO: maybe swap to observer for internal visibillity (preferred, cause use should not call these event methods)

        public override void OnBeforeCaptureSnapshot()
        {
            base.OnBeforeCaptureSnapshot();
            
            sceneManagerEvents.onBeforeSnapshot.Invoke();
        }
        
        public override void OnAfterCaptureSnapshot()
        {
            base.OnAfterCaptureSnapshot();
            
            sceneManagerEvents.onAfterSnapshot.Invoke();
        }
        
        public override void OnBeforeRestoreSnapshot()
        {
            base.OnBeforeRestoreSnapshot();
            
            sceneManagerEvents.onBeforeLoad.Invoke();
        }
        
        public override void OnAfterRestoreSnapshot()
        {
            base.OnAfterRestoreSnapshot();
            
            sceneManagerEvents.onAfterLoad.Invoke();
        }
        
        internal override void OnBeforeDeleteDiskData()
        {
            base.OnBeforeDeleteDiskData();
            
            sceneManagerEvents.onBeforeDeleteDiskData.Invoke();
        }
        
        internal override void OnAfterDeleteDiskData()
        {
            base.OnAfterDeleteDiskData();
            
            sceneManagerEvents.onAfterDeleteDiskData.Invoke();
        }
        
        internal override void OnBeforeWriteToDisk()
        {
            base.OnBeforeWriteToDisk();
            
            sceneManagerEvents.onBeforeWriteToDisk.Invoke();
        }
        
        internal override void OnAfterWriteToDisk()
        {
            base.OnAfterWriteToDisk();
            
            sceneManagerEvents.onAfterWriteToDisk.Invoke();
        }

        
        #endregion
        
        #region Private Classes
        
        [Serializable]
        private class SceneManagerEvents
        {
            public UnityEvent onBeforeSnapshot;
            public UnityEvent onAfterSnapshot;
            
            public UnityEvent onBeforeLoad;
            public UnityEvent onAfterLoad;
            
            public UnityEvent onBeforeDeleteDiskData;
            public UnityEvent onAfterDeleteDiskData;
            
            public UnityEvent onBeforeWriteToDisk;
            public UnityEvent onAfterWriteToDisk;
        }
        
        public void RegisterAction(UnityAction action, SceneManagerEventType firstEventType, params SceneManagerEventType[] additionalEventTypes)
        {
            foreach (var selectionViewEventType in additionalEventTypes.Append(firstEventType))
            {
                switch (selectionViewEventType)
                {
                    case SceneManagerEventType.OnBeforeSnapshot:
                        sceneManagerEvents.onBeforeSnapshot.AddListener(action);
                        break;
                    case SceneManagerEventType.OnAfterSnapshot:
                        sceneManagerEvents.onAfterSnapshot.AddListener(action);
                        break;
                    case SceneManagerEventType.OnBeforeLoad:
                        sceneManagerEvents.onBeforeLoad.AddListener(action);
                        break;
                    case SceneManagerEventType.OnAfterLoad:
                        sceneManagerEvents.onAfterLoad.AddListener(action);
                        break;
                    case SceneManagerEventType.OnBeforeDeleteDiskData:
                        sceneManagerEvents.onBeforeDeleteDiskData.AddListener(action);
                        break;
                    case SceneManagerEventType.OnAfterDeleteDiskData:
                        sceneManagerEvents.onAfterDeleteDiskData.AddListener(action);
                        break;
                    case SceneManagerEventType.OnBeforeWriteToDisk:
                        sceneManagerEvents.onBeforeWriteToDisk.AddListener(action);
                        break;
                    case SceneManagerEventType.OnAfterWriteToDisk:
                        sceneManagerEvents.onAfterWriteToDisk.AddListener(action);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        
        public void UnregisterAction(UnityAction action, SceneManagerEventType firstEventType, params SceneManagerEventType[] additionalEventTypes)
        {
            foreach (var selectionViewEventType in additionalEventTypes.Append(firstEventType))
            {
                switch (selectionViewEventType)
                {
                    case SceneManagerEventType.OnBeforeSnapshot:
                        sceneManagerEvents.onBeforeSnapshot.RemoveListener(action);
                        break;
                    case SceneManagerEventType.OnAfterSnapshot:
                        sceneManagerEvents.onAfterSnapshot.RemoveListener(action);
                        break;
                    case SceneManagerEventType.OnBeforeLoad:
                        sceneManagerEvents.onBeforeLoad.RemoveListener(action);
                        break;
                    case SceneManagerEventType.OnAfterLoad:
                        sceneManagerEvents.onAfterLoad.RemoveListener(action);
                        break;
                    case SceneManagerEventType.OnBeforeDeleteDiskData:
                        sceneManagerEvents.onBeforeDeleteDiskData.RemoveListener(action);
                        break;
                    case SceneManagerEventType.OnAfterDeleteDiskData:
                        sceneManagerEvents.onAfterDeleteDiskData.RemoveListener(action);
                        break;
                    case SceneManagerEventType.OnBeforeWriteToDisk:
                        sceneManagerEvents.onBeforeWriteToDisk.RemoveListener(action);
                        break;
                    case SceneManagerEventType.OnAfterWriteToDisk:
                        sceneManagerEvents.onAfterWriteToDisk.RemoveListener(action);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        
        #endregion
    }
}
