using System;
using System.Collections.Generic;

namespace RomM.Platforms.Abstractions.Logging
{
    public interface IPlatformLogger
    {
        void Write(PlatformLogLevel level, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null);
    }
}

