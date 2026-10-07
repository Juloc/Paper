using System.Net;
using System.Net.Sockets;
using SMBLibrary;
using SMBLibrary.Client;
using SmbAttributes = SMBLibrary.FileAttributes;

namespace Paper.Web.Features.Storage;

/// <summary>
/// Direct SMB2 storage. The provider opens a short-lived authenticated SMB session
/// per operation so the application does not need a host mount or a local CIFS daemon.
/// </summary>
public sealed class SmbStorageProvider : IStorageProvider
{
    private static readonly TimeSpan WakeCooldown = TimeSpan.FromSeconds(30);
    private readonly StorageOptions options;
    private readonly SmbEndpoint endpoint;
    private readonly ILogger<SmbStorageProvider> logger;
    private readonly object wakeLock = new();
    private DateTime lastWakeAtUtc = DateTime.MinValue;

    public SmbStorageProvider(StorageOptions options, ILogger<SmbStorageProvider> logger)
    {
        this.options = options;
        endpoint = SmbEndpoint.Parse(this.options.SmbRootPath ?? throw new InvalidOperationException("Storage:SmbRootPath ist für den SMB-Provider erforderlich."));
        this.logger = logger;
    }

    public SmbStorageProvider(IConfiguration configuration, ILogger<SmbStorageProvider> logger)
        : this(configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions(), logger)
    {
    }

    public Task<StorageConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var connection = OpenConnection();
            return new StorageConnectionTestResult(true, "SMB-Verbindung erfolgreich.");
        }, cancellationToken);
    }

    public async Task<StoredDocument> SaveAsync(Stream source, string originalFileName, CancellationToken cancellationToken)
    {
        var temporaryDirectory = Directory.CreateTempSubdirectory("paper-smb-upload-");
        var temporaryPath = Path.Combine(temporaryDirectory.FullName, "upload");
        try
        {
            long size = 0;
            string hash;
            await using (var temporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                using var incrementalHash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await temporary.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    incrementalHash.AppendData(buffer, 0, read);
                    size += read;
                }

                hash = Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
            }

            var result = await Task.Run(
                () => Upload(temporaryPath, hash, originalFileName, size),
                cancellationToken);
            return new StoredDocument(result.RelativePath, hash, size, result.AlreadyExisted);
        }
        finally
        {
            try
            {
                temporaryDirectory.Delete(recursive: true);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Could not remove temporary SMB upload directory.");
            }
        }
    }

    public Task<string> MoveToShelfAsync(
        string sourceRelativePath,
        string shelfRelativePath,
        DateOnly? documentDate,
        string title,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var folderPath = StoragePathPolicy.NormalizeFolderPath(shelfRelativePath);
        var fileName = StoragePathPolicy.CreateShelfFileName(documentDate, title, originalFileName);
        return Task.Run(() =>
        {
            using var connection = OpenConnection();
            EnsureDirectory(connection, folderPath);
            var extension = Path.GetExtension(fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            for (var attempt = 1; ; attempt++)
            {
                var candidateName = attempt == 1 ? fileName : $"{stem} ({attempt}){extension}";
                var destination = StoragePathPolicy.Combine(folderPath, candidateName);
                if (string.Equals(StoragePathPolicy.NormalizeRelativePath(sourceRelativePath), destination, StringComparison.OrdinalIgnoreCase))
                {
                    return destination;
                }

                try
                {
                    Move(connection, sourceRelativePath, destination, directory: false);
                    return destination;
                }
                catch (IOException) when (Exists(connection, destination, directory: false) || Exists(connection, destination, directory: true))
                {
                    // A concurrent filing operation claimed this candidate;
                    // retry with the next human-readable suffix.
                }
            }
        }, cancellationToken);
    }

    public Task MoveAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var connection = OpenConnection();
            Move(connection, sourceRelativePath, destinationRelativePath, directory: false);
        }, cancellationToken);

    public Task MoveBackAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken) =>
        MoveAsync(sourceRelativePath, destinationRelativePath, cancellationToken);

    public Task MoveDirectoryAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var connection = OpenConnection();
            Move(connection, sourceRelativePath, destinationRelativePath, directory: true);
        }, cancellationToken);

    public Task DeleteDirectoryAsync(string relativePath, CancellationToken cancellationToken) =>
        Task.Run(() => Execute(connection =>
        {
            var status = connection.Store.CreateFile(
                out var handle,
                out _,
                connection.RemotePath(relativePath),
                AccessMask.DELETE | AccessMask.SYNCHRONIZE,
                SmbAttributes.Directory,
                ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_DIRECTORY_FILE,
                null);
            if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND)
            {
                return true;
            }

            ThrowIfFailed(status, relativePath, "gelöscht");
            try
            {
                status = connection.Store.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
                ThrowIfFailed(status, relativePath, "gelöscht");
            }
            finally
            {
                connection.Store.CloseFile(handle);
            }

            return true;
        }), cancellationToken);

    public bool DirectoryExists(string relativePath) => Execute(connection => Exists(connection, relativePath, directory: true));

    public bool FileExists(string relativePath) => Execute(connection => Exists(connection, relativePath, directory: false));

    public StorageFileMetadata? GetFileMetadata(string relativePath) => Execute(connection =>
    {
        var status = connection.Store.CreateFile(
            out var handle,
            out _,
            connection.RemotePath(relativePath),
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SmbAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);
        if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND)
        {
            return null;
        }

        ThrowIfFailed(status, relativePath, "gelesen");
        try
        {
            status = connection.Store.GetFileInformation(out var information, handle, FileInformationClass.FileStandardInformation);
            ThrowIfFailed(status, relativePath, "gelesen");
            return information is FileStandardInformation standardInformation
                ? new StorageFileMetadata(standardInformation.EndOfFile)
                : throw new IOException($"SMB-Dateiinformationen für {relativePath} waren unvollständig.");
        }
        finally
        {
            connection.Store.CloseFile(handle);
        }
    });

    public void EnsureDirectory(string relativePath) => Execute(connection =>
    {
        EnsureDirectory(connection, relativePath);
        return true;
    });

    public bool TryGetLocalPath(string relativePath, out string path)
    {
        path = string.Empty;
        return false;
    }

    public Stream OpenRead(string relativePath)
    {
        var connection = OpenConnection();
        try
        {
            var status = connection.Store.CreateFile(
                out var handle,
                out _,
                connection.RemotePath(relativePath),
                AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                SmbAttributes.Normal,
                ShareAccess.Read,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SEQUENTIAL_ONLY,
                null);
            ThrowIfFailed(status, relativePath, "geöffnet");
            FileInformation information;
            status = connection.Store.GetFileInformation(out information, handle, FileInformationClass.FileStandardInformation);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                connection.Store.CloseFile(handle);
                ThrowIfFailed(status, relativePath, "gelesen");
            }

            if (information is not FileStandardInformation standardInformation)
            {
                connection.Store.CloseFile(handle);
                throw new IOException($"SMB-Dateiinformationen für {relativePath} waren unvollständig.");
            }

            return new SmbReadStream(connection, handle, standardInformation.EndOfFile);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Delete(string relativePath) => Execute(connection =>
    {
        var status = connection.Store.CreateFile(
            out var handle,
            out _,
            connection.RemotePath(relativePath),
            AccessMask.GENERIC_WRITE | AccessMask.DELETE | AccessMask.SYNCHRONIZE,
            SmbAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);
        if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND)
        {
            return true;
        }

        ThrowIfFailed(status, relativePath, "gelöscht");
        try
        {
            status = connection.Store.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
            ThrowIfFailed(status, relativePath, "gelöscht");
        }
        finally
        {
            connection.Store.CloseFile(handle);
        }

        return true;
    });

    private SmbUploadResult Upload(string localPath, string hash, string originalFileName, long size) => Execute(connection =>
    {
        EnsureDirectory(connection, "inbox");
        for (var collisionIndex = 1; ; collisionIndex++)
        {
            var relativePath = StoragePathPolicy.CreateInboxPath(hash, originalFileName, collisionIndex);
            var status = connection.Store.CreateFile(
                out var handle,
                out _,
                connection.RemotePath(relativePath),
                AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                SmbAttributes.Normal,
                ShareAccess.Read,
                CreateDisposition.FILE_CREATE,
                CreateOptions.FILE_NON_DIRECTORY_FILE,
                null);
            if (status == NTStatus.STATUS_OBJECT_NAME_COLLISION)
            {
                if (MatchesExisting(connection, relativePath, size, hash))
                {
                    return new SmbUploadResult(relativePath, AlreadyExisted: true);
                }

                continue;
            }

            ThrowIfFailed(status, relativePath, "angelegt");
            try
            {
                using var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
                var offset = 0L;
                var buffer = new byte[(int)Math.Min(connection.Store.MaxWriteSize, 1024 * 1024)];
                while (source.Position < source.Length)
                {
                    var read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    var chunk = read == buffer.Length ? buffer : buffer[..read];
                    status = connection.Store.WriteFile(out var written, handle, offset, chunk);
                    ThrowIfFailed(status, relativePath, "geschrieben");
                    if (written != read)
                    {
                        throw new IOException("Der SMB-Server hat nur einen Teil des Dokumentblocks geschrieben.");
                    }

                    offset += written;
                }

                status = connection.Store.FlushFileBuffers(handle);
                ThrowIfFailed(status, relativePath, "geschrieben");
            }
            catch
            {
                TryDelete(connection, relativePath, handle);
                throw;
            }
            finally
            {
                connection.Store.CloseFile(handle);
            }

            return new SmbUploadResult(relativePath, AlreadyExisted: false);
        }
    });

    private bool MatchesExisting(SmbConnection connection, string relativePath, long expectedSize, string expectedHash)
    {
        var status = connection.Store.CreateFile(
            out var handle,
            out _,
            connection.RemotePath(relativePath),
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SmbAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);
        if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND)
        {
            return false;
        }

        ThrowIfFailed(status, relativePath, "gelesen");
        try
        {
            status = connection.Store.GetFileInformation(out var information, handle, FileInformationClass.FileStandardInformation);
            ThrowIfFailed(status, relativePath, "gelesen");
            if (information is not FileStandardInformation standardInformation || standardInformation.EndOfFile != expectedSize)
            {
                return false;
            }

            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var offset = 0L;
            while (offset < expectedSize)
            {
                var requested = (int)Math.Min((long)connection.Store.MaxReadSize, Math.Min(1024 * 1024L, expectedSize - offset));
                status = connection.Store.ReadFile(out var data, handle, offset, requested);
                if (status == NTStatus.STATUS_END_OF_FILE)
                {
                    return false;
                }

                ThrowIfFailed(status, relativePath, "gelesen");
                if (data.Length == 0)
                {
                    return false;
                }

                hash.AppendData(data, 0, data.Length);
                offset += data.Length;
            }

            return string.Equals(
                Convert.ToHexString(hash.GetHashAndReset()),
                expectedHash,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            connection.Store.CloseFile(handle);
        }
    }

    private void Move(SmbConnection connection, string sourceRelativePath, string destinationRelativePath, bool directory)
    {
        var source = StoragePathPolicy.NormalizeRelativePath(sourceRelativePath);
        var destination = StoragePathPolicy.NormalizeRelativePath(destinationRelativePath);
        if (Exists(connection, destination, directory: false) || Exists(connection, destination, directory: true))
        {
            throw new IOException("Der Zieldateipfad existiert bereits.");
        }

        var destinationDirectory = Path.GetDirectoryName(destination.Replace('/', Path.DirectorySeparatorChar))?.Replace(Path.DirectorySeparatorChar, '/');
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            EnsureDirectory(connection, destinationDirectory);
        }

        var status = connection.Store.CreateFile(
            out var handle,
            out _,
            connection.RemotePath(source),
            directory ? AccessMask.DELETE | AccessMask.SYNCHRONIZE : AccessMask.GENERIC_WRITE | AccessMask.DELETE | AccessMask.SYNCHRONIZE,
            directory ? SmbAttributes.Directory : SmbAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            directory ? CreateOptions.FILE_DIRECTORY_FILE : CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);
        ThrowIfFailed(status, source, "verschoben");
        try
        {
            status = connection.Store.SetFileInformation(handle, new FileRenameInformationType2
            {
                ReplaceIfExists = false,
                FileName = connection.RemotePath(destination)
            });
            ThrowIfFailed(status, source, "verschoben");
        }
        finally
        {
            connection.Store.CloseFile(handle);
        }
    }

    private void EnsureDirectory(SmbConnection connection, string relativePath)
    {
        var normalized = StoragePathPolicy.NormalizeRelativePath(relativePath);
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(endpoint.BasePath))
        {
            segments.AddRange(endpoint.BasePath.Split('\\', StringSplitOptions.RemoveEmptyEntries));
        }

        segments.AddRange(normalized.Split('/', StringSplitOptions.RemoveEmptyEntries));
        for (var index = 0; index < segments.Count; index++)
        {
            var path = string.Join('\\', segments.Take(index + 1));
            var status = connection.Store.CreateFile(
                out var handle,
                out _,
                path,
                AccessMask.GENERIC_READ | AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                SmbAttributes.Directory,
                ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                CreateDisposition.FILE_OPEN_IF,
                CreateOptions.FILE_DIRECTORY_FILE,
                null);
            ThrowIfFailed(status, path, "angelegt");
            connection.Store.CloseFile(handle);
        }
    }

    private bool Exists(SmbConnection connection, string relativePath, bool directory)
    {
        var status = connection.Store.CreateFile(
            out var handle,
            out _,
            connection.RemotePath(relativePath),
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            directory ? SmbAttributes.Directory : SmbAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            directory ? CreateOptions.FILE_DIRECTORY_FILE : CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);
        if (status == NTStatus.STATUS_SUCCESS)
        {
            connection.Store.CloseFile(handle);
            return true;
        }

        return false;
    }

    private void TryDelete(SmbConnection connection, string relativePath, object handle)
    {
        try
        {
            connection.Store.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove incomplete SMB upload {Path}.", relativePath);
        }
    }

    private SmbConnection OpenConnection()
    {
        WakeIfRequested();
        var client = new SMB2Client(5000, enableSMB311Support: true, resolveDfsNamespace: false);
        if (!client.Connect(endpoint.Server, SMBTransportType.DirectTCPTransport))
        {
            client.Disconnect();
            throw new IOException($"Der SMB-Server {endpoint.Server} ist nicht erreichbar.");
        }

        var status = client.Login(options.SmbDomain ?? string.Empty, options.SmbUsername ?? string.Empty, options.SmbPassword ?? string.Empty);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            client.Disconnect();
            ThrowIfFailed(status, endpoint.Server, "angemeldet");
        }

        var store = client.TreeConnect(endpoint.Share, out status);
        if (status != NTStatus.STATUS_SUCCESS || store is null)
        {
            client.Logoff();
            client.Disconnect();
            ThrowIfFailed(status, endpoint.Share, "verbunden");
        }

        return new SmbConnection(client, store!, endpoint.BasePath);
    }

    private T Execute<T>(Func<SmbConnection, T> action)
    {
        using var connection = OpenConnection();
        return action(connection);
    }

    private void WakeIfRequested()
    {
        if (options.WakePolicy != WakePolicy.OnDemand || string.IsNullOrWhiteSpace(options.WakeMacAddress))
        {
            return;
        }

        lock (wakeLock)
        {
            var now = DateTime.UtcNow;
            if (now - lastWakeAtUtc < WakeCooldown)
            {
                return;
            }

            WakeOnLan.Send(options.WakeMacAddress, options.WakeBroadcastAddress);
            lastWakeAtUtc = now;
            logger.LogInformation("Sent Wake-on-LAN packet for the configured SMB storage.");
        }
    }

    private static void ThrowIfFailed(NTStatus status, string path, string operation)
    {
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new IOException($"SMB-Datei konnte nicht {operation} werden ({path}): {status}.");
        }
    }

    private sealed class SmbConnection(SMB2Client client, ISMBFileStore store, string basePath) : IDisposable
    {
        public SMB2Client Client { get; } = client;
        public ISMBFileStore Store { get; } = store;
        private string BasePath { get; } = basePath;

        public string RemotePath(string relativePath)
        {
            var normalized = StoragePathPolicy.NormalizeRelativePath(relativePath).Replace('/', '\\');
            return string.IsNullOrWhiteSpace(BasePath) ? normalized : $"{BasePath}\\{normalized}";
        }

        public void Dispose()
        {
            try
            {
                Store.Disconnect();
            }
            finally
            {
                try
                {
                    Client.Logoff();
                }
                finally
                {
                    Client.Disconnect();
                }
            }
        }
    }

    private sealed record SmbUploadResult(string RelativePath, bool AlreadyExisted);

    private sealed class SmbReadStream(SmbConnection connection, object handle, long length) : Stream
    {
        private long position;
        private bool disposed;

        public override bool CanRead => !disposed;
        public override bool CanSeek => !disposed;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if ((uint)offset > (uint)buffer.Length || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (position >= length || count == 0)
            {
                return 0;
            }

            var requested = Math.Min(count, (int)Math.Min(connection.Store.MaxReadSize, length - position));
            var status = connection.Store.ReadFile(out var data, handle, position, requested);
            if (status == NTStatus.STATUS_END_OF_FILE)
            {
                return 0;
            }

            ThrowIfFailed(status, "SMB-Datei", "gelesen");
            var read = Math.Min(data.Length, count);
            Buffer.BlockCopy(data, 0, buffer, offset, read);
            position += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var temporary = new byte[buffer.Length];
            var read = Read(temporary, 0, temporary.Length);
            temporary.AsSpan(0, read).CopyTo(buffer);
            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Read(buffer.Span);
            }, cancellationToken));

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (target < 0 || target > length)
            {
                throw new IOException("Die SMB-Dateiposition liegt außerhalb der Datei.");
            }

            position = target;
            return position;
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                disposed = true;
                if (disposing)
                {
                    connection.Store.CloseFile(handle);
                    connection.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

}

internal static class WakeOnLan
{
    public static void Send(string macAddress, string broadcastAddress)
    {
        var normalized = macAddress.Replace(":", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        if (normalized.Length != 12 || !normalized.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("Storage:WakeMacAddress ist keine gültige MAC-Adresse.");
        }

        var mac = Convert.FromHexString(normalized);
        var packet = new byte[6 + 16 * mac.Length];
        packet.AsSpan(0, 6).Fill(0xff);
        for (var offset = 6; offset < packet.Length; offset += mac.Length)
        {
            mac.CopyTo(packet, offset);
        }

        using var client = new UdpClient { EnableBroadcast = true };
        client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse(broadcastAddress), 9));
    }
}
