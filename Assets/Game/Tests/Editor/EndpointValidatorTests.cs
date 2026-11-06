using MF2.Game.Networking;
using NUnit.Framework;

namespace MF2.Tests
{
    public sealed class EndpointValidatorTests
    {
        [TestCase("127.0.0.1", "7770", 7770)]
        [TestCase("localhost", "1", 1)]
        [TestCase("game.example.com", "65535", 65535)]
        public void ValidEndpoint_IsAccepted(string host, string portText, int expectedPort)
        {
            var valid = EndpointValidator.TryValidate(host, portText, out var normalized, out var port, out var error);

            Assert.That(valid, Is.True, error);
            Assert.That(normalized, Is.EqualTo(host));
            Assert.That(port, Is.EqualTo(expectedPort));
        }

        [TestCase("", "7770")]
        [TestCase("bad host", "7770")]
        [TestCase("localhost", "0")]
        [TestCase("localhost", "65536")]
        [TestCase("localhost", "abc")]
        public void InvalidEndpoint_IsRejected(string host, string portText)
        {
            Assert.That(EndpointValidator.TryValidate(host, portText, out _, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }
    }
}
