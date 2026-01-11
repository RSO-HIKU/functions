# Azure Functions - Image Processing Service

## Overview

This Azure Functions application provides HTTP-triggered serverless functions for image processing operations, with a primary focus on image resizing and uploading to Azure Blob Storage. The application is built using .NET 10.0 and the Azure Functions v4 runtime with the isolated worker model.

## Table of Contents

1. [Architecture](#architecture)
2. [Functions](#functions)
3. [Dependencies](#dependencies)
4. [Configuration](#configuration)
5. [API Reference](#api-reference)
6. [Local Development](#local-development)
7. [Deployment](#deployment)
8. [Error Handling](#error-handling)
9. [Monitoring](#monitoring)

---

## Architecture

### Technology Stack

- **Runtime**: .NET 10.0
- **Functions Runtime**: Azure Functions v4
- **Worker Model**: Isolated Worker Process
- **Image Processing**: SixLabors.ImageSharp 3.1.0
- **Storage**: Azure Blob Storage
- **Monitoring**: Application Insights

### Application Structure

```
functionCsharp/
├── Program.cs                   # Application entry point and DI configuration
├── ImageResizeFunction.cs       # Main image processing function
├── HttpTrigger1.cs             # Sample HTTP trigger function
├── FormDataParser.cs           # Empty utility file (reserved for future use)
├── functionCsharp.csproj       # Project configuration and dependencies
├── host.json                   # Function host configuration
├── local.settings.json         # Local development settings
└── Properties/
    └── launchSettings.json     # Launch configuration
```

---

## Functions

### 1. ResizeAndUploadImage

**Purpose**: Accepts an image file via HTTP POST, resizes it to specified dimensions, and uploads it to Azure Blob Storage.

**Trigger**: HTTP (POST, OPTIONS)

**Authorization Level**: Anonymous

**Key Features**:
- Multipart form-data support for file uploads
- Raw binary data support
- Configurable image width and quality
- Automatic JPEG conversion
- CORS support for cross-origin requests
- Blob storage integration with automatic container creation

**Input Formats**:
- `multipart/form-data`: Standard form upload
- Binary data: Raw image bytes in request body

**Processing Pipeline**:
1. Parse incoming request (multipart or binary)
2. Extract image data
3. Resize image to specified dimensions
4. Convert to JPEG format with configurable quality
5. Upload to Azure Blob Storage
6. Return success response with blob URL

### 2. HttpTrigger1

**Purpose**: Sample HTTP trigger function that returns a welcome message.

**Trigger**: HTTP (GET, POST)

**Authorization Level**: Anonymous

**Purpose**: Demonstrates basic HTTP trigger functionality for testing and development.

---

## Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Azure.Functions.Worker | 2.51.0 | Core Functions worker runtime |
| Microsoft.Azure.Functions.Worker.Sdk | 2.0.7 | Build-time SDK for Functions |
| Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore | 2.1.0 | HTTP trigger support with ASP.NET Core |
| Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs | 6.2.0 | Blob storage bindings |
| Azure.Storage.Blobs | 12.17.0 | Azure Blob Storage client library |
| SixLabors.ImageSharp | 3.1.0 | Cross-platform image processing |
| Microsoft.ApplicationInsights.WorkerService | 2.23.0 | Application Insights telemetry |
| Microsoft.Azure.Functions.Worker.ApplicationInsights | 2.50.0 | Application Insights integration for Functions |

---

## Configuration

### Environment Variables

#### Required

| Variable | Description | Example |
|----------|-------------|---------|
| `AzureWebImageStore` | Connection string for Azure Storage account where images are stored | `DefaultEndpointsProtocol=https;AccountName=...` |
| `FUNCTIONS_WORKER_RUNTIME` | Specifies the worker runtime | `dotnet-isolated` |
| `AzureWebJobsStorage` | Connection string for Azure Functions internal storage | `UseDevelopmentStorage=true` (local) |


## API Reference

### ResizeAndUploadImage

#### Headers

```
Content-Type: multipart/form-data; boundary=<boundary>
# OR
Content-Type: image/jpeg (or image/png, etc.)
```

#### Query Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `width` | integer | No | 1200 | Target width in pixels (height auto-calculated) |
| `quality` | integer | No | 80 | JPEG quality (1-100) |

#### Request Body

**Option 1: Multipart Form Data**

**Option 2: Binary Data**

#### Success Response

**Code**: `200 OK`

```json
{
  "success": true,
  "message": "Image resized and uploaded successfully",
  "url": "https://<storage-account>.blob.core.windows.net/images/2026-01-11/<guid>.jpg",
  "blobName": "2026-01-11/<guid>.jpg",
  "size": 245678
}
```

#### Error Responses

**Code**: `400 Bad Request`

```json
{
  "error": "No image data provided"
}
```

**Code**: `500 Internal Server Error`

```json
{
  "error": "AzureWebImageStore not configured"
}
```


## Local Development

### Prerequisites

- .NET 10.0 SDK or later
- Azure Functions Core Tools (v4)
- Azure Storage Emulator or Azurite
- Visual Studio 2022 / Visual Studio Code with Azure Functions extension

### Setup

1. **Install Azure Functions Core Tools**:
   ```bash
   npm install -g azure-functions-core-tools@4 --unsafe-perm true
   ```

2. **Configure Local Settings**:
   Edit `local.settings.json` and add your Azure Storage connection string:
   ```json
   {
     "Values": {
       "AzureWebImageStore": "<your-storage-connection-string>"
     }
   }
   ```

3. **Restore Dependencies**:
   ```bash
   dotnet restore
   ```

4. **Build the Project**:
   ```bash
   dotnet build
   ```

5. **Run Locally**:
   ```bash
   func start
   # OR
   dotnet run
   ```

   The function will be available at `http://localhost:7071/api/ResizeAndUploadImage`

### Using Azurite for Local Storage

For local development without Azure Storage account:

1. **Install Azurite**:
   ```bash
   npm install -g azurite
   ```

2. **Start Azurite**:
   ```bash
   azurite --silent --location c:\azurite --debug c:\azurite\debug.log
   ```

3. **Update local.settings.json**:
   ```json
   {
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "AzureWebImageStore": "UseDevelopmentStorage=true"
     }
   }
   ```


## Deployment

### Prerequisites

- Azure subscription
- Azure Storage Account
- Azure Function App (Windows or Linux)

### Deployment Methods

####  Visual Studio Code

1. Install Azure Functions extension
2. Right-click on the project folder
3. Select "Deploy to Function App..."
4. Follow the prompts to select or create a Function App


### Post-Deployment Configuration

1. **Configure Application Settings** in Azure Portal:
   - Navigate to Function App > Configuration
   - Add `AzureWebImageStore` connection string
   - Add `APPLICATIONINSIGHTS_CONNECTION_STRING` (if not auto-configured)

2. **Configure CORS** (if needed):
   - Navigate to Function App > CORS
   - Add allowed origins or use `*` for development


## Error Handling

### Error Types and Handling

| Error Scenario | HTTP Code | Response | Handling |
|----------------|-----------|----------|----------|
| Missing image data | 400 | `{"error": "No image data provided"}` | Validate request body |
| Invalid multipart format | 400 | `{"error": "Invalid multipart boundary"}` | Check Content-Type header |
| Missing storage configuration | 500 | `{"error": "AzureWebImageStore not configured"}` | Configure environment variable |
| Image processing failure | 500 | `{"error": "<exception message>"}` | Check image format and size |
| Storage upload failure | 500 | `{"error": "<exception message>"}` | Verify storage account access |

## Monitoring

### Application Insights

The function is integrated with Application Insights for monitoring and telemetry.

**Key Metrics**:
- Request rate
- Response time
- Failure rate
- Dependency calls (Blob Storage)
- Custom metrics (image size, processing time)

**Last Updated**: January 11, 2026
