using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace StudioLog.Core
{
    public class GoogleDriveManager : IDisposable
    {
        private static readonly string[] Scopes = { DriveService.Scope.Drive };

        private static readonly string TokenStorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StudioLog",
            "google-token"
        );

        private static readonly string CredentialsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StudioLog",
            "google-credentials.json"
        );

        private DriveService? _driveService;

        public class DriveFolder
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? DriveId { get; set; }
        }

        private class OAuthCredentials
        {
            public string ClientId { get; set; } = string.Empty;
            public string ClientSecret { get; set; } = string.Empty;
        }

        private static OAuthCredentials LoadCredentials()
        {
            if (!File.Exists(CredentialsPath))
            {
                throw new FileNotFoundException(
                    $"Google Drive is not configured on this machine. Create {CredentialsPath} " +
                    "with {\"ClientId\": \"...\", \"ClientSecret\": \"...\"} using the OAuth Desktop " +
                    "client from the StudioLog Google Cloud project.",
                    CredentialsPath);
            }

            var json = File.ReadAllText(CredentialsPath);
            var creds = JsonSerializer.Deserialize<OAuthCredentials>(json);

            if (creds == null || string.IsNullOrWhiteSpace(creds.ClientId) || string.IsNullOrWhiteSpace(creds.ClientSecret))
            {
                throw new InvalidDataException($"{CredentialsPath} is missing ClientId or ClientSecret.");
            }

            return creds;
        }

        private async Task<DriveService> GetServiceAsync(CancellationToken ct)
        {
            if (_driveService != null)
            {
                Console.WriteLine("[GoogleDriveManager] Reusing cached DriveService.");
                return _driveService;
            }

            Console.WriteLine("[GoogleDriveManager] No cached service, starting AuthorizeAsync...");
            var creds = LoadCredentials();
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret },
                Scopes,
                "user",
                ct,
                new FileDataStore(TokenStorePath, true));
            Console.WriteLine("[GoogleDriveManager] AuthorizeAsync completed.");

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "StudioLog"
            });

            return _driveService;
        }

        public async Task<List<DriveFolder>> ListSharedDrivesAsync(CancellationToken ct = default)
        {
            var service = await GetServiceAsync(ct);
            var request = service.Drives.List();
            request.PageSize = 100;
            var result = await request.ExecuteAsync(ct);

            return (result.Drives ?? new List<Google.Apis.Drive.v3.Data.Drive>())
                .Select(d => new DriveFolder { Id = d.Id, Name = d.Name, DriveId = d.Id })
                .OrderBy(f => f.Name)
                .ToList();
        }

        public async Task<List<DriveFolder>> ListChildFoldersAsync(string folderId, string? driveId, CancellationToken ct = default)
        {
            var service = await GetServiceAsync(ct);
            var request = service.Files.List();
            request.Q = $"'{folderId}' in parents and mimeType='application/vnd.google-apps.folder' and trashed=false";
            request.Fields = "files(id, name)";
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;
            if (driveId != null)
            {
                request.DriveId = driveId;
                request.Corpora = "drive";
            }

            var result = await request.ExecuteAsync(ct);

            return (result.Files ?? new List<Google.Apis.Drive.v3.Data.File>())
                .Select(f => new DriveFolder { Id = f.Id, Name = f.Name, DriveId = driveId })
                .OrderBy(f => f.Name)
                .ToList();
        }

        public async Task<DriveFolder?> FindSharedDriveByNameAsync(string name, CancellationToken ct = default)
        {
            var drives = await ListSharedDrivesAsync(ct);
            return drives.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<DriveFolder?> FindChildFolderByNameAsync(string parentFolderId, string? driveId, string name, CancellationToken ct = default)
        {
            var children = await ListChildFoldersAsync(parentFolderId, driveId, ct);
            return children.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public async Task UploadFileAsync(string localPath, string fileName, string mimeType, string parentFolderId, string? driveId, CancellationToken ct = default)
        {
            Console.WriteLine($"[GoogleDriveManager] UploadFileAsync: localPath={localPath} fileName={fileName} mimeType={mimeType} parentFolderId={parentFolderId} driveId={driveId}");
            var service = await GetServiceAsync(ct);
            Console.WriteLine("[GoogleDriveManager] Got DriveService, building request...");

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
                Parents = new List<string> { parentFolderId }
            };

            await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read);
            var request = service.Files.Create(fileMetadata, stream, mimeType);
            request.SupportsAllDrives = true;

            Console.WriteLine("[GoogleDriveManager] Calling request.UploadAsync...");
            var progress = await request.UploadAsync(ct);
            Console.WriteLine($"[GoogleDriveManager] UploadAsync returned. Status={progress.Status} BytesSent={progress.BytesSent} Exception={progress.Exception}");
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                throw new Exception($"Upload did not complete: {progress.Exception?.Message}", progress.Exception);
            }
        }

        public void Disconnect()
        {
            _driveService?.Dispose();
            _driveService = null;

            if (Directory.Exists(TokenStorePath))
            {
                Directory.Delete(TokenStorePath, true);
            }
        }

        public void Dispose()
        {
            _driveService?.Dispose();
        }
    }
}
