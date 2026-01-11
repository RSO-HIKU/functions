using System.IO;
using System.Threading.Tasks;
using System;
using System.Linq;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Azure.Identity;

namespace functionCsharp
{
    public class ImageResizeFunction
    {
        private readonly ILogger<ImageResizeFunction> _logger;

        public ImageResizeFunction(ILogger<ImageResizeFunction> logger)
        {
            _logger = logger;
        }

        private void AddCorsHeaders(HttpResponseData response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS, GET");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");
            response.Headers.Add("Access-Control-Max-Age", "86400");
        }

        [Function("ResizeAndUploadImage")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("Image resize and upload function triggered.");

            // Handle OPTIONS preflight request
            if (req.Method == "OPTIONS")
            {
                var optionsResponse = req.CreateResponse(System.Net.HttpStatusCode.NoContent);
                AddCorsHeaders(optionsResponse);
                return optionsResponse;
            }

            try
            {
                byte[] imageData = null;
                var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? "";

                if (contentType.Contains("multipart/form-data"))
                {
                    var boundaryIndex = contentType.IndexOf("boundary=");
                    if (boundaryIndex < 0)
                    {
                        var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse);
                        await badResponse.WriteAsJsonAsync(new { error = "Invalid multipart boundary" });
                        return badResponse;
                    }

                    var boundary = contentType.Substring(boundaryIndex + 9).Trim('"');
                    
                    using var ms = new MemoryStream();
                    await req.Body.CopyToAsync(ms);
                    byte[] bodyBytes = ms.ToArray();

                    string searchStr = System.Text.Encoding.UTF8.GetString(bodyBytes);
                    int imageTypeIndex = searchStr.IndexOf("Content-Type: image/");
                    
                    if (imageTypeIndex < 0)
                    {
                        var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse);
                        await badResponse.WriteAsJsonAsync(new { error = "No image data found in multipart request" });
                        return badResponse;
                    }

                    int headerEndPos = -1;
                    for (int i = imageTypeIndex; i < bodyBytes.Length - 3; i++)
                    {
                        if (bodyBytes[i] == '\r' && bodyBytes[i + 1] == '\n' && 
                            bodyBytes[i + 2] == '\r' && bodyBytes[i + 3] == '\n')
                        {
                            headerEndPos = i + 4;
                            break;
                        }
                    }

                    if (headerEndPos < 0)
                    {
                        var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse);
                        await badResponse.WriteAsJsonAsync(new { error = "Invalid multipart format" });
                        return badResponse;
                    }

                    int dataStartIndex = headerEndPos;
                    byte[] boundaryBytes = System.Text.Encoding.UTF8.GetBytes($"\r\n--{boundary}");

                    int nextBoundaryIndex = -1;
                    for (int i = dataStartIndex; i < bodyBytes.Length - boundaryBytes.Length; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < boundaryBytes.Length; j++)
                        {
                            if (bodyBytes[i + j] != boundaryBytes[j])
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match)
                        {
                            nextBoundaryIndex = i;
                            break;
                        }
                    }

                    if (nextBoundaryIndex < 0)
                    {
                        var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse);
                        await badResponse.WriteAsJsonAsync(new { error = "Incomplete multipart data" });
                        return badResponse;
                    }

                    int dataLength = nextBoundaryIndex - dataStartIndex;
                    
                    if (dataLength <= 0)
                    {
                        var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse);
                        await badResponse.WriteAsJsonAsync(new { error = "No image data found" });
                        return badResponse;
                    }

                    imageData = new byte[dataLength];
                    System.Buffer.BlockCopy(bodyBytes, dataStartIndex, imageData, 0, dataLength);
                    
                    _logger.LogInformation($"Extracted {imageData.Length} bytes of image data from multipart form");
                }
                else
                {
                    using var ms = new MemoryStream();
                    await req.Body.CopyToAsync(ms);
                    imageData = ms.ToArray();
                    _logger.LogInformation($"Received {imageData.Length} bytes of raw image data");
                }

                if (imageData == null || imageData.Length == 0)
                {
                    var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse);
                    await badResponse.WriteAsJsonAsync(new { error = "No image data provided" });
                    return badResponse;
                }

                int width = int.TryParse(req.Query["width"], out var w) ? w : 1200;
                int quality = int.TryParse(req.Query["quality"], out var q) ? q : 80;

                using var image = Image.Load(imageData);
                _logger.LogInformation($"Loaded image: {image.Width}x{image.Height}");
                
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(width, (int)(width * image.Height / (float)image.Width)),
                    Mode = ResizeMode.Max
                }));

                using var outputStream = new MemoryStream();
                await image.SaveAsJpegAsync(outputStream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder 
                { 
                    Quality = quality 
                });
                outputStream.Position = 0;

                _logger.LogInformation($"Image resized successfully: {image.Width}x{image.Height}");

                string accountName = Environment.GetEnvironmentVariable("APP_BLOB_ACCOUNT") ?? "";
                string containerName = Environment.GetEnvironmentVariable("APP_BLOB_CONTAINER") ?? "images";

                if (string.IsNullOrWhiteSpace(accountName))
                {
                    var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse);
                    await errorResponse.WriteAsJsonAsync(new { error = "APP_BLOB_ACCOUNT not configured" });
                    return errorResponse;
                }

                var blobServiceUri = new Uri($"https://{accountName}.blob.core.windows.net");

                // Uses the Function App's managed identity in Azure.
                // Locally it will try Visual Studio / Azure CLI credentials if you are logged in.
                var credential = new DefaultAzureCredential();

                var blobServiceClient = new BlobServiceClient(blobServiceUri, credential);
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

                // Optional: remove this if you want to enforce “container must exist” and avoid creation permissions.
                await containerClient.CreateIfNotExistsAsync();

                string blobName = $"{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid()}.jpg";
                var blockBlobClient = containerClient.GetBlobClient(blobName);
                await blockBlobClient.UploadAsync(outputStream, overwrite: true);

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                AddCorsHeaders(response);
                
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
                _logger.LogError($"Error processing image: {ex.Message}\n{ex.StackTrace}");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }
    }
}