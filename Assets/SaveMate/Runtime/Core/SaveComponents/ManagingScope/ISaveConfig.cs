namespace SaveMate.Runtime.Core.SaveComponents.ManagingScope
{
    public interface ISaveConfig
    {
        string SavePath { get; }
        string SaveDataExtensionName { get; }
        string MetaDataExtensionName { get; }
        
    }
}
