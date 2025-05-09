namespace SaveMate.Runtime.Core.StateSnapshot.Converter
{
    internal interface ISaveMateConverter
    {
        void OnCaptureState(object input, CreateSnapshotHandler createSnapshotHandler);
        object CreateStateObject(RestoreSnapshotHandler restoreSnapshotHandler);
        void OnRestoreState(object input, RestoreSnapshotHandler restoreSnapshotHandler);
    }
}
