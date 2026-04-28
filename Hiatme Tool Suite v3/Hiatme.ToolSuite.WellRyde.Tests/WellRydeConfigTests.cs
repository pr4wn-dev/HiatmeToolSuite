using System;
using Hiatme_Tool_Suite_v3;
using Xunit;

namespace Hiatme.ToolSuite.WellRyde.Tests
{
    public class WellRydeConfigTests
    {
        [Fact]
        public void Defaults_MatchPhpDocs_WellRydeMd()
        {
            Assert.Equal("https://portal.app.wellryde.com", WellRydeConfig.PortalOrigin);
            Assert.Equal("https://portal.app.wellryde.com/portal/nu", WellRydeConfig.TripsPageAbsoluteUrl);
            Assert.Equal("https://portal.app.wellryde.com/portal/login", WellRydeConfig.LoginPageAbsoluteUrl);
            Assert.Equal("SEC-J0JwBzGuni0ZopMPBRCNuQ", WellRydeConfig.ListDefId);
            Assert.Equal("https://portal.app.wellryde.com/portal/filterdata", WellRydeConfig.FilterDataUrl);
            Assert.Equal(200, WellRydeConfig.FilterDataPageSize);
        }

        [Fact]
        public void UsesVtTripBilling_IsFalse_WhenListDefId_IsLegacySecJ()
        {
            WellRydeConfig.ResolvedTripListDefId = null;
            Assert.StartsWith("SEC-J", WellRydeConfig.ListDefId, StringComparison.Ordinal);
            Assert.False(WellRydeConfig.UsesVtTripBillingFilterListShape());
        }

        [Fact]
        public void TripFilterListDefId_MatchesListDefId_WhenNoResolvedOverride()
        {
            WellRydeConfig.ResolvedTripListDefId = null;
            Assert.Equal(WellRydeConfig.ListDefId, WellRydeConfig.TripFilterListDefId);
        }

        [Fact]
        public void UsesVtTripBilling_IsTrue_WhenResolvedTripList_IsSecS()
        {
            try
            {
                WellRydeConfig.ResolvedTripListDefId = "SEC-S_XoEZX6lDWauVBtgu7FHw";
                Assert.True(WellRydeConfig.UsesVtTripBillingFilterListShape());
            }
            finally
            {
                WellRydeConfig.ResolvedTripListDefId = null;
            }
        }
    }
}
