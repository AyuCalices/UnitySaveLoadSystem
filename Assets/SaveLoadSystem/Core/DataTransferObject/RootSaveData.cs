using System.Collections.Generic;
using JetBrains.Annotations;

namespace SaveLoadSystem.Core.DataTransferObject
{
    public class RootSaveData
    {
        public const string ScriptableObjectDataName = "ScriptableObjects";
        
        [UsedImplicitly] public BranchSaveData ScriptableObjectSaveData { get; set; } = new();
        [UsedImplicitly] public Dictionary<string, SceneData> SceneDataLookup { get; set; } = new();

        public void SetSceneData(string sceneName, SceneData sceneData)
        {
            SceneDataLookup[sceneName] = sceneData;
        }

        public bool TryGetSceneData(string sceneName, out SceneData sceneData)
        {
            return SceneDataLookup.TryGetValue(sceneName, out sceneData);
        }

        public void Clear()
        {
            ScriptableObjectSaveData = new();
            SceneDataLookup.Clear();
        }
    }
}
