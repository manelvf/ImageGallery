using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using SkiaSharp;
using Microsoft.Data.Sqlite;


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

        // Extract an MD5 hash from the filepath
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
        
        string selectQuery = "SELECT * FROM Images;";

        string connectionString = "Data Source=images.db;";

        List<Image> images = new List<Image>();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        
        using (var command = new SqliteCommand(selectQuery, connection))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                Image image = new Image
                {
                    Name = reader.GetString(1),
                    InsertionDate = reader.GetString(3),
                };
                images.Add(image);
            }
        }

        return Ok(images);
    }

    private class Image
    {
        public string Name { get; set; }
        public string InsertionDate { get; set; }
    }
}



[ApiController]
[Route("api/remove")]
public class FileRemoveController : ControllerBase
{
    [HttpDelete]
    public async Task<IActionResult> DeleteImage([FromQuery] string name, [FromQuery] string password)
    {
        
        string selectQuery = "SELECT * FROM Images where Name = @name;";

        string connectionString = "Data Source=images.db;";
        
        Image image = null;

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        
        using (var command = new SqliteCommand(selectQuery, connection))
        {
            command.Parameters.AddWithValue("@name", name);
            
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                image = new Image
                {
                    Name = reader.GetString(1),
                    Password = reader.GetString(2),
                };
            }
        }

        if (image != null && password.Length > 0)
        {
            string hashedPassword = HashPassword(password);
            
            if (image.Password != hashedPassword)
            {
                return BadRequest("Passwords do not match.");
            }
            else
            {
                FileService fileService = new FileService();
                fileService.DeleteImage(image);
            }
        }
        
        return Ok();
    }
    
    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashBytes); // Store the hash as Base64
    }
        
}

public class Image
{
    public string Name { get; set; }
    public string Password { get; set; }
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

    public void DeleteImage(Image image)
    {
        string deleteQuery = "DELETE FROM Images where Name = @name;";
        string connectionString = "Data Source=images.db;";
        
        using (var connection = new SqliteConnection(connectionString))
        using (var command = new SqliteCommand(deleteQuery, connection))
        {
            command.Parameters.AddWithValue("@name", image.Name);
            connection.Open();
            command.ExecuteNonQuery();
        }
    }
}

public class ThumbnailService
{
    private const int MaxThumbnailSizeBytes = 50 * 1024; // 50 KB
    private const int MaxOptimizedSizeBytes = 300 * 1024; // 300 KB
    private const int MaxCompressionAttempts = 10;

    public void CreateThumbnail(string uploadedFile, int width, int height)
    {
        string wwwfolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        
        string thumbsFolder = Path.Combine(wwwfolder, "thumbs");
        Directory.CreateDirectory(thumbsFolder);
        
        string optimizedFolder = Path.Combine(wwwfolder, "optimized");
        Directory.CreateDirectory(optimizedFolder);

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
                string thumbnailPath = Path.Combine(thumbsFolder, fileName);
                string optimizedPath = Path.Combine(optimizedFolder, fileName);

                // Try to compress the image to fit within 50 KB
                SaveThumbnailWithSizeLimit(image, thumbnailPath, 50);
                SaveThumbnailWithSizeLimit(image, optimizedPath, 300);
            }
        }
    }

    private void SaveThumbnailWithSizeLimit(SKImage image, string thumbnailPath, int kbLimit)
    {
        int quality = 80; // Starting quality
        byte[] thumbnailData = null;
        
        int sizeLimit = kbLimit * 1024;

        for (int attempt = 0; attempt < MaxCompressionAttempts; attempt++)
        {
            // Encode the image with current quality
            using (var data = image.Encode(SKEncodedImageFormat.Jpeg, quality))
            {
                thumbnailData = data.ToArray();

                // Check if the file size is within the limit
                if (thumbnailData.Length <= sizeLimit)
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
