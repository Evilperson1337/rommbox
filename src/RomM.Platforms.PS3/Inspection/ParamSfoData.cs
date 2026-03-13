using System.Collections.Generic;

namespace RomM.Platforms.PS3.Inspection
{
    public sealed class ParamSfoData
    {
        public Dictionary<string, string> Strings { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string GetString(string key)
        {
            return Strings.TryGetValue(key, out var value) ? value : string.Empty;
        }
    }
}
