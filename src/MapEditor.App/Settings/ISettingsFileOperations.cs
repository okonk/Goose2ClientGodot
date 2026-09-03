namespace MapEditor.App.Settings;

internal interface ISettingsFileOperations
{
    bool FileExists(string path);

    string ReadAllText(string path);

    void CreateDirectory(string directory);

    void WriteAllText(string path, string contents);

    void Move(string source, string destination);

    void DeleteFile(string path);
}
