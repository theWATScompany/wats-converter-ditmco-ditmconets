using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Virinco.WATS.Interface;
using WATS.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Virinco.WATS.Converter.DITMCO.Tests
{
    [SupportedOSPlatform("windows7.0")]
    public class ConverterTests : TextConverterTestBase
    {
        public ConverterTests(ITestOutputHelper output) : base(output) { }

        protected override IReportConverter_v2 CreateConverter() => new DITMCONETSConverter();

        private const string RunHeader = "NETS(R) Version 1.7.8440\n12 MAI 26 07:22:54\nC:\\PROGRAMS\\EXAMPLE.RO\n" +
            "EXAMPLE-PART Example product\nREV.2.0\nSERIAL N0: EXAMPLE-SERIAL\nOperator: EXAMPLE-OP\nTest Procedure:\n";
        private const string RunFooter = "; ERR,0\nNET errors: 000000\nBulk errors: 000000\nTwo point errors: 000000\nTotal Test Count: 000003\n";

        private UUTReport[] ConvertText(string text, DITMCONETSConverter converter = null)
        {
            Api.ClearCaptured();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            Assert.Null((converter ?? new DITMCONETSConverter()).ImportReport(Api, stream));
            Assert.All(Api.ValidationResults, result => Assert.True(result.IsValid, string.Join("; ", result.Errors)));
            return Api.CapturedReports.ToArray();
        }

        [Fact, Trait("TestMode", "ConvertOnly")]
        public void AppendedRunsRetainFailuresAndResetContext()
        {
            string failed = RunHeader + "F\tSMU\t48V\t100M\t.1S\t.1T\n; C\tSMU\t.5A\t9R\t.05S\t.01T\n" +
                "; BTB Interconnect\n: FF 00001 J1-1 NET_A\n: 00002 LOW 7.2M OHM J1-2 NET_B\n" +
                ": CV 00003 J1-3 NET_C\n: 00004 HIGH >30M OHM J1-4 NET_C\n" +
                ": XS 00003 J1-3 NET_C\n: 00005 WIRED 123m OHM\n" +
                ": XT 00004 J1-4 NET_C\n: ISOLATED >30M OHM J1-4 NET_C\n" + RunFooter + "TEST PASSED!!!\n";
            string passed = RunHeader.Replace("12 MAI", "13 MAI") + ": GG 00006 J2-1 POWER\n: 00007 PASS 12.95 uf J2-2 GND\n" + RunFooter;
            var converter = new DITMCONETSConverter();
            var reports = ConvertText(failed + passed, converter);
            Assert.Equal(2, reports.Length);
            Assert.Equal(UUTStatusType.Failed, reports[0].Status);
            Assert.Equal(UUTStatusType.Passed, reports[1].Status);
            Assert.Equal(new DateTime(2026, 5, 13, 7, 22, 54), reports[1].StartDateTime);
            var steps = reports[0].AllSteps.OfType<NumericLimitStep>().ToArray();
            Assert.Equal(4, steps.Length);
            Assert.Equal(new[] { StepStatusType.Failed, StepStatusType.Failed, StepStatusType.Done, StepStatusType.Done }, steps.Select(step => step.Status));
            Assert.Contains("From: 00004 J1-4 NET_C", steps[3].ReportText);
            Assert.Contains("From: 00001 J1-1 NET_A", steps[0].ReportText);
            Assert.Contains("Parameters: F", steps[0].ReportText);
            Assert.Contains("Parameters: C", steps[1].ReportText);
            Assert.Equal(0.123, steps[2].Tests[0].NumericValue, 10);
            Assert.Equal(StepStatusType.Failed, steps[0].Parent.Status);
            var capacitance = Assert.Single(reports[1].AllSteps.OfType<NumericLimitStep>());
            Assert.Equal(12.95, capacitance.Tests[0].NumericValue);
            Assert.Equal("uf", capacitance.Tests[0].Units);
            Assert.DoesNotContain("Parameters:", capacitance.ReportText);
            Assert.Equal("EXAMPLE", reports[1].GetRootSequenceCall().SequenceName);
            Assert.Single(ConvertText(passed, converter));
        }

        [Fact, Trait("TestMode", "ConvertOnly")]
        public void OptionalMetadataAndConfiguredFieldsDoNotUseStationIdentity()
        {
            const string metadata = "; BTB TEST_INFO\nTest Station Serial No: STATION-ONLY\n" +
                "Custom Unit: UNIT-EXAMPLE\nCustom Part: PART-EXAMPLE\nFixture: FIXTURE-EXAMPLE\n; ETB TEST_INFO\n";
            string text = RunHeader + metadata + ": FF 00001 PASS 1G OHM J1-1 NET_A\n" + RunFooter;
            Assert.Equal("EXAMPLE-SERIAL", Assert.Single(ConvertText(text)).SerialNumber);
            var configured = new DITMCONETSConverter(new Dictionary<string, string>
            {
                { "serialNumberField", "Custom Unit" }, { "partNumberField", "Custom Part" }, { "requiredFields", "Fixture" }
            });
            var report = Assert.Single(ConvertText(text, configured));
            Assert.Equal("UNIT-EXAMPLE", report.SerialNumber);
            Assert.Equal("PART-EXAMPLE", report.PartNumber);
            Assert.Throws<InvalidDataException>(() => ConvertText(text.Replace("Fixture: FIXTURE-EXAMPLE\n", ""), configured));
            Assert.Throws<InvalidDataException>(() => ConvertText(text.Replace("SERIAL N0: EXAMPLE-SERIAL\n", "")));
            Assert.Empty(Api.CapturedReports);
        }

        [Theory, Trait("TestMode", "ConvertOnly")]
        [InlineData("not a NETS log")]
        [InlineData(RunHeader + ": FF 00001 PASS bad OHM J1-1 NET_A\n" + RunFooter)]
        [InlineData(RunHeader + ": FF 00001 UNKNOWN 10 OHM J1-1 NET_A\n" + RunFooter)]
        [InlineData(RunHeader + ": FF 00001 PASS 10 OHM J1-1 NET_A\n; ERR,0\n")]
        public void InvalidRunsNeverSubmit(string text)
        {
            Assert.Throws<InvalidDataException>(() => ConvertText(text));
            Assert.Empty(Api.CapturedReports);
        }

        [Fact, Trait("TestMode", "ConvertOnly")]
        public void InvalidLaterRunPreventsPartialSubmission()
        {
            Assert.Throws<InvalidDataException>(() => ConvertText(RunHeader + RunFooter + RunHeader));
            Assert.Empty(Api.CapturedReports);
        }

        [ExternalArchiveFact, Trait("TestMode", "ExternalArchive")]
        public void ExternalArchive_AllRunsAndMeasurements()
        {
            string path = Environment.GetEnvironmentVariable("DITMCO_TEST_ARCHIVE");
            Assert.True(File.Exists(path), "Set DITMCO_TEST_ARCHIVE to a local ZIP; raw customer logs must remain outside Public/.");
            using var archive = ZipFile.OpenRead(path);
            var entries = archive.Entries.Where(entry => entry.Name.EndsWith(".LOG", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.NotEmpty(entries);
            var errors = new List<string>();
            int runCount = 0, measurementCount = 0, failedCount = 0;
            for (int fileIndex = 0; fileIndex < entries.Length; fileIndex++)
            {
                try
                {
                    using var reader = new StreamReader(entries[fileIndex].Open());
                    string text = reader.ReadToEnd();
                    var runs = Regex.Split(text, @"(?m)(?=^NETS\(R\) Version)").Where(run => !string.IsNullOrWhiteSpace(run)).ToArray();
                    var reports = ConvertText(text);
                    Assert.Equal(runs.Length, reports.Length);
                    for (int runIndex = 0; runIndex < runs.Length; runIndex++)
                    {
                        string source = runs[runIndex];
                        var report = reports[runIndex];
                        var rows = source.Split('\n').Where(line => line.TrimStart().StartsWith(":"))
                            .Where(line => Regex.IsMatch(line, @"\b(PASS|FAIL|LOW|HIGH|WIRED|ISOLATED)\b")).ToArray();
                        var steps = report.AllSteps.OfType<NumericLimitStep>().ToArray();
                        Assert.Equal(rows.Length, steps.Length);
                        Assert.Equal(Regex.Match(source, @"(?m)^\s*SERIAL N0:\s*(\S+)").Groups[1].Value, report.SerialNumber);
                        var product = Regex.Match(source, @"(?m)^\s*(\S+) [^\r\n]+\r?\n\s*REV\.");
                        Assert.True(product.Success);
                        Assert.Equal(product.Groups[1].Value, report.PartNumber);
                        string timestamp = source.Split('\n')[1].Trim().Replace("MAI", "MAY").Replace("OKT", "OCT").Replace("DES", "DEC");
                        Assert.Equal(DateTime.ParseExact(timestamp, "dd MMM yy HH:mm:ss", CultureInfo.InvariantCulture), report.StartDateTime);
                        bool failed = Regex.IsMatch(source, @"(?m)^:.*\b(FAIL|LOW|HIGH)\b|TEST FAILED")
                            || Regex.Matches(source, @"(?m)^.*errors:\s*(\d+)").Any(match => int.Parse(match.Groups[1].Value) > 0);
                        Assert.Equal(failed ? UUTStatusType.Failed : UUTStatusType.Passed, report.Status);
                        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                        {
                            string row = rows[rowIndex].Trim();
                            Assert.StartsWith("Source: " + row, steps[rowIndex].ReportText);
                            var expected = Regex.Match(row, @"\b(PASS|FAIL|LOW|HIGH|WIRED|ISOLATED)\s+([<>]=?)?([-+\d.]+)([GMKm]?)\s+(\S+)");
                            Assert.True(expected.Success);
                            decimal scale = expected.Groups[4].Value switch { "G" => 1000000000m, "M" => 1000000m, "K" => 1000m, "m" => .001m, _ => 1m };
                            double value = (double)(decimal.Parse(expected.Groups[3].Value, CultureInfo.InvariantCulture) * scale);
                            var measurement = Assert.Single(steps[rowIndex].Tests);
                            Assert.True(Math.Abs(measurement.NumericValue - value) <= Math.Max(1e-10, Math.Abs(value) * 1e-12));
                            Assert.Equal(expected.Groups[5].Value, measurement.Units);
                            var status = expected.Groups[1].Value == "PASS" ? StepStatusType.Passed : expected.Groups[1].Value == "WIRED" || expected.Groups[1].Value == "ISOLATED" ? StepStatusType.Done : StepStatusType.Failed;
                            Assert.Equal(status, steps[rowIndex].Status);
                            if (status == StepStatusType.Failed)
                                Assert.Equal(StepStatusType.Failed, steps[rowIndex].Parent.Status);
                        }
                        runCount++;
                        measurementCount += rows.Length;
                        if (failed)
                            failedCount++;
                    }
                }
                catch (Exception exception) { errors.Add($"File #{fileIndex + 1}: {exception.Message}"); }
            }
            Output.WriteLine($"Files={entries.Length}; runs={runCount}; measurements={measurementCount}; failed runs={failedCount}; errors={errors.Count}");
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }

        [Fact, Trait("TestMode", "ConvertOnly")]
        public void LegacyHeaderWithoutMetadataRetainsIdentityAndMeasurements()
        {
            const string text = "NETS(R) Version 1.0.6899\n06 OKT 21 11:41:48\nC:\\PROGRAMS\\EXAMPLE.RO\n" +
                "  EXAMPLE-PART Example product\n  REV.1.0.0\n  SERIAL N0: EXAMPLE-SERIAL\n" +
                "  Test Prosedure: EXAMPLE-TEST\n; ENB,CES,PAT\n" +
                ":       FF  00115        PASS  >1.065G    OHM J1-1 NET_A\n" +
                "; ERR,0\nNET errors: 000000\nBulk errors: 000000\nTwo point errors: 000000\nTotal Test Count: 000001\n";
            var api = new SimulatedTDM();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            Assert.Null(new DITMCONETSConverter().ImportReport(api, stream));
            var report = Assert.Single(api.CapturedReports);
            Assert.Equal("EXAMPLE-SERIAL", report.SerialNumber);
            Assert.Equal("EXAMPLE-PART", report.PartNumber);
            Assert.Equal(new System.DateTime(2021, 10, 6, 11, 41, 48), report.StartDateTime);
            var measurement = Assert.Single(report.AllSteps.OfType<NumericLimitStep>());
            Assert.Equal(1.065e9, Assert.Single(measurement.Tests).NumericValue);
            Assert.Contains(">1.065G", measurement.ReportText);
        }

        [Fact, Trait("TestMode", "ConvertOnly")]
        public void ConvertOnly_AllFiles() => RunAllFiles(TestMode.ConvertOnly);

        [Theory, Trait("TestMode", "ConvertOnly")]
        [InlineData("legacy-pass.LOG", "SYNTHETIC-001", "SYNTHETIC-PART-A", "Passed", 3)]
        [InlineData("metadata-fail.LOG", "SYNTHETIC-002", "SYNTHETIC-PART-B", "Failed", 5)]
        [InlineData("appended-retest.LOG", "SYNTHETIC-003", "SYNTHETIC-PART-C", "Failed,Passed", 4)]
        public void PublicExamplesHaveExpectedReports(string fileName, string serial, string part, string outcomes, int readings)
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", fileName));
            var reports = ConvertText(text);
            Assert.Equal(outcomes.Split(','), reports.Select(report => report.Status.ToString()));
            Assert.Equal(readings, reports.Sum(report => report.AllSteps.OfType<NumericLimitStep>().Count()));
            Assert.All(reports, report =>
            {
                Assert.Equal(serial, report.SerialNumber);
                Assert.Equal(part, report.PartNumber);
                Assert.True(report.ExecutionTime > 0);
                Assert.All(report.AllSteps.OfType<NumericLimitStep>(), step =>
                {
                    Assert.Single(step.Tests);
                    Assert.Contains("Source:", step.ReportText);
                });
            });
            if (fileName == "legacy-pass.LOG")
                Assert.Equal(new[] { 1.2e9, 0.75, 12.5 }, reports[0].AllSteps.OfType<NumericLimitStep>().Select(step => step.Tests[0].NumericValue));
            if (fileName == "metadata-fail.LOG")
                Assert.Equal(2, reports[0].AllSteps.OfType<NumericLimitStep>().Count(step => step.Status == StepStatusType.Done));
            if (reports.Length == 2)
                Assert.True(reports[1].StartDateTime > reports[0].StartDateTime);
        }

        [Fact, Trait("TestMode", "ConvertAndValidate")]
        public void ConvertAndValidate_AllFiles() => RunAllFiles(TestMode.ConvertAndValidate);

        [Fact, Trait("TestMode", "ConvertAndUpload"), Trait("RequiresServer", "true")]
        public void ConvertAndUpload_AllFiles() => RunAllFiles(TestMode.ConvertAndUpload);
    }

    public sealed class ExternalArchiveFactAttribute : FactAttribute
    {
        public ExternalArchiveFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DITMCO_TEST_ARCHIVE")))
                Skip = "Requires an explicit local DITMCO_TEST_ARCHIVE path.";
        }
    }
}
