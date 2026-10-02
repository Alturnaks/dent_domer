using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dental.Storage;

public record UploadForm(string Url, Dictionary<string, string> Fields, DateTime ExpiresAt);
public interface IFileStorage
{
    UploadForm CreateUpload(string key, string contentType, long size);
    Task PromoteAsync(string pendingKey, string key, string contentType, long size);
    string Download(string key, string fileName);
    Task DeleteAsync(string key);
}

public static class FileRules
{
    public const long MaxSize = 20 * 1024 * 1024;
    public static bool IsAllowed(string name, string mime) =>
        (System.IO.Path.GetExtension(name).ToLowerInvariant(), mime) is
        (".pdf", "application/pdf") or (".png", "image/png") or
        (".jpg", "image/jpeg") or (".jpeg", "image/jpeg");
    public static bool Matches(byte[] bytes, string mime) => mime switch
    {
        "application/pdf" => bytes.Length >= 5 && System.Text.Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-",
        "image/png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}),
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
        _ => false
    };
}
