﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PlasmaShared;

namespace Tests
{
    [TestFixture]
    public class WhoisTests
    {
        [Test]
        public void RunQuery() {
            var whois = new Whois();
            var data = whois.QueryByIp("31.7.187.232");
            Assert.AreEqual("PRIVAX-LTD", data["netname"]);

            data = whois.QueryByIp("62.233.34.238");
            Assert.AreEqual("info@hidemyass.com", data["abuse-mailbox"]);
        }

    }
}
