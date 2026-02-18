using System;

namespace RomMbox.Tests.Utilities
{
    internal sealed class TestEnvironmentScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public TestEnvironmentScope(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previous);
        }
    }
}
