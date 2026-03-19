using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RomMbox.Services.Auth
{
    /// <summary>
    /// Applies authentication state to outbound RomM HTTP requests.
    /// </summary>
    internal interface IRommAuthProvider
    {
        Task PrepareAsync(HttpClient httpClient, string serverUrl, CancellationToken cancellationToken);
    }
}
