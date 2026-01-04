using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace functionCsharp
{
    public class ImageResizeFunction
    {
        private readonly ILogger<ImageResizeFunction> _logger;

        public ImageResizeFunction(ILogger<ImageResizeFunction> logger)
        {
            _logger = logger;
        }

        [Function("ResizeAndUploadImage")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("Image resize and upload function triggered.");

            try
            {
                // Read the image from request body
                using var ms = new MemoryStream();
                await req.Body.CopyToAsync(ms);
                var requestBody = ms.ToArray();
                if (requestBody.Length == 0)
                {
                    var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "No image data provided" });
                    return badResponse;
                }

                // Get resize parameters from query string
                int width = int.TryParse(req.Query["width"], out var w) ? w : 1200;
                int quality = int.TryParse(req.Query["quality"], out var q) ? q : 80;

                // Resize and compress image using ImageSharp
                using var image = Image.Load(requestBody);
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(width, (int)(width * image.Height / (float)image.Width)),
                    Mode = ResizeMode.Max
                }));

                // Convert to memory stream
                using var outputStream = new MemoryStream();
                await image.SaveAsJpegAsync(outputStream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder 
                { 
                    Quality = quality 
                });
                outputStream.Position = 0;

                // Get blob storage connection string from environment
                string connectionString = Environment.GetEnvironmentVariable("AzureWebImageStore") ?? "";
                if (string.IsNullOrEmpty(connectionString))
                {
                    var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                    await errorResponse.WriteAsJsonAsync(new { error = "AzureWebImageStore not configured" });
                    return errorResponse;
                }

                // Upload to Blob Storage
                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient("images");
                await containerClient.CreateIfNotExistsAsync();

                string blobName = $"{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid()}.jpg";
                var blockBlobClient = containerClient.GetBlobClient(blobName);
                await blockBlobClient.UploadAsync(outputStream, overwrite: true);

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new 
                { 
                    success = true, 
                    message = "Image resized and uploaded successfully",
                    url = blockBlobClient.Uri.ToString(),
                    blobName = blobName,
                    size = outputStream.Length
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing image: {ex.Message}");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }
    }
}
