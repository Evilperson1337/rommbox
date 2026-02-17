using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RomMbox.Models.Romm
{
    /// <summary>
    /// Generic paged result wrapper returned by the RomM API.
    /// </summary>
    internal sealed class PagedResult<T>
    {
        /// <summary>
        /// Page of items returned by the API.
        /// </summary>
        [JsonPropertyName("items")]
        public List<T> Items { get; set; }

        /// <summary>
        /// Page size limit used by the request.
        /// </summary>
        [JsonPropertyName("limit")]
        public int PageSize { get; set; }

        /// <summary>
        /// Offset used for the current page.
        /// </summary>
        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        /// <summary>
        /// Optional total count provided by the server.
        /// </summary>
        [JsonPropertyName("total")]
        public int? Total { get; set; }
    }
}
