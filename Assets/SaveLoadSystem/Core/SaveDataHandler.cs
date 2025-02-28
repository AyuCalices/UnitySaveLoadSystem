using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SaveLoadSystem.Core.Converter;
using SaveLoadSystem.Core.DataTransferObject;
using SaveLoadSystem.Core.UnityComponent.SavableConverter;
using SaveLoadSystem.Utility;
using UnityEngine;

namespace SaveLoadSystem.Core
{
    /// <summary>
    /// The <see cref="SaveDataHandler"/> class is responsible for managing the serialization and storage of data
    /// within the save/load system, specifically handling the addition and reference management of savable objects.
    /// </summary>
    public readonly struct SaveDataHandler
    {
        private readonly GuidPath _guidPath;
        private readonly LeafSaveData _leafSaveData;
        private readonly Dictionary<object, GuidPath> _processedInstancesLookup;
        
        private readonly BranchSaveData _branchSaveData;
        
        private readonly Dictionary<GameObject, GuidPath> _savableGameObjectToGuidLookup;
        private readonly Dictionary<ScriptableObject, GuidPath> _scriptableObjectToGuidLookup;
        private readonly Dictionary<Component, GuidPath> _componentToGuidLookup;

        public SaveDataHandler(BranchSaveData branchSaveData, GuidPath guidPath, LeafSaveData leafSaveData, 
            Dictionary<object, GuidPath> processedInstancesLookup, Dictionary<GameObject, GuidPath> savableGameObjectToGuidLookup,
            Dictionary<ScriptableObject, GuidPath> scriptableObjectToGuidLookup, Dictionary<Component, GuidPath> componentToGuidLookup)
        {
            _branchSaveData = branchSaveData;
            
            _guidPath = guidPath;
            _leafSaveData = leafSaveData;
            _processedInstancesLookup = processedInstancesLookup;
            _savableGameObjectToGuidLookup = savableGameObjectToGuidLookup;
            _scriptableObjectToGuidLookup = scriptableObjectToGuidLookup;
            _componentToGuidLookup = componentToGuidLookup;
        }

        public void Save(string uniqueIdentifier, object obj)
        {
            if (obj.GetType().IsValueType)
            {
                SaveAsValue(uniqueIdentifier, obj);
            }
            else
            {
                SaveAsReferencable(uniqueIdentifier, obj);
            }
        }

        /// <summary>
        /// Adds an object to the save data buffer using a unique identifier.
        /// Supports all valid types for Newtonsoft Json. Uses less disk space and is faster than Referencable Saving and Loading.
        /// </summary>
        /// <param name="uniqueIdentifier">The unique identifier for the object to be serialized.</param>
        /// <param name="obj">The object to be serialized and added to the buffer.</param>
        public void SaveAsValue(string uniqueIdentifier, object obj)
        {
            if (obj is Object)
            {
                Debug.LogError($"You can't save an object of type {typeof(Object)} as a value!");
                return;
            }
            
            if (obj is ISavable savable)
            {
                var newPath = new GuidPath(uniqueIdentifier);
                var componentDataBuffer = new LeafSaveData();
                var saveDataHandler = new SaveDataHandler(_branchSaveData, newPath, componentDataBuffer, _processedInstancesLookup, 
                    _savableGameObjectToGuidLookup, _scriptableObjectToGuidLookup, _componentToGuidLookup);
                
                savable.OnSave(saveDataHandler);
                
                _leafSaveData.Values.Add(uniqueIdentifier, JToken.FromObject(componentDataBuffer));
            }
            else if (ConverterServiceProvider.ExistsAndCreate(obj.GetType()))
            {
                var newPath = new GuidPath(uniqueIdentifier);
                var componentDataBuffer = new LeafSaveData();
                var saveDataHandler = new SaveDataHandler(_branchSaveData, newPath, componentDataBuffer, _processedInstancesLookup, 
                    _savableGameObjectToGuidLookup, _scriptableObjectToGuidLookup, _componentToGuidLookup);

                ConverterServiceProvider.GetConverter(obj.GetType()).Save(obj, saveDataHandler);
                
                _leafSaveData.Values.Add(uniqueIdentifier, JToken.FromObject(componentDataBuffer));
            }
            else
            {
                _leafSaveData.Values.Add(uniqueIdentifier, JToken.FromObject(obj));
            }
        }

        /// <summary>
        /// Attempts to add a referencable object to the save data buffer using a unique identifier.
        /// Supported types:
        /// 1. All Objects that have a unique identifier
        /// 2. Non-MonoBehaviour Classes, that can be instantiated by Activator.CreateInstance() and have Savable Attributes on them or implement the ISavable interface
        /// 3. Serializable Types of types that are supported by <see cref="SaveLoadSystem.Core.Converter.IConvertable"/>
        /// </summary>
        /// <param name="uniqueIdentifier">The unique identifier for the object reference.</param>
        /// <param name="obj">The object to be referenced and added to the buffer.</param>
        /// <returns><c>true</c> if the object reference was successfully added; otherwise, <c>false</c>.</returns>
        public void SaveAsReferencable(string uniqueIdentifier, object obj)
        {
            _leafSaveData.References.Add(uniqueIdentifier, ConvertToPath(uniqueIdentifier, obj));
        }

        /// <summary>
        /// Attempts to convert an object to a GUID path, so the reference can be identified at deserialization.
        /// </summary>
        /// <param name="uniqueIdentifier">The unique identifier for the object.</param>
        /// <param name="objectToSave">The object to convert to a GUID path.</param>
        /// <param name="guidPath">The resulting GUID path if the conversion is successful.</param>
        /// <returns><c>true</c> if the object was successfully converted to a GUID path; otherwise, <c>false</c>.</returns>
        private GuidPath ConvertToPath(string uniqueIdentifier, object objectToSave)
        {
            if (objectToSave == null)
            {
                //TODO: debug
                return default;
            }
            
            //search for a unity reference
            GuidPath guidPath;
            if (objectToSave is Component component)
            {
                //components with a guid must be processed because: 1. prevent ambiguity between duplicates 2. clearly identify components that inherit from ISavable
                if (_componentToGuidLookup.TryGetValue(component, out guidPath))
                {
                    return guidPath;
                }
                
                if (_savableGameObjectToGuidLookup.TryGetValue(component.gameObject, out guidPath))
                {
                    return guidPath;
                }
                
                //Debug.LogError("Internal Error!");
            }

            if (objectToSave is ScriptableObject scriptableObject)
            {
                if (_scriptableObjectToGuidLookup.TryGetValue(scriptableObject, out guidPath))
                {
                    return guidPath;
                }
            }
            
            //make sure the object gets created
            if (!_processedInstancesLookup.TryGetValue(objectToSave, out guidPath))
            {
                guidPath = new GuidPath(_guidPath.TargetGuid, uniqueIdentifier);
                ProcessAsSaveReferencable(objectToSave, guidPath);
            }
            
            return guidPath;
        }
        
        //TODO: if an object is beeing loaded in two different types, there must be an error -> not allowed
        //TODO: objects must always be loaded with the same type like when saving: how to check this is the case? i cant, but i can throw an error, if at spot 1 it is A and at spot 2 it is B and it is performed in the wrong order
        
        /// <summary>
        /// When only using Component-Saving and the Type-Converter, this Method will perform saving without reflection,
        /// which heavily improves performance. You will need the exchange the ProcessSavableElement method with this one.
        /// </summary>
        private void ProcessAsSaveReferencable(object objectToSave, GuidPath guidPath)
        {
            //if the fields and properties was found once, it shall not be created again to avoid a stackoverflow by cyclic references
            if (objectToSave.IsUnityNull() || !_processedInstancesLookup.TryAdd(objectToSave, guidPath)) return;
            
            if (objectToSave is ISavable)
            {
                var saveDataInstance = new LeafSaveData();
                
                _branchSaveData.AddLeafSaveData(guidPath, saveDataInstance);
                
                if (!TypeUtility.TryConvertTo(objectToSave, out ISavable targetSavable)) return;
            
                targetSavable.OnSave(new SaveDataHandler(_branchSaveData, guidPath, saveDataInstance, _processedInstancesLookup, 
                    _savableGameObjectToGuidLookup, _scriptableObjectToGuidLookup, _componentToGuidLookup));
            }
            else if (ConverterServiceProvider.ExistsAndCreate(objectToSave.GetType()))
            {
                var saveDataInstance = new LeafSaveData();
                
                _branchSaveData.AddLeafSaveData(guidPath, saveDataInstance);
                        
                var saveDataHandler = new SaveDataHandler(_branchSaveData, guidPath, saveDataInstance, _processedInstancesLookup, 
                    _savableGameObjectToGuidLookup, _scriptableObjectToGuidLookup, _componentToGuidLookup);
                ConverterServiceProvider.GetConverter(objectToSave.GetType()).Save(objectToSave, saveDataHandler);
            }
            else
            {
                var saveDataInstance = new LeafSaveData();
                
                _branchSaveData.AddLeafSaveData(guidPath, saveDataInstance);
                
                saveDataInstance.Values.Add("SerializeRef", JToken.FromObject(objectToSave));
            }
        }
    }
}
