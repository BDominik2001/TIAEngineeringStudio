using TiaStudio.Infrastructure.Localization;
using TiaStudio.Infrastructure.Logging;
using Xunit;

namespace TiaStudio.UnitTests
{
    public class AuditEntryTests
    {
        [Fact]
        public void FailedEventShowsHungarianNamesAndTheErrorText()
        {
            const string line = "{\"details\":{},\"error_code\":\"NO_DEVICES\",\"event\":\"Failed\",\"job_id\":null,"
                + "\"message\":\"Model validation failed with 1 error(s)\",\"run_id\":null,\"session_id\":\"s\","
                + "\"state\":\"FAILED\",\"tia_version\":\"V18\",\"timestamp_utc\":\"2026-10-29T06:56:11.276Z\"}";
            AuditEntry entry = AuditEntry.Parse(line, MessageCatalog.Default);
            Assert.Equal("Hiba", entry.Event);
            Assert.Equal("Failed", entry.EventCode);
            Assert.Equal("FAILED", entry.State);
            Assert.Equal("V18", entry.TiaVersion);
            Assert.True(entry.IsError);
            Assert.Equal("A gépdefiníció nem tartalmaz eszközt. (Model validation failed with 1 error(s))", entry.Message);
            Assert.Matches(@"^\d\d:\d\d:11$", entry.Time);
            Assert.Contains("\"session_id\": \"s\"", entry.Raw);
        }

        [Fact]
        public void UnknownEventAndNonJsonLinesStayReadable()
        {
            AuditEntry entry = AuditEntry.Parse("{\"event\":\"SomethingNew\",\"message\":\"m\"}", MessageCatalog.Default);
            Assert.Equal("SomethingNew", entry.Event);
            Assert.Equal("m", entry.Message);
            Assert.False(entry.IsError);
            Assert.Equal("plain text", AuditEntry.Parse("plain text", MessageCatalog.Default).Message);
        }
    }
}
