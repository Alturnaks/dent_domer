using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Dental.Storage;

public class S3FileStorage(IConfiguration configuration) : IFileStorage, ITransientDependency
{
    private string Value(string key) => configuration["Storage:" + key] ?? throw new UserFriendlyException("Файловое хранилище не настроено.");
    private AmazonS3Client Client(bool publicUrl = false) => new(Value("AccessKey"), Value("SecretKey"), new AmazonS3Config
    {
        ServiceURL = Value(publicUrl ? "PublicUrl" : "ServiceUrl"), ForcePathStyle = true,
        AuthenticationRegion = Value("Region")
    });
    public UploadForm CreateUpload(string key, string contentType, long size)
    {
        var now = DateTime.UtcNow;
        var date = now.ToString("yyyyMMdd");
        var scope = $"{date}/{Value("Region")}/s3/aws4_request";
        var fields = new Dictionary<string, string>
        {
            ["key"] = key, ["Content-Type"] = contentType, ["x-amz-algorithm"] = "AWS4-HMAC-SHA256",
            ["x-amz-credential"] = Value("AccessKey") + "/" + scope, ["x-amz-date"] = now.ToString("yyyyMMddTHHmmssZ")
        };
        var conditions = new List<object> { new Dictionary<string,string> { ["bucket"] = Value("Bucket") },
            new object[] { "content-length-range", size, size } };
        foreach (var field in fields) conditions.Add(new Dictionary<string,string> { [field.Key] = field.Value });
        var expires = now.AddMinutes(10);
        var policy = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { expiration = expires.ToString("yyyy-MM-ddTHH:mm:ssZ"), conditions })));
        byte[] H(byte[] k, string s) => HMACSHA256.HashData(k, Encoding.UTF8.GetBytes(s));
        var signing = H(H(H(H(Encoding.UTF8.GetBytes("AWS4" + Value("SecretKey")), date), Value("Region")), "s3"), "aws4_request");
        fields["policy"] = policy;
        fields["x-amz-signature"] = Convert.ToHexStringLower(H(signing, policy));
        return new(Value("PublicUrl").TrimEnd('/') + "/" + Value("Bucket"), fields, expires);
    }
    public async Task PromoteAsync(string pendingKey, string key, string contentType, long size)
    {
        using var client = Client();
        var head = await client.GetObjectMetadataAsync(Value("Bucket"), pendingKey);
        using var response = await client.GetObjectAsync(new GetObjectRequest { BucketName = Value("Bucket"), Key = pendingKey, ByteRange = new ByteRange(0, 15), EtagToMatch = head.ETag });
        using var bytes = new MemoryStream(); await response.ResponseStream.CopyToAsync(bytes);
        if (head.ContentLength != size || size > FileRules.MaxSize || head.Headers.ContentType != contentType || !FileRules.Matches(bytes.ToArray(), contentType))
        {
            await client.DeleteObjectAsync(Value("Bucket"), pendingKey);
            throw new UserFriendlyException("Содержимое файла не соответствует заявленному типу или размеру.");
        }
        // Copy the exact validated ETag. The final key cannot be overwritten through the upload form.
        await client.CopyObjectAsync(new CopyObjectRequest { SourceBucket = Value("Bucket"), SourceKey = pendingKey,
            DestinationBucket = Value("Bucket"), DestinationKey = key, ETagToMatch = head.ETag });
        await client.DeleteObjectAsync(Value("Bucket"), pendingKey);
    }
    public string Download(string key, string fileName)
    {
        using var client = Client(true);
        return client.GetPreSignedURL(new GetPreSignedUrlRequest { BucketName = Value("Bucket"), Key = key,
            Protocol = new Uri(Value("PublicUrl")).Scheme == "https" ? Protocol.HTTPS : Protocol.HTTP,
            Verb = HttpVerb.GET, Expires = DateTime.UtcNow.AddMinutes(5),
            ResponseHeaderOverrides = new ResponseHeaderOverrides { ContentDisposition = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(fileName) } });
    }
    public async Task DeleteAsync(string key)
    {
        using var client = Client(); await client.DeleteObjectAsync(Value("Bucket"), key);
    }
}
