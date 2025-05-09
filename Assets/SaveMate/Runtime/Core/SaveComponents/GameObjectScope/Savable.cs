using System;
using System.Collections.Generic;
using System.Linq;
using SaveMate.Runtime.Core.ObjectChangeEvents;
using SaveMate.Runtime.Core.SaveComponents.ManagingScope;
using SaveMate.Runtime.Core.SaveComponents.SceneScope;
using SaveMate.Runtime.Core.StateSnapshot;
using SaveMate.Runtime.Utility;
using SaveMate.Runtime.Utility.PreventReset;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveMate.Runtime.Core.SaveComponents.GameObjectScope
{
    [DisallowMultipleComponent]
    public class Savable : MonoBehaviour, IChangeGameObjectStructure
    {
        [SerializeField] private bool disablePrefabSpawning;
        
        [SerializeField] private NonResetable<string> prefabGuid;
        [SerializeField] private NonResetable<string> sceneGuid;
        
        [SerializeField] private NonResetableList<UnityObjectIdentification> saveStateHandlers = new();
        [SerializeField] private NonResetableList<UnityObjectIdentification> duplicateComponentLookup = new();
        
        internal static event Action<Savable> OnValidateSavable;
        
        public bool DisablePrefabSpawning => disablePrefabSpawning;
        public string SceneGuid
        {
            get => sceneGuid;
            internal set => sceneGuid.value = value;
        }

        public string PrefabGuid
        {
            get => prefabGuid;
            internal set => prefabGuid.value = value;
        }
        
        internal List<UnityObjectIdentification> SaveStateHandlers => saveStateHandlers;
        internal List<UnityObjectIdentification> DuplicateComponentLookup => duplicateComponentLookup;
        
        
        private Scene _lastScene;
        private SceneSaveManager _sceneSaveManager;

        private void Awake()
        {
            _lastScene = gameObject.scene;
            RegisterToSceneManager();
        }

        private void Update()
        {
            if (gameObject.scene != _lastScene)
            {
                UnregisterFromSceneManager();
                RegisterToSceneManager();
                _lastScene = gameObject.scene;
            }
        }

        private void OnDestroy()
        {
            UnregisterFromSceneManager();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            
            RegisterToSceneManager();
            
            //update prefab guid
            OnValidateSavable?.Invoke(this);
            
            UpdateISaveStateComponents();
            CheckUniqueISaveStateGuidOnInspectorInput();
            
            UpdateDuplicateComponents();
            CheckUniqueDuplicateComponentGuidOnInspectorInput();
            
            SaveLoadUtility.SetDirty(this);
        }
#endif
        
        /*
         * Currently the system only supports adding savable-components during editor mode.
         * This is by design, to prevent the necessary to save the type of the component.
         */
        void IChangeGameObjectStructure.OnChangeGameObjectStructure()
        {
            UpdateISaveStateComponents();
            UpdateDuplicateComponents();
            
            SaveLoadUtility.SetDirty(this);
        }
        
        private void RegisterToSceneManager()
        {
            if (!gameObject.scene.IsValid()) return;

            if (gameObject.scene.name == "DontDestroyOnLoad")
            {
                SaveMateManager.GetDontDestroyOnLoadSceneManager().RegisterSavable(this);
            }
            else if (AcquireSceneManager())
            {
                _sceneSaveManager.RegisterSavable(this);
            }
            else
            {
                SceneGuid = null;
            }
        }
        
        private bool AcquireSceneManager()
        {
            if (_sceneSaveManager.IsUnityNull())
            {
                try
                {
                    var sceneManagers = FindObjectsOfType<SceneSaveManager>();
                    _sceneSaveManager = sceneManagers.First(x => x.gameObject.scene == gameObject.scene);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SaveMate] Internal Error: " + e);
                    return false;
                }
            }

            return true;
        }
        
        private void UnregisterFromSceneManager()
        {
            if (!_sceneSaveManager.IsUnityNull())
            {
                _sceneSaveManager.UnregisterSavable(this);
            }
        }

        private void CheckUniqueISaveStateGuidOnInspectorInput()
        {
            SaveLoadUtility.CheckUniqueGuidOnInspectorInput(saveStateHandlers.values,
                obj => obj.unityObject,
                obj => obj.guid,
                $"Duplicate Guid on GameObject '{gameObject.name}' for different 'ISaveStateHandler Components' detected!");
        }
        
        private void CheckUniqueDuplicateComponentGuidOnInspectorInput()
        {
            SaveLoadUtility.CheckUniqueGuidOnInspectorInput(duplicateComponentLookup.values,
                obj => obj.unityObject,
                obj => obj.guid,
                $"Duplicate Guid on GameObject '{gameObject.name}' for different 'Duplicated Components' detected!");
        }

        private void UpdateISaveStateComponents()
        {
            //if setting this dirty, the hierarchy changed event will trigger, resulting in an update behaviour
            var foundElements = SaveLoadUtility.GetComponentsWithTypeCondition(gameObject, SaveLoadUtility.ContainsType<ISaveStateHandler>);
            
            //update removed elements and those that are kept 
            for (var index = SaveStateHandlers.Count - 1; index >= 0; index--)
            {
                var objectId = SaveStateHandlers[index];
                
                if (!foundElements.Exists(x => x == objectId.unityObject))
                {
                    SaveStateHandlers.Remove(objectId);
                }
                else
                {
                    if (string.IsNullOrEmpty(objectId.guid))
                    {
                        SaveStateHandlers[index].guid = GetUniqueSavableID();
                    }
                    
                    foundElements.Remove((Component)objectId.unityObject);
                }
            }

            //add new elements
            foreach (var foundElement in foundElements) 
            {
                var guid = GetUniqueSavableID();
                
                SaveStateHandlers.Add(new UnityObjectIdentification(guid, foundElement));
            }
        }
        
        private void UpdateDuplicateComponents()
        {
            var duplicates = SaveLoadUtility.GetDuplicateComponents(gameObject);

            //remove duplicates, that implement the savable component: all savables need an id
            for (var index = duplicates.Count - 1; index >= 0; index--)
            {
                var duplicate = duplicates[index];
                if (duplicate is ISaveStateHandler)
                {
                    duplicates.Remove(duplicate);
                }
            }

            for (var index = DuplicateComponentLookup.Count - 1; index >= 0; index--)
            {
                var objectId = DuplicateComponentLookup[index];
                
                if (!duplicates.Exists(x => x == objectId.unityObject))
                {
                    DuplicateComponentLookup.Remove(objectId);
                }
                else
                {
                    
                    if (string.IsNullOrEmpty(objectId.guid))
                    {
                        DuplicateComponentLookup[index].guid = GetUniqueDuplicateID();
                    }
                    
                    duplicates.Remove((Component)objectId.unityObject);
                }
            }
            
            //add new elements
            foreach (var foundElement in duplicates) 
            {
                var guid = GetUniqueSavableID();
                
                DuplicateComponentLookup.Add(new UnityObjectIdentification(guid, foundElement));
            }
        }
        
        private string GetUniqueSavableID()
        {
            var guid = "Component_" + SaveLoadUtility.GenerateId();
            
            while (SaveStateHandlers != null && SaveStateHandlers.Exists(x => x.guid == guid))
            {
                guid = "Component_" + SaveLoadUtility.GenerateId();
            }

            return guid;
        }
        
        private string GetUniqueDuplicateID()
        {
            var guid = "Component_" + SaveLoadUtility.GenerateId();
            
            while (DuplicateComponentLookup != null && DuplicateComponentLookup.Exists(x => x.guid == guid))
            {
                guid = "Component_" + SaveLoadUtility.GenerateId();
            }

            return guid;
        }
    }
}
