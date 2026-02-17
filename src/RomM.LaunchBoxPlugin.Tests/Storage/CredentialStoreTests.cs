using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Storage;

namespace RomMbox.Tests.Storage
{
    [TestClass]
    public class CredentialStoreTests
    {
        [TestMethod]
        public void SaveCredentials_WithNullServerUrl_ShouldThrow()
        {
            var store = new CredentialStore();
            Assert.ThrowsException<ArgumentNullException>(() => store.SaveCredentials(null, "user", "pass"));
        }

        [TestMethod]
        public void SaveCredentials_WithEmptyServerUrl_ShouldThrow()
        {
            var store = new CredentialStore();
            Assert.ThrowsException<ArgumentException>(() => store.SaveCredentials("", "user", "pass"));
        }
    }
}
