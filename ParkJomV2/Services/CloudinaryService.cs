using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace ParkJomV2.Services;

public class CloudinaryService
{
    private readonly Cloudinary _cloudinary;

    public CloudinaryService(IConfiguration configuration)
    {
        var account = new Account(
            configuration["Cloudinary:CloudName"],
            configuration["Cloudinary:ApiKey"],
            configuration["Cloudinary:ApiSecret"]
        );

        _cloudinary = new Cloudinary(account)
        {
            Api = { Secure = true }
        };
    }

    public async Task<ImageUploadResult> UploadImageAsync(IFormFile file, string folder, string type = "upload")
    {
        using var stream = file.OpenReadStream();

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = folder,
            Type = type
        };

        return await _cloudinary.UploadAsync(uploadParams);
    }

    public async Task<RawUploadResult> UploadPdfAsync(IFormFile file, string folder)
    {
        using var stream = file.OpenReadStream();

        var uploadParams = new RawUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = folder,
            Type = "private" // Ensure the image is uploaded as private
        };

        return await _cloudinary.UploadAsync(uploadParams);
    }

    public async Task<DeletionResult> DeleteAsync(string publicId,string resourceType = "image")
    {
        var deleteParams = new DeletionParams(publicId)
        {
            ResourceType = resourceType.ToLower() switch
            {
                "raw" => ResourceType.Raw,
                "video" => ResourceType.Video,
                _ => ResourceType.Image
            }
        };

        return await _cloudinary.DestroyAsync(deleteParams);
    }

    public string GeneratePrivateUrl(string publicId, string resourceType)
    {
        return _cloudinary.Api.Url
            .ResourceType(resourceType)
            .Type("private")
            .Secure(true)
            .Signed(true)
            .BuildUrl(publicId);
    }
}