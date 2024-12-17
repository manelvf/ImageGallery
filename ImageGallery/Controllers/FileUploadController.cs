using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;


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
        
        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "UploadedFiles");
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
        var images = fileService.GetUploadedFiles("UploadedFiles");

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
            return files;
        }
        catch (Exception ex)
        {
            // Handle potential exceptions
            Console.WriteLine($"Error reading upload folder: {ex.Message}");
            return Array.Empty<string>();
        }
    }
}
