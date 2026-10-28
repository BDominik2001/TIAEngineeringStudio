using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using TiaStudio.Contracts;
using TiaStudio.Domain.Model;
using TiaStudio.Domain.Rules;
using TiaStudio.Infrastructure.Json;
using Xunit;

namespace TiaStudio.UnitTests
{
    public class ContractCaseTests
    {
        public static IEnumerable<object[]> Cases()
        {
            JObject file = JObject.Parse(File.ReadAllText(Repo.PathOf("tests", "Contracts", "validation-cases.json")));
            return file["cases"].Select(c => new object[] { (string)c["name"] });
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SharedCaseMatchesExpectedIssues(string name)
        {
            JObject file = JObject.Parse(File.ReadAllText(Repo.PathOf("tests", "Contracts", "validation-cases.json")));
            JToken testCase = file["cases"].Single(c => (string)c["name"] == name);
            byte[] input = testCase["input_raw"] != null
                ? Encoding.UTF8.GetBytes((string)testCase["input_raw"])
                : Encoding.UTF8.GetBytes(testCase["input"].ToString(Newtonsoft.Json.Formatting.None));

            ValidationReport report = new MachineProjectJsonValidator().Validate(input);

            List<string> expected = testCase["expected"].Select(e => (string)e["severity"] + " " + (string)e["code"] + " @ " + (string)e["path"]).ToList();
            List<string> actual = report.Issues.Select(i => i.Severity + " " + i.Code + " @ " + i.Path).ToList();
            Assert.Equal(expected, actual);
            Assert.Equal(report.ErrorCount == 0, report.Ok);
        }

        [Fact]
        public void RulesVersionMatchesSharedCases()
        {
            JObject file = JObject.Parse(File.ReadAllText(Repo.PathOf("tests", "Contracts", "validation-cases.json")));
            Assert.Equal(NamingRules.RulesVersion, (string)file["rules_version"]);
        }
    }

    public class ValidatorLimitTests
    {
        private static byte[] ExampleWithDevices(int count)
        {
            JObject doc = JObject.Parse(File.ReadAllText(Repo.PathOf("examples", "machine-v18.json")));
            doc["devices"] = new JArray(Enumerable.Range(0, count).Select(i => new JObject
            {
                ["id"] = "CYL_" + i.ToString("000"),
                ["kind"] = "cylinder",
                ["inputs"] = new JObject { ["retracted"] = "I" + i + ".0", ["extended"] = "I" + i + ".1" },
            }));
            return Encoding.UTF8.GetBytes(doc.ToString());
        }

        [Fact]
        public void WorstCaseDeviceCountIsAccepted()
        {
            Assert.True(new MachineProjectJsonValidator().Validate(ExampleWithDevices(NamingRules.MaxDevices)).Ok);
        }

        [Fact]
        public void TooManyDevicesIsSingleError()
        {
            ValidationReport report = new MachineProjectJsonValidator().Validate(ExampleWithDevices(NamingRules.MaxDevices + 1));
            Assert.Equal(new[] { ErrorCodes.TooManyDevices }, report.Issues.Select(i => i.Code));
        }

        [Fact]
        public void BomAndOversizeAndNaN()
        {
            MachineProjectJsonValidator validator = new MachineProjectJsonValidator();
            byte[] example = File.ReadAllBytes(Repo.PathOf("examples", "machine-v19.json"));
            Assert.True(validator.Validate(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(example).ToArray()).Ok);
            Assert.Equal(ErrorCodes.InputTooLarge, validator.Validate(new byte[NamingRules.MaxInputBytes + 1]).Issues.Single().Code);
            Assert.Equal(ErrorCodes.InvalidJson, validator.Validate(Encoding.ASCII.GetBytes("{\"a\": NaN}")).Issues.Single().Code);
            Assert.Equal(ErrorCodes.InvalidJson, validator.Validate(Encoding.ASCII.GetBytes("{\"a\": 1, // comment\n}")).Issues.Single().Code);
            Assert.Equal(ErrorCodes.InvalidJson, validator.Validate(new byte[] { 0x7B, 0x22, 0xFF, 0x22, 0x3A, 0x31, 0x7D }).Issues.Single().Code);
        }

        [Fact]
        public void HugeIntegerIsInvalidValue()
        {
            string json = File.ReadAllText(Repo.PathOf("examples", "machine-v18.json")).Replace("\"monitor_time_ms\": 3000", "\"monitor_time_ms\": 100000000000000000000000000000");
            Assert.Equal(new[] { ErrorCodes.InvalidValue }, new MachineProjectJsonValidator().Validate(Encoding.UTF8.GetBytes(json)).Issues.Select(i => i.Code));
        }

        [Theory]
        [InlineData("V18")]
        [InlineData("V19")]
        [InlineData("V20")]
        public void ExamplesAreValidAndTargetTheirVersion(string version)
        {
            byte[] bytes = File.ReadAllBytes(Repo.PathOf("examples", "machine-" + version.ToLowerInvariant() + ".json"));
            ValidationReport report = new MachineProjectJsonValidator().Validate(bytes);
            Assert.True(report.Ok);
            Assert.Equal(new[] { ErrorCodes.IoHwNotVerified }, report.Issues.Select(i => i.Code));
            Assert.Equal(version, new MachineProjectSerializer().Deserialize(bytes).Target.TiaVersion);
        }
    }

    public class SerializerTests
    {
        [Fact]
        public void RoundTripIsDeterministicAndValid()
        {
            MachineProjectSerializer serializer = new MachineProjectSerializer();
            MachineProject project = serializer.Deserialize(File.ReadAllBytes(Repo.PathOf("examples", "machine-v18.json")));
            byte[] first = serializer.Serialize(project);
            byte[] second = serializer.Serialize(serializer.Deserialize(first));
            Assert.Equal(first, second);
            Assert.True(new MachineProjectJsonValidator().Validate(first).Ok);
            CylinderDevice cylinder = Assert.IsType<CylinderDevice>(project.Devices[1]);
            Assert.Equal(3000, cylinder.MonitorTimeMs);
            Assert.Equal("DB_Cylinder_CYL_02", cylinder.InstanceBlockName);
            Assert.False(first.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        }

        [Fact]
        public void DefaultsAreOmitted()
        {
            MachineProject project = new MachineProject
            {
                ProjectName = "Cell",
                Target = new MachineTarget { TiaVersion = "V20", PlcName = "PLC_1", CpuFamily = "S7-1200" },
                Devices = { new CylinderDevice { Id = "CYL_01", RetractedInput = "I0.0", ExtendedInput = "I0.1" } },
            };
            string json = Encoding.UTF8.GetString(new MachineProjectSerializer().Serialize(project));
            Assert.DoesNotContain("parameters", json);
            Assert.DoesNotContain("description", json);
            Assert.True(new MachineProjectJsonValidator().Validate(Encoding.UTF8.GetBytes(json)).Ok);
        }
    }

    public class NamingRuleTests
    {
        [Theory]
        [InlineData("I0.0", true, 0, 0)]
        [InlineData("I32767.7", true, 32767, 7)]
        [InlineData("I99999.0", true, 99999, 0)]
        [InlineData("I100000.0", false, 0, 0)]
        [InlineData("I01.0", false, 0, 0)]
        [InlineData("I0.8", false, 0, 0)]
        [InlineData("%I0.0", false, 0, 0)]
        [InlineData("Q0.0", false, 0, 0)]
        [InlineData("I0..0", false, 0, 0)]
        [InlineData("I.0", false, 0, 0)]
        [InlineData("I0.", false, 0, 0)]
        [InlineData("", false, 0, 0)]
        public void InputAddressParsing(string text, bool ok, int byteIndex, int bit)
        {
            Assert.Equal(ok, IoAddress.TryParseInput(text, out IoAddress address));
            if (ok)
            {
                Assert.Equal(byteIndex, address.ByteIndex);
                Assert.Equal(bit, address.Bit);
                Assert.Equal(text, address.ToString());
            }
        }

        [Theory]
        [InlineData("CYL_01", true)]
        [InlineData("AX_X01", true)]
        [InlineData("MTR1", true)]
        [InlineData("CYL__01", false)]
        [InlineData("CYL_", false)]
        [InlineData("_CYL", false)]
        [InlineData("cyl_01", false)]
        [InlineData("1CYL", false)]
        public void DeviceIds(string id, bool ok)
        {
            Assert.Equal(ok, NamingRules.IsDeviceId(id));
        }
    }

    public class ErrorCodeCatalogueTests
    {
        [Fact]
        public void CSharpConstantsMatchSharedCatalogue()
        {
            JObject catalogue = (JObject)JObject.Parse(File.ReadAllText(Repo.PathOf("contracts", "error-codes.json")))["codes"];
            List<string> constants = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (string)f.GetValue(null)).OrderBy(c => c).ToList();
            Assert.Equal(catalogue.Properties().Select(p => p.Name).OrderBy(c => c).ToList(), constants);
        }

        [Fact]
        public void ProtocolHasNoDownloadOnlineOrForceOperation()
        {
            string[] forbidden = { "download", "online", "force", "run", "stop" };
            Assert.DoesNotContain(TiaOperations.All, op => forbidden.Any(f => op.ToLowerInvariant().Contains(f)));
            JObject schema = JObject.Parse(File.ReadAllText(Repo.PathOf("contracts", "tia-command.schema.json")));
            List<string> schemaOperations = schema["properties"]["operation"]["enum"].Select(t => (string)t).ToList();
            Assert.Equal(TiaOperations.All.OrderBy(o => o), schemaOperations.OrderBy(o => o));
        }
    }
}

namespace TiaStudio.UnitTests
{
    /// <summary>Spec 12: V21-es hivatkozás és V21-es assembly-felosztás nem maradhat a forrásban.</summary>
    public class NoV21LeftoversTests
    {
        [Fact]
        public void SourcesContainNoV21ApiOrVersion()
        {
            string[] forbidden = { "V21", "Siemens.Engineering.Base", "Siemens.Engineering.Step7", "TIA_V21_API_PATH", ".ap21" };
            List<string> hits = new List<string>();
            foreach (string file in Directory.GetFiles(Repo.PathOf("src"), "*.*", SearchOption.AllDirectories)
                         .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                         .Where(f => f.EndsWith(".cs") || f.EndsWith(".csproj") || f.EndsWith(".targets") || f.EndsWith(".xaml") || f.EndsWith(".json") || f.EndsWith(".template")))
            {
                string text = File.ReadAllText(file);
                hits.AddRange(forbidden.Where(text.Contains).Select(token => file + ": " + token));
            }
            Assert.Empty(hits);
        }
    }
}
