using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SaveLoadSystem.Core.DataTransferObject;
using SaveLoadSystem.Core.Integrity;
using SaveLoadSystem.Core.SerializeStrategy;
using SaveLoadSystem.Core.UnityComponent;
using SaveLoadSystem.Utility;
using UnityEngine;

namespace SaveLoadSystem.Core
{
    [CreateAssetMenu]
    public class SaveLoadManager : ScriptableObject, ISaveConfig, ISaveStrategy
    {
        [Header("Version")] 
        [SerializeField] private int major;
        [SerializeField] private int minor;
        [SerializeField] private int patch;
        
        [Header("File Name")]
        [SerializeField] private string defaultFileName;
        [SerializeField] private string savePath;
        [SerializeField] private string saveDataExtensionName = "savedata";
        [SerializeField] private string metaDataExtensionName = "metadata";
        
        [Header("Storage")]
        [SerializeField] private SaveIntegrityType integrityCheckType;
        [SerializeField] private SaveCompressionType compressionType;
        [SerializeField] private SaveEncryptionType encryptionType;
        [SerializeField] private string defaultEncryptionKey = "0123456789abcdef0123456789abcdef";
        [SerializeField] private string defaultEncryptionIv = "abcdef9876543210";

        [Header("Other")] 
        [SerializeField] private AssetRegistry assetRegistry;
        
        public event Action<SaveFileContext, SaveFileContext> OnBeforeSaveFileContextChange;
        public event Action<SaveFileContext, SaveFileContext> OnAfterSaveFileContextChange;
        
        public SaveVersion SaveVersion => new(major, minor, patch);
        public string SavePath
        {
            get => savePath;
            set => savePath = value;
        }

        public string SaveDataExtensionName
        {
            get => saveDataExtensionName;
            set => saveDataExtensionName = value;
        }

        public string MetaDataExtensionName
        {
            get => metaDataExtensionName;
            set => metaDataExtensionName = value;
        }


        private string _activeSaveFile;
        private SaveFileContext _currentSaveFileContext;
        private byte[] _aesKey = Array.Empty<byte>();
        private byte[] _aesIv = Array.Empty<byte>();
        
        //scene save manager
        private static SimpleSceneSaveManager _dontDestroyOnLoadManager;
        private readonly List<SimpleSceneSaveManager> _trackedSaveSceneManagers = new();
        
        //reference lookup
        internal readonly Dictionary<GuidPath, ScriptableObject> GuidToScriptableObjectLookup = new();
        internal readonly Dictionary<ScriptableObject, GuidPath> ScriptableObjectToGuidLookup = new();
        internal readonly Dictionary<string, Savable> GuidToSavablePrefabsLookup = new();

        
        #region Unity Lifecycle

        
        private void OnEnable()
        {
            _activeSaveFile = defaultFileName;
            
            foreach (var scriptableObjectSavable in assetRegistry.ScriptableObjectSavables)
            {
                var guidPath = new GuidPath(RootSaveData.ScriptableObjectDataName, scriptableObjectSavable.guid);
                ScriptableObjectToGuidLookup.Add((ScriptableObject)scriptableObjectSavable.unityObject, guidPath);
                GuidToScriptableObjectLookup.Add(guidPath, (ScriptableObject)scriptableObjectSavable.unityObject);
            }

            foreach (var prefabSavable in assetRegistry.PrefabSavables)
            {
                GuidToSavablePrefabsLookup.Add(prefabSavable.PrefabGuid, prefabSavable);
            }
        }

        private void OnDisable()
        {
            ReleaseSaveFileContext();
            
            GuidToScriptableObjectLookup.Clear();
            GuidToSavablePrefabsLookup.Clear();
            ScriptableObjectToGuidLookup.Clear();
        }

        
        #endregion
        
        #region File I/O
        
        
        public void SetActiveSaveFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                _activeSaveFile = defaultFileName;
                Debug.Log($"[Save Mate] {nameof(fileName)} missing: Swapped to the default file name: {fileName}.");
            }
            
            _activeSaveFile = fileName;
            UpdateSaveFileContext(fileName);
        }
        
        public string[] GetAllSaveFiles()
        {
            return SaveLoadUtility.FindAllSaveFiles(this);
        }
        
        
        #endregion

        public void SetNewtonsoftJsonSettings()
        {
            throw new NotImplementedException();
        }

        #region Encryption

        public void SetAesEncryption(byte[] aesKey, byte[] aesIv)
        {
            _aesKey = aesKey;
            _aesIv = aesIv;
        }

        public void ClearAesEncryption()
        {
            _aesKey = Array.Empty<byte>();
            _aesIv = Array.Empty<byte>();
        }

        #endregion

        #region MetaData

        //TODO: optimization possible: cache and update all meta data for direct use of SaveFileContext
        public async void FetchAllMetaData(Action<SaveMetaData[]> onAllMetaDataFound)
        {
            var saveMetaData = await FetchAllMetaData();
            
            onAllMetaDataFound.Invoke(saveMetaData);
        }
        
        public async Task<SaveMetaData[]> FetchAllMetaData()
        {
            var saveFileNames = GetAllSaveFiles();
            var saveMetaData = new SaveMetaData[saveFileNames.Length];

            for (var index = 0; index < saveFileNames.Length; index++)
            {
                var saveFileName = saveFileNames[index];

                saveMetaData[index] = await FetchMetaData(saveFileName);
            }

            return saveMetaData;
        }
        
        public async void FetchMetaData(string saveName, Action<SaveMetaData> onMetaDataFound)
        {
            var metaData = await FetchMetaData(saveName);
            
            onMetaDataFound.Invoke(metaData);
        }
        
        
        public async Task<SaveMetaData> FetchMetaData(string saveName)
        {
            return await SaveLoadUtility.ReadMetaDataAsync(this, this, saveName);
        }
        
        //TODO: maybe support value saving through doubled object saving in save file with same id
        public void QuickSaveMetaData(string identifier, object obj)
        {
            QuickSaveMetaData(_activeSaveFile, identifier, obj);
        }

        public void QuickSaveMetaData(string fileName, string identifier, object obj)
        {
            UpdateSaveFileContext(fileName);

            _currentSaveFileContext.SaveCustomMetaData(identifier, obj);
        }
        
        public bool TryQuickLoadMetaData<T>(string identifier, out T obj)
        {
            return TryQuickLoadMetaData(_activeSaveFile, identifier, out obj);
        }

        public bool TryQuickLoadMetaData<T>(string fileName, string identifier, out T obj)
        {
            UpdateSaveFileContext(fileName);

            //TODO: will result in error: SaveFileContext not initialized!
            return _currentSaveFileContext.TryLoadCustomMetaData(identifier, out obj);
        }

        
        #endregion
        
        #region Save
        
        
        public void SaveActiveScenes()
        {
            Save(_activeSaveFile, _trackedSaveSceneManagers.Cast<ISavableGroup>().ToArray());
        }

        public void SaveActiveScenes(string fileName)
        {
            Save(fileName, _trackedSaveSceneManagers.Cast<ISavableGroup>().ToArray());
        }
        
        public void Save(params ISavableGroup[] savableGroups)
        {
            Save(_activeSaveFile, savableGroups);
        }
        
        public void Save(string fileName, params ISavableGroup[] savableGroups)
        {
            UpdateSaveFileContext(fileName);
            
            _currentSaveFileContext.CaptureSnapshot(savableGroups.ToArray());
            _currentSaveFileContext.WriteToDisk();
        }
        

        #endregion

        #region CaptureSnapshot
        
        
        public void CaptureSnapshotActiveScenes()
        {
            CaptureSnapshot(_activeSaveFile, _trackedSaveSceneManagers.Cast<ISavableGroup>().ToArray());
        }

        public void CaptureSnapshotActiveScenes(string fileName)
        {
            CaptureSnapshot(fileName, _trackedSaveSceneManagers.Cast<ISavableGroup>().ToArray());
        }

        public void CaptureSnapshot(params ISavableGroup[] savableGroups)
        {
            CaptureSnapshot(_activeSaveFile, savableGroups);
        }

        public void CaptureSnapshot(string fileName, params ISavableGroup[] savableGroups)
        {
            UpdateSaveFileContext(fileName);
            
            _currentSaveFileContext.CaptureSnapshot(savableGroups);
        }

        
        #endregion

        #region WriteToDisk
        
        
        public void WriteToDisk()
        {
            WriteToDisk(_activeSaveFile);
        }

        public void WriteToDisk(string fileName)
        {
            UpdateSaveFileContext(fileName);
            
            _currentSaveFileContext.WriteToDisk();
        }

        
        #endregion

        #region Load
        
        
        public void LoadActiveScenes(LoadType loadType = LoadType.Hard)
        {
            Load(_activeSaveFile, loadType, _trackedSaveSceneManagers.Cast<ILoadableGroup>().ToArray());
        }

        public void LoadActiveScenes(string fileName, LoadType loadType = LoadType.Hard)
        {
            Load(fileName, loadType, _trackedSaveSceneManagers.Cast<ILoadableGroup>().ToArray());
        }

        public void Load(LoadType loadType = LoadType.Hard, params ILoadableGroup[] loadableGroups)
        {
            Load(_activeSaveFile, loadType, loadableGroups);
        }

        public void Load(string fileName, LoadType loadType = LoadType.Hard, params ILoadableGroup[] loadableGroups)
        {
            UpdateSaveFileContext(fileName);
            
            _currentSaveFileContext.ReadFromDisk();
            _currentSaveFileContext.RestoreSnapshot(loadType, loadableGroups);
        }

        
        #endregion

        #region RestoreSnapshot
        
        
        public void RestoreSnapshotActiveScenes(LoadType loadType = LoadType.Hard)
        {
            RestoreSnapshot(_activeSaveFile, loadType, _trackedSaveSceneManagers.Cast<ILoadableGroup>().ToArray());
        }

        public void RestoreSnapshotActiveScenes(string fileName, LoadType loadType = LoadType.Hard)
        {
            RestoreSnapshot(fileName, loadType, _trackedSaveSceneManagers.Cast<ILoadableGroup>().ToArray());
        }

        public void RestoreSnapshot(LoadType loadType = LoadType.Hard, params ILoadableGroup[] loadableGroups)
        {
            RestoreSnapshot(_activeSaveFile, loadType, loadableGroups);
        }

        public void RestoreSnapshot(string fileName, LoadType loadType = LoadType.Hard, params ILoadableGroup[] loadableGroups)
        {
            UpdateSaveFileContext(fileName);
            
            _currentSaveFileContext.RestoreSnapshot(loadType, loadableGroups);
        }
        

        #endregion

        #region MyRegion
        
        
        public void ReadFromDisk()
        {
            ReadFromDisk(_activeSaveFile);
        }

        public void ReadFromDisk(string fileName)
        {
            UpdateSaveFileContext(fileName);
            
            _currentSaveFileContext.ReadFromDisk();
        }

        
        #endregion

        #region DeleteSnapshotData

        //TODO: documentary: make sure, that snapshots doesnt affect meta data in any way
        //wont delete meta data
        public void DeleteSnapshotData()
        {
            DeleteSnapshotData(_activeSaveFile);
        }

        //will delete meta data on the disk
        public void DeleteSnapshotData(string fileName)
        {
            UpdateSaveFileContext(fileName);
            
            _currentSaveFileContext.DeleteSnapshotData();
        }

        
        #endregion

        #region DeleteDiskData

        
        public void DeleteDiskData()
        {
            DeleteDiskData(_activeSaveFile);
        }

        public void DeleteDiskData(string fileName)
        {
            UpdateSaveFileContext(fileName);
            
            _currentSaveFileContext.DeleteDiskData();
        }

        
        #endregion

        #region WipeAll

        
        public void WipeAll()
        {
            UpdateSaveFileContext(_activeSaveFile);
        }

        public void WipeAll(string fileName)
        {
            UpdateSaveFileContext(fileName);
            
            _currentSaveFileContext.DeleteSnapshotData();
            _currentSaveFileContext.DeleteDiskData();
        }

        
        #endregion

        #region ReloadScenes

        
        public void ReloadActiveScenes()
        {
            ReloadScenes(_trackedSaveSceneManagers.ToArray());
        }

        public void ReloadScenes(params SimpleSceneSaveManager[] scenesToLoad)
        {
            _currentSaveFileContext.ReloadScenes(scenesToLoad);
        }
        

        #endregion
        

        #region Internal

        
        internal void RegisterSaveSceneManager(SceneSaveManager sceneSaveManager)
        {
            _trackedSaveSceneManagers.Add(sceneSaveManager);
        }
        
        internal bool UnregisterSaveSceneManager(SceneSaveManager sceneSaveManager)
        {
            return _trackedSaveSceneManagers.Remove(sceneSaveManager);
        }
        
        internal List<SimpleSceneSaveManager> GetTrackedSaveSceneManagers()
        {
            if (_dontDestroyOnLoadManager != null && !_trackedSaveSceneManagers.Contains(_dontDestroyOnLoadManager))
            {
                _trackedSaveSceneManagers.Add(_dontDestroyOnLoadManager);
            }

            if (_dontDestroyOnLoadManager == null && _trackedSaveSceneManagers.Contains(_dontDestroyOnLoadManager))
            {
                _trackedSaveSceneManagers.Remove(_dontDestroyOnLoadManager);
            }

            return _trackedSaveSceneManagers;
        }

        internal static SimpleSceneSaveManager GetDontDestroyOnLoadSceneManager()
        {
            if (!_dontDestroyOnLoadManager)
            {
                GameObject newObject = new GameObject("DontDestroyOnLoadObject");
                DontDestroyOnLoad(newObject);
                _dontDestroyOnLoadManager = newObject.AddComponent<SimpleSceneSaveManager>();
            }
            
            return _dontDestroyOnLoadManager;
        }
        
        ICompressionStrategy ISaveStrategy.GetCompressionStrategy()
        {
            return compressionType switch
            {
                SaveCompressionType.None => new NoneCompressionSerializationStrategy(),
                SaveCompressionType.Gzip => new GzipCompressionSerializationStrategy(),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        ISerializationStrategy ISaveStrategy.GetSerializationStrategy()
        {
            return new JsonSerializeStrategy();
        }

        IEncryptionStrategy ISaveStrategy.GetEncryptionStrategy()
        {
            switch (encryptionType)
            {
                case SaveEncryptionType.None:
                    return new NoneEncryptSerializeStrategy();
                
                case SaveEncryptionType.Aes:
                    if (_aesKey.Length == 0 || _aesIv.Length == 0)
                    {
                        return new AesEncryptSerializeStrategy(Encoding.UTF8.GetBytes(defaultEncryptionKey), Encoding.UTF8.GetBytes(defaultEncryptionIv));
                    }
                    return new AesEncryptSerializeStrategy(_aesKey, _aesIv);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        IIntegrityStrategy ISaveStrategy.GetIntegrityStrategy()
        {
            return integrityCheckType switch
            {
                SaveIntegrityType.None => new EmptyIntegrityStrategy(),
                SaveIntegrityType.Sha256Hashing => new Sha256HashingIntegrityStrategy(),
                SaveIntegrityType.CRC32 => new CRC32IntegrityStrategy(),
                SaveIntegrityType.Adler32 => new Adler32IntegrityStrategy(),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
        
        
        #endregion

        #region Private
        
        
        private void UpdateSaveFileContext(string fileName = null)
        {
            if (_currentSaveFileContext != null && _currentSaveFileContext.FileName == fileName) return;
            
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = _activeSaveFile;
                Debug.Log($"[Save Mate] {nameof(fileName)} missing: Swapped to the file with the name: {fileName}.");
            }
            
            ChangeSaveFileContext(new SaveFileContext(this, assetRegistry, fileName));
        }

        private void ReleaseSaveFileContext()
        {
            if (_currentSaveFileContext != null)
            {
                ChangeSaveFileContext(null);
            }
        }
        
        private void ChangeSaveFileContext(SaveFileContext newSaveFileContext)
        {
            SaveFileContext oldSaveFileContext = _currentSaveFileContext;
            
            OnBeforeSaveFileContextChange?.Invoke(oldSaveFileContext, newSaveFileContext);

            _currentSaveFileContext = newSaveFileContext;
            
            OnAfterSaveFileContextChange?.Invoke(oldSaveFileContext, newSaveFileContext);
        }
        

        #endregion
    }
}
