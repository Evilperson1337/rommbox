namespace RomMbox.Models.Download
{
    /// <summary>
    /// Progress payload used to report download or extraction status.
    /// </summary>
    internal sealed class DownloadProgress
    {
        /// <summary>
        /// Creates a progress snapshot with the current byte counts.
        /// </summary>
        public DownloadProgress(long bytesReceived, long? totalBytes)
        {
            BytesReceived = bytesReceived;
            TotalBytes = totalBytes;
        }

        /// <summary>
        /// Bytes received so far.
        /// </summary>
        public long BytesReceived { get; }

        /// <summary>
        /// Optional total size for the operation.
        /// </summary>
        public long? TotalBytes { get; }
    }
}
