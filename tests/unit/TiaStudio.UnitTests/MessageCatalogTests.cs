using System.Linq;
using System.Reflection;
using TiaStudio.Contracts;
using TiaStudio.Infrastructure.Localization;
using Xunit;

namespace TiaStudio.UnitTests
{
    public class MessageCatalogTests
    {
        private static readonly MessageCatalog Catalog = MessageCatalog.Default;

        [Fact]
        public void EmbeddedCatalogueHasHungarianTextForEveryErrorCode()
        {
            string[] codes = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToArray();
            Assert.NotEmpty(codes);
            Assert.All(codes, code => Assert.False(string.IsNullOrWhiteSpace(Catalog.Message(code)), code));
        }

        [Fact]
        public void IssueLineIsLocalizedAndKeepsTheOriginalDetail()
        {
            Assert.Equal(
                "Hiba · devices · A gépdefiníció nem tartalmaz eszközt. (At least one device is required) [NO_DEVICES]",
                Catalog.Issue("error NO_DEVICES @ devices: At least one device is required"));
            Assert.Equal("not an issue line", Catalog.Issue("not an issue line"));
        }

        [Fact]
        public void FailureStatusNamesTheCodeAndTheNextStep()
        {
            string status = Catalog.Status(false, ErrorCodes.NoDevices, "Model validation failed with 1 error(s)");
            Assert.StartsWith("A gépdefiníció nem tartalmaz eszközt. [NO_DEVICES] – ", status);
            Assert.Contains("Munkahenger hozzáadása", status);
            Assert.Equal("SOMETHING_NEW: text", Catalog.Status(false, "SOMETHING_NEW", "text"));
        }

        [Theory]
        [InlineData("Model is valid", "A gépmodell érvényes.")]
        [InlineData("Generated 4 SCL source file(s)", "4 SCL-forrásfájl generálva.")]
        [InlineData("Approved for C:\\sandbox\\p.ap18", "Jóváhagyva: C:\\sandbox\\p.ap18")]
        [InlineData("Something else", "Something else")]
        public void SuccessMessagesAreTranslatedOrPassedThrough(string message, string expected)
        {
            Assert.Equal(expected, Catalog.Status(true, null, message));
        }
    }
}
