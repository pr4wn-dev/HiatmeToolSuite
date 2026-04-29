using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Hiatme_Tool_Suite_v3;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Hiatme.ToolSuite.WellRyde.Tests
{
    public class WellRydeTripParsingTests
    {
        [Fact]
        public void ResolveTripDate_MatchesInvariantSpecificDate_April282026()
        {
            var dt = WellRydeTripParsing.ResolveTripDate("Tuesday, April 28, 2026", 28, 2026);
            Assert.Equal(new DateTime(2026, 4, 28), dt.Date);
            var specific = dt.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            Assert.Equal("April 28, 2026", specific);
        }

        [Fact]
        public void ResolveTripDate_MCTimeCorrectionStyleLongDate_Works()
        {
            var mcDate = new DateTime(2026, 4, 28);
            string longdate = mcDate.DayOfWeek + ", " +
                CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(mcDate.Month) + " " +
                mcDate.Day + ", " + mcDate.Year;
            var dt = WellRydeTripParsing.ResolveTripDate(longdate, mcDate.Day, mcDate.Year);
            Assert.Equal(mcDate.Date, dt.Date);
        }

        [Fact]
        public void ParseWellRydeTotalRecords_NullReturnsZero()
        {
            Assert.Equal(0, WellRydeTripParsing.ParseWellRydeTotalRecords(JValue.CreateNull()));
        }

        [Theory]
        [InlineData("42", 42)]
        [InlineData(17, 17)]
        public void ParseWellRydeTotalRecords_ParsesStringOrInt(object tokenValue, int expected)
        {
            JToken t = tokenValue is int i ? new JValue(i) : JToken.Parse("\"" + tokenValue + "\"");
            Assert.Equal(expected, WellRydeTripParsing.ParseWellRydeTotalRecords(t));
        }

        [Fact]
        public void LooksLikeNonJsonPayload_DetectsHtmlAndEmpty()
        {
            Assert.True(WellRydeTripParsing.LooksLikeNonJsonPayload(""));
            Assert.True(WellRydeTripParsing.LooksLikeNonJsonPayload("   "));
            Assert.True(WellRydeTripParsing.LooksLikeNonJsonPayload("<html>"));
            Assert.True(WellRydeTripParsing.LooksLikeNonJsonPayload("<!DOCTYPE html>"));
            Assert.False(WellRydeTripParsing.LooksLikeNonJsonPayload("{\"a\":1}"));
            Assert.False(WellRydeTripParsing.LooksLikeNonJsonPayload("[1,2]"));
        }

        [Fact]
        public void LooksLikeJsonArray_RequiresBracketStart()
        {
            Assert.True(WellRydeTripParsing.LooksLikeJsonArray("[{\"text\":\"A\"}]"));
            Assert.False(WellRydeTripParsing.LooksLikeJsonArray("{\"values\":[]}"));
            Assert.False(WellRydeTripParsing.LooksLikeJsonArray(""));
        }

        [Fact]
        public void BillSubmitBodyIndicatesSuccess_OnlyPlainSuccess()
        {
            Assert.True(WellRydeTripParsing.BillSubmitBodyIndicatesSuccess("SUCCESS"));
            Assert.True(WellRydeTripParsing.BillSubmitBodyIndicatesSuccess("  SUCCESS  "));
            Assert.False(WellRydeTripParsing.BillSubmitBodyIndicatesSuccess("<html>"));
            Assert.False(WellRydeTripParsing.BillSubmitBodyIndicatesSuccess("{\"ok\":true}"));
        }

        [Fact]
        public void TripFilterList_Legacy_Sequence7_MatchesPhp_StringSequences_AndDateValueAsJsonString()
        {
            var tripDate = new DateTime(2026, 4, 28);
            var specificDate = tripDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            var dateSlotInner = JsonConvert.SerializeObject(new { specificDate });
            var filterList = new object[]
            {
                new { sequence = "1", value = "-1" },
                new { sequence = "2", value = "-1" },
                new { sequence = "3", value = "-1" },
                new { sequence = "4", value = "-1" },
                new { sequence = "5", value = "-1" },
                new { sequence = "6", value = "-1" },
                new { sequence = "7", value = dateSlotInner },
                new { sequence = "8", value = "-1" },
                new { sequence = "9", value = "-1" },
                new { sequence = "10", value = "-1" },
            };
            string filterListJson = JsonConvert.SerializeObject(filterList);
            Assert.Contains("April 28, 2026", filterListJson);
            Assert.Contains("specificDate", filterListJson);
            Assert.Contains("\"sequence\":\"7\"", filterListJson);
            Assert.Contains("\"value\":\"{\\\"specificDate\\\":\\\"April 28, 2026\\\"}\"", filterListJson);
        }

        [Fact]
        public void TripFilterList_VtTripBilling_MatchesChrome_StringSequence_AndDateValueAsJsonString()
        {
            var dateSlotInner = WellRydeTripParsing.BuildVtTripBillingDateSlotValueJson(new DateTime(2030, 6, 15));
            Assert.Equal("{\"specificDate\":\"June 15, 2030\"}", dateSlotInner);
            var vtList = new object[]
            {
                new { sequence = "1", value = "-1" },
                new { sequence = "2", value = dateSlotInner },
                new { sequence = "3", value = "-1" },
                new { sequence = "4", value = "-1" },
                new { sequence = "5", value = "-1" },
                new { sequence = "6", value = "-1" },
            };
            var json = JsonConvert.SerializeObject(vtList);
            Assert.Contains("\"sequence\":\"2\"", json);
            Assert.Contains("\"value\":\"{\\\"specificDate\\\":\\\"June 15, 2030\\\"}\"", json);
        }

        [Fact]
        public void BuildVtTripBillingDateSlotValueJson_Today_UsesPeriod0d_ChromeHar()
        {
            var inner = WellRydeTripParsing.BuildVtTripBillingDateSlotValueJson(DateTime.Today);
            Assert.Contains("period", inner);
            Assert.Contains("0d", inner);
            Assert.Equal("{\"period\":\"0d\"}", inner);
        }

        [Fact]
        public void ResponseIndicatesPortalLogin_WhenRequestUriContainsLogin()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://portal.app.wellryde.com/portal/login");
            var res = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            Assert.True(WellRydeTripParsing.ResponseIndicatesPortalLogin(res));
        }

        [Fact]
        public void ResponseIndicatesPortalLogin_FalseForTripEndpoint()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://portal.app.wellryde.com/portal/trip/getAllDriversForTripAssignment");
            var res = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            Assert.False(WellRydeTripParsing.ResponseIndicatesPortalLogin(res));
        }
    }
}
