using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

public class S3Bucket
{
    private readonly AmazonS3Client _client;
    private readonly string _bucketName;

    public S3Bucket(IConfiguration config)
    {
        var accessKey = config["AWS:AccessKey"];
        var secretKey = config["AWS:SecretKey"];
        var region = config["AWS:Region"];
        _bucketName = config["AWS:Bucket"];

        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(_bucketName))
            throw new Exception("AWS credentials or bucket information are missing from configuration.");

        _client = new AmazonS3Client(accessKey, secretKey, RegionEndpoint.GetBySystemName(region));
    }

    public async Task<string> UploadTranscriptAsync(string filePath, string fileName)
    {
        var uploadRequest = new TransferUtilityUploadRequest
        {
            FilePath = filePath,
            BucketName = _bucketName,
            Key = $"transcripts/{fileName}",
            CannedACL = S3CannedACL.Private
        };

        var transfer = new TransferUtility(_client);
        await transfer.UploadAsync(uploadRequest);

        var presignedRequest = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = uploadRequest.Key,
            Expires = DateTime.UtcNow.AddDays(7)
        };

        return _client.GetPreSignedURL(presignedRequest);
    }
}