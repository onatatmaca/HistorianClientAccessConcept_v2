using HistorianSyncTool.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace HistorianSyncTool.Tests
{
    [TestClass]
    public class HostInputParserTests
    {
        [TestMethod]
        public void Parse_PlainHostname_NoPort()
        {
            var (host, port) = HostInputParser.Parse("GENTHIN");
            Assert.AreEqual("GENTHIN", host);
            Assert.IsNull(port);
        }

        [TestMethod]
        public void Parse_HostnameWithPort()
        {
            var (host, port) = HostInputParser.Parse("GENTHIN:14000");
            Assert.AreEqual("GENTHIN", host);
            Assert.AreEqual(14000, port);
        }

        [TestMethod]
        public void Parse_IpWithPort()
        {
            var (host, port) = HostInputParser.Parse("192.168.50.186:13000");
            Assert.AreEqual("192.168.50.186", host);
            Assert.AreEqual(13000, port);
        }

        [TestMethod]
        public void Parse_TrimsWhitespace()
        {
            var (host, port) = HostInputParser.Parse("  testsv1 : 13000 ");
            Assert.AreEqual("testsv1", host);
            Assert.AreEqual(13000, port);
        }

        [TestMethod]
        public void Parse_EmptyInput_EmptyHost()
        {
            Assert.AreEqual("", HostInputParser.Parse(null).Host);
            Assert.AreEqual("", HostInputParser.Parse("   ").Host);
        }

        [TestMethod]
        public void Parse_BadPort_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() => HostInputParser.Parse("host:abc"));
            Assert.ThrowsException<ArgumentException>(() => HostInputParser.Parse("host:0"));
            Assert.ThrowsException<ArgumentException>(() => HostInputParser.Parse("host:70000"));
            Assert.ThrowsException<ArgumentException>(() => HostInputParser.Parse(":14000"));
        }

        [TestMethod]
        public void Parse_Ipv6Literal_TreatedAsPlainHost()
        {
            // multiple colons → not our host:port syntax; passed through untouched
            var (host, port) = HostInputParser.Parse("fe80::1");
            Assert.AreEqual("fe80::1", host);
            Assert.IsNull(port);
        }

        [TestMethod]
        public void IsIpAddress_DetectsIpv4Only()
        {
            Assert.IsTrue(HostInputParser.IsIpAddress("192.168.50.186"));
            Assert.IsTrue(HostInputParser.IsIpAddress("10.0.0.1"));
            Assert.IsFalse(HostInputParser.IsIpAddress("GENTHIN"));
            Assert.IsFalse(HostInputParser.IsIpAddress("testsv1pc2"));
            Assert.IsFalse(HostInputParser.IsIpAddress(""));
            Assert.IsFalse(HostInputParser.IsIpAddress(null));
        }
    }
}
