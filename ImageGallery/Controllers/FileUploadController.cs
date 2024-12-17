using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using SkiaSharp;


[ApiController]
[Route("api/upload")]
public class FileUploadController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }
        
        string wwwfolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var uploadsFolder = Path.Combine(wwwfolder, "uploads");
        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        var filePath = Path.Combine(uploadsFolder, file.FileName);
        if (System.IO.File.Exists(filePath))
        {
            return BadRequest("File already exists.");
        }
        
        using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);
        
        ThumbnailService thumbnailService = new ThumbnailService();
        thumbnailService.CreateThumbnail(filePath, 100, 100);

        MD5 md5 = MD5.Create();
        byte[] fileBytes = Encoding.UTF8.GetBytes(filePath);
        byte[] hashBytes = md5.ComputeHash(fileBytes);
        
        
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < hashBytes.Length; i++)
        {
            sb.Append(hashBytes[i].ToString("x2"));
        }

        return Ok(sb.ToString());
    }
}


[ApiController]
[Route("api/list")]
public class ListController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListImages()
    {
        var fileService = new FileService();
        var wwwfolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var uploadsFolder = Path.Combine(wwwfolder, "uploads");
        var images = fileService.GetUploadedFiles(uploadsFolder);

        return Ok(images);
    }
}


public class FileService
{
    public string[] GetUploadedFiles(string uploadPath)
    {
        try
        {
            // Get all files in the directory
            string[] files = Directory.GetFiles(uploadPath);
            string[] filenames = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                filenames[i] = Path.GetFileName(files[i]);
            }
            return filenames;
        }
        catch (Exception ex)
        {
            // Handle potential exceptions
            Console.WriteLine($"Error reading upload folder: {ex.Message}");
            return Array.Empty<string>();
        }
    }
}

public class ThumbnailService
{
    private const int MaxThumbnailSizeBytes = 300 * 1024; // 300 KB
    private const int MaxCompressionAttempts = 10;

    public void CreateThumbnail(string uploadedFile, int width, int height)
    {
        string wwwfolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        string thumbsFolder = Path.Combine(wwwfolder, "thumbs");
        Directory.CreateDirectory(thumbsFolder);

        using (var originalStream = File.OpenRead(uploadedFile))
        using (var originalBitmap = SKBitmap.Decode(originalStream))
        {
            // Calculate aspect ratio preservation
            float ratio = Math.Min((float)width / originalBitmap.Width,
                (float)height / originalBitmap.Height);
            int newWidth = (int)(originalBitmap.Width * ratio);
            int newHeight = (int)(originalBitmap.Height * ratio);

            using (var resizedBitmap = originalBitmap.Resize(
                       new SKImageInfo(newWidth, newHeight), SKFilterQuality.High))
            using (var image = SKImage.FromBitmap(resizedBitmap))
            {
                string fileName = Path.GetFileName(uploadedFile);
                string thumbnailFileName = $"thumb_{fileName}";
                string thumbnailPath = Path.Combine(thumbsFolder, thumbnailFileName);

                // Try to compress the image to fit within 300 KB
                SaveThumbnailWithSizeLimit(image, thumbnailPath);
            }
        }
    }

    private void SaveThumbnailWithSizeLimit(SKImage image, string thumbnailPath)
    {
        int quality = 80; // Starting quality
        byte[] thumbnailData = null;

        for (int attempt = 0; attempt < MaxCompressionAttempts; attempt++)
        {
            // Encode the image with current quality
            using (var data = image.Encode(SKEncodedImageFormat.Jpeg, quality))
            {
                thumbnailData = data.ToArray();

                // Check if the file size is within the limit
                if (thumbnailData.Length <= MaxThumbnailSizeBytes)
                {
                    File.WriteAllBytes(thumbnailPath, thumbnailData);
                    return;
                }
            }

            // Reduce quality for next attempt
            quality -= 10;

            // Prevent quality from going too low
            if (quality < 10)
            {
                // If we can't compress enough, resize and try again
                using (var resizedBitmap = SKBitmap.Decode(thumbnailData).Resize(
                           new SKImageInfo(image.Width / 2, image.Height / 2), SKFilterQuality.Medium))
                using (var resizedImage = SKImage.FromBitmap(resizedBitmap))
                {
                    image = resizedImage;
                    quality = 80; // Reset quality
                }
            }
        }

        // If all attempts fail, save with lowest quality
        File.WriteAllBytes(thumbnailPath, thumbnailData);
    }
}
