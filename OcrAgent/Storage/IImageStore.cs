namespace OcrAgent.Storage;

public interface IImageStore
{
    bool Exists(string reference);
    string SaveFromFile(string filePath, string? desiredReference = null);
    string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null, string? desiredReference = null);