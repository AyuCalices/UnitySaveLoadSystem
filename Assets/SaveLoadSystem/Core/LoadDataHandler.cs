using System;
using System.Collections.Generic;
using SaveLoadSystem.Core.Converter;
using SaveLoadSystem.Core.DataTransferObject;
using SaveLoadSystem.Core.UnityComponent;
using SaveLoadSystem.Core.UnityComponent.SavableConverter;
using SaveLoadSystem.Utility;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaveLoadSystem.Core
{
    /// <summary>
    /// The <see cref="LoadDataHandler"/> class is responsible for managing the deserialization and retrieval of
    /// serialized data, as well as handling reference building for complex object graphs.
    /// </summary>
    public readonly struct LoadDataHandler
    {
        //save data container
        private readonly RootSaveData _rootSaveData;
        private readonly BranchSaveData _globalBranchSaveData;
        private readonly LeafSaveData _leafSaveData;

        private readonly LoadType _loadType;
        private readonly string _sceneName;

        //reference lookups
        private readonly SaveLoadManager _saveLoadManager;
        private readonly SaveLink _saveLink;

        public LoadDataHandler(RootSaveData rootSaveData, BranchSaveData globalBranchSaveData, LeafSaveData leafSaveData, 
            LoadType loadType, string sceneName, SaveLink saveLink, SaveLoadManager saveLoadManager)
        {
            _rootSaveData = rootSaveData;
            _globalBranchSaveData = globalBranchSaveData;
            _leafSaveData = leafSaveData;

            _loadType = loadType;
            _sceneName = sceneName;

            _saveLoadManager = saveLoadManager;
            _saveLink = saveLink;
        }

        public bool TryLoad<T>(string identifier, out T obj)
        {
            var res = TryLoad(typeof(T), identifier, out var innerObj);

            if (res)
            {
                obj = (T)innerObj;
            }
            else
            {
                obj = default;
            }
            
            return res;
        }
        
        public bool TryLoad(Type type, string identifier, out object obj)
        {
            return type.IsValueType || type ==  typeof(string) ? TryLoadValue(type, identifier, out obj) : TryLoadReference(type, identifier, out obj);
        }

        public bool TryLoadValue<T>(string identifier, out T value)
        {
            var res = TryLoadValue(typeof(T), identifier, out var obj);
            
            if (res)
            {
                value = (T)obj;
            }
            else
            {
                value = default;
            }
            
            return res;
        }

        private bool TryLoadValue(Type type, string identifier, out object value)
        {
            //unity object handling
            if (typeof(Object).IsAssignableFrom(type))
            {
                Debug.LogError($"You can't load an object of type {typeof(Object)} as a value!");
                value = default;
                return false;
            }
            
            //savable handling
            if (typeof(ISavable).IsAssignableFrom(type))
            {
                var res = TryLoadValueSavable(type, identifier, out value);
                return res;
            }

            //converter handling
            if (ConverterServiceProvider.ExistsAndCreate(type))
            {
                var res = TryLoadValueWithConverter(type, identifier, out value);
                return res;
            }
            
            //serialization handling
            if (_leafSaveData.Values[identifier] == null)
            {
                value = default;
                return false;     //TODO: debug
            }

            value = _leafSaveData.Values[identifier].ToObject(type);
            return true;
        }
        
        public bool TryLoadReference<T>(string identifier, out T reference)
        {
            var res = TryLoadReference(typeof(T), identifier, out var obj);
            
            if (res)
            {
                reference = (T)obj;
            }
            else
            {
                reference = default;
            }
            
            return res;
        }

        private bool TryLoadReference(Type type, string identifier, out object reference)
        {
            reference = default;

            if (!TryGetGuidPath(identifier, out var guidPath))
                return false;

            if (TryGetUnityObjectReference(type, guidPath, out reference))
                return true;

            if (TryGetCreatedObject(type, guidPath, out reference))
                return true;
            
            if (CreateObject(type, guidPath, out reference))
                return true;

            return false;
        }
        
        private bool TryLoadValueSavable(Type type, string identifier, out object value)
        {
            if (_leafSaveData == null || !_leafSaveData.Values.TryGetValue(identifier, out var saveData))
            {
                Debug.LogWarning("There was no matching data!");
                value = null;
                return false;
            }
            
            var saveDataBuffer = saveData.ToObject<LeafSaveData>();
            var loadDataHandler = new LoadDataHandler(_rootSaveData, _globalBranchSaveData, saveDataBuffer, _loadType, 
                _sceneName, _saveLink, _saveLoadManager);

            value = Activator.CreateInstance(type);
            ((ISavable)value).OnLoad(loadDataHandler);

            return true;
        }
        
        private bool TryLoadValueWithConverter(Type type, string identifier, out object value)
        {
            if (_leafSaveData == null || !_leafSaveData.Values.TryGetValue(identifier, out var saveData))
            {
                Debug.LogWarning("There was no matching data!");
                value = null;
                return false;
            }
            
            var saveDataBuffer = saveData.ToObject<LeafSaveData>();
            var loadDataHandler = new LoadDataHandler(_rootSaveData, _globalBranchSaveData, saveDataBuffer, _loadType, 
                _sceneName, _saveLink, _saveLoadManager);

            var convertable = ConverterServiceProvider.GetConverter(type);
            value = convertable.CreateInstanceForLoad(loadDataHandler);
            convertable.Load(value, loadDataHandler);
            return true;
        }
        
        private bool TryGetGuidPath(string identifier, out GuidPath guidPath)
        {
            if (!_leafSaveData.References.TryGetValue(identifier, out guidPath))
            {
                Debug.LogWarning("Wasn't able to find the created object!"); //TODO: debug
                return false;
            }
            
            return true;
        }

        private bool TryGetUnityObjectReference(Type type, GuidPath guidPath, out object reference)
        {
            reference = default;

            //unity type reference handling
            if (typeof(ScriptableObject).IsAssignableFrom(type))
            {
                if (_saveLoadManager.GuidToScriptableObjectLookup.TryGetValue(guidPath, out var scriptableObject))
                {
                    reference = scriptableObject;
                    return true;
                }
                
                Debug.LogWarning("Wasn't able to find the requested object in the ScriptableObject lookup.");
            }
            else if (type == typeof(GameObject))
            {
                if (GetGuidPathGameObject(guidPath, out var savable))
                {
                    reference = savable.gameObject;
                    return true;
                }
                
                Debug.LogWarning("Wasn't able to find the requested object in the GameObject lookup.");
            } 
            else if (type == typeof(Transform))
            {
                if (GetGuidPathGameObject(guidPath, out var savable))
                {
                    reference = savable.transform;
                    return true;
                }
                
                Debug.LogWarning("Wasn't able to find the requested object in the Transform lookup.");
            }
            else if (type == typeof(RectTransform))
            {
                if (GetGuidPathGameObject(guidPath, out var savable))
                {
                    reference = (RectTransform)savable.transform;
                    return true;
                }
            }
            else if (typeof(Component).IsAssignableFrom(type))
            {
                if (GetGuidPathComponent(guidPath, out var duplicatedComponent))
                {
                    reference = duplicatedComponent;
                    return true;
                }
                
                if (GetGuidPathGameObject(guidPath, out var savable))  //TODO: explain why this is happening
                {
                    reference = savable.GetComponent(type);
                    return true;
                }
                
                Debug.LogWarning("Wasn't able to find the requested object in the Component lookup.");
            }
            
            return false;
        }
        
        private bool GetGuidPathGameObject(GuidPath guidPath, out GameObject gameObject)
        {
            gameObject = default;
            
            foreach (var saveSceneManager in _saveLoadManager.TrackedSaveSceneManagers)
            {
                if (_sceneName != saveSceneManager.SceneName) continue;
                    
                if (saveSceneManager.GuidToSavableGameObjectLookup.TryGetValue(guidPath, out gameObject))
                {
                    return true;
                }
                
                return false;
            }

            return false;
        }
        
        private bool GetGuidPathComponent(GuidPath guidPath, out Component component)
        {
            component = default;
            
            foreach (var saveSceneManager in _saveLoadManager.TrackedSaveSceneManagers)
            {
                if (_sceneName != saveSceneManager.SceneName) continue;
                    
                if (saveSceneManager.GuidToComponentLookup.TryGetValue(guidPath, out component))
                {
                    return true;
                }
                
                return false;
            }

            return false;
        }

        private bool TryGetCreatedObject(Type type, GuidPath guidPath, out object reference)
        {
            reference = default;
            
            if (_saveLink.GuidToCreatedNonUnityObjectLookup.TryGetValue(_loadType, guidPath, out reference))
            {
                if (reference.GetType() != type)
                {
                    Debug.LogWarning($"The requested object was already created as type '{reference.GetType()}'. You tried to return it as type '{type}', which is not allowed. Please use matching types."); //TODO: debug
                    return false;
                }
                
                return true;
            }

            return false;
        }

        private bool CreateObject(Type type, GuidPath guidPath, out object reference)
        {
            reference = null;
            
            if (!_globalBranchSaveData.TryGetLeafSaveData(guidPath, out LeafSaveData leafSaveData) && 
                !_rootSaveData.GlobalSaveData.TryGetLeafSaveData(guidPath, out leafSaveData))
            {
                Debug.LogWarning("Wasn't able to find the created object!"); //TODO: debug
                return false;
            }
            
            var loadDataHandler = new LoadDataHandler(_rootSaveData, _globalBranchSaveData, leafSaveData, _loadType, 
                _sceneName, _saveLink, _saveLoadManager);
            
            //savable handling
            if (typeof(ISavable).IsAssignableFrom(type))
            {
                reference = Activator.CreateInstance(type);
                if (!TypeUtility.TryConvertTo(reference, out ISavable objectSavable))
                    return false;

                _saveLink.GuidToCreatedNonUnityObjectLookup.Add(_loadType, guidPath, reference);
                objectSavable.OnLoad(loadDataHandler);
                return true;
            }

            //converter handling
            if (ConverterServiceProvider.ExistsAndCreate(type))
            {
                if (!ConverterServiceProvider.ExistsAndCreate(type))
                    return false;

                var converter = ConverterServiceProvider.GetConverter(type);

                reference = converter.CreateInstanceForLoad(loadDataHandler);
                _saveLink.GuidToCreatedNonUnityObjectLookup.Add(_loadType, guidPath, reference);
                converter.Load(reference, loadDataHandler);
                return true;
            }

            //serializable handling
            var jObject = leafSaveData.Values["SerializeRef"];
            if (jObject != null)
            {
                reference = jObject.ToObject(type);
                if (reference != null)
                {
                    _saveLink.GuidToCreatedNonUnityObjectLookup.Add(_loadType, guidPath, reference);
                    return true;
                }
            }
            
            //TODO: if UnityEngine.Object, then it might not be inside any registry -> create Debug.log
            Debug.LogError("Wasn't able to find the SerializeRef"); //TODO: debug
            return false;
        }
    }
}
