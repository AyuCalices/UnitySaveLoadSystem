using System.Collections.Generic;
using System.Linq;
using SaveLoadSystem.Core.UnityComponent.SavableConverter;
using SaveLoadSystem.Utility;
using SaveLoadSystem.Utility.PreventReset;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace SaveLoadSystem.Core.UnityComponent
{
    [DisallowMultipleComponent]
    public class Savable : MonoBehaviour
    {
        [SerializeField] private bool dynamicPrefabSpawningDisabled;
        
        [SerializeField] private NonResetable<string> prefabGuid;
        [SerializeField] private NonResetable<string> savableGuid;
        
        [SerializeField] private NonResetableList<UnityObjectIdentification> savableLookup = new();
        [SerializeField] private NonResetableList<UnityObjectIdentification> duplicateComponentLookup = new();

        
        public string SavableGuid
        {
            get => savableGuid;
            internal set => savableGuid.value = value;
        }

        public string PrefabGuid
        {
            get => prefabGuid;
            internal set => prefabGuid.value = value;
        }

        public bool DynamicPrefabSpawningDisabled => dynamicPrefabSpawningDisabled;
        public List<UnityObjectIdentification> SavableLookup => savableLookup;
        public List<UnityObjectIdentification> DuplicateComponentLookup => duplicateComponentLookup;

        
        private SaveSceneManager _saveSceneManager;


        private void Awake()
        {
            RegisterToSceneManager();
        }

        private void OnDestroy()
        {
            UnregisterFromSceneManager();
        }

        private void OnValidate()
        {
            if (Application.isPlaying) return;
            
            RegisterToSceneManager();
            
            /*
             * Currently the system only supports adding savable-components during editor mode.
             * This is by design, to prevent the necessary to save the type of the component.
             * optional todo: find a way to save added savable-components similar to dynamic prefab spawning without saving the type.
             */
            UpdateSavableComponents();
            UpdateSavableReferenceComponents();
            SetDirty(this);
        }

        private bool AcquireSceneManager()
        {
            if (_saveSceneManager.IsUnityNull())
            {
                var sceneManagers = FindObjectsOfType<SaveSceneManager>();
                _saveSceneManager = sceneManagers.First(x => x.gameObject.scene == gameObject.scene);
                
                if (_saveSceneManager.IsUnityNull())
                {
                    Debug.LogWarning("[SaveMate] SaveSceneManager is missing in the current scene. Please ensure a SaveSceneManager is present to enable proper functionality.");
                    return false;
                }
            }

            return true;
        }
        
        private void RegisterToSceneManager()
        {
            if (gameObject.scene.IsValid() && AcquireSceneManager())
            {
                _saveSceneManager.RegisterSavable(this);
            }
            else
            {
                SavableGuid = null;
            }
        }
        
        private void UnregisterFromSceneManager()
        {
            if (!_saveSceneManager.IsUnityNull() && gameObject.scene.IsValid())
            {
                _saveSceneManager.UnregisterSavable(this);
            }
        }

        private void UpdateSavableComponents()
        {
            //if setting this dirty, the hierarchy changed event will trigger, resulting in an update behaviour
            var foundElements = TypeUtility.GetComponentsWithTypeCondition(gameObject, TypeUtility.ContainsType<ISavable>);
            
            //update removed elements and those that are kept 
            for (var index = SavableLookup.Count - 1; index >= 0; index--)
            {
                var objectId = SavableLookup[index];
                
                if (!foundElements.Exists(x => x == objectId.unityObject))
                {
                    SavableLookup.Remove(objectId);
                }
                else
                {
                    if (string.IsNullOrEmpty(objectId.guid))
                    {
                        SavableLookup[index].guid = GetUniqueSavableID((Component)objectId.unityObject);
                    }
                    
                    foundElements.Remove((Component)objectId.unityObject);
                }
            }

            //add new elements
            foreach (var foundElement in foundElements) 
            {
                var guid = GetUniqueSavableID(foundElement);
                
                SavableLookup.Add(new UnityObjectIdentification(guid, foundElement));
            }
        }

        private string GetUniqueSavableID(Component component)
        {
            var guid = "Component_" + component.name + "_" + SaveLoadUtility.GenerateId();
            
            while (SavableLookup != null && SavableLookup.Exists(x => x.guid == guid))
            {
                guid = "Component_" + component.name + "_" + SaveLoadUtility.GenerateId();
            }

            return guid;
        }
        
        private void UpdateSavableReferenceComponents()
        {
            var duplicates = UnityUtility.GetDuplicateComponents(gameObject);

            //remove duplicates, that implement the savable component: all savables need an id
            for (var index = duplicates.Count - 1; index >= 0; index--)
            {
                var duplicate = duplicates[index];
                if (duplicate is ISavable)
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
                        DuplicateComponentLookup[index].guid = GetUniqueDuplicateID((Component)objectId.unityObject);
                    }
                    
                    duplicates.Remove((Component)objectId.unityObject);
                }
            }
            
            //add new elements
            foreach (var foundElement in duplicates) 
            {
                var guid = GetUniqueSavableID(foundElement);
                
                DuplicateComponentLookup.Add(new UnityObjectIdentification(guid, foundElement));
            }
        }
        
        private string GetUniqueDuplicateID(Component component)
        {
            var guid = "Component_" + component.name + "_" + SaveLoadUtility.GenerateId();
            
            while (DuplicateComponentLookup != null && DuplicateComponentLookup.Exists(x => x.guid == guid))
            {
                guid = "Component_" + component.name + "_" + SaveLoadUtility.GenerateId();
            }

            return guid;
        }
        
        private void SetDirty(Object obj)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(obj);
            }
#endif
        }
    }
}
