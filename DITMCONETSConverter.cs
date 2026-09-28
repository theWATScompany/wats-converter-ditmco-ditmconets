using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Virinco.WATS.Interface;

namespace Virinco.WATS.Converter.DITMCO
{
    public class DITMCONETSConverter : IReportConverter_v2
    {
        private readonly Dictionary<string, string> parameters = new Dictionary<string, string>
        {
            { "partNumber", "" },
            { "partRevision", "" },
            { "operationTypeCode", "10" },
            { "stationName", "" },
            { "sequenceName", "" },
            { "sequenceVersion", "" },
            { "partNumberField", "" },
            { "serialNumberField", "" },
            { "requiredFields", "" }
        };

        public Dictionary<string, string> ConverterParameters => parameters;
        public DITMCONETSConverter() { }
        public DITMCONETSConverter(IDictionary<string, string> args) : this()
        {
            if (args != null)
                foreach (var pair in args)
                    parameters[pair.Key] = pair.Value;
        }

        public void CleanUp() { }

        public Report ImportReport(TDM api, Stream file)
        {
            var reports = SplitRuns(ReadLines(file)).Select(ParseLog).Select(log => BuildReport(api, log)).ToList();
            foreach (var report in reports)
                report.ValidateForSubmit();
            foreach (var report in reports)
                if (!api.Submit(report))
                    throw new InvalidOperationException("WATS rejected a NETS report submission.");
            return null;
        }

        private UUTReport BuildReport(TDM api, ParsedLog log)
        {
            foreach (var field in (parameters["requiredFields"] ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (GetInfo(log, field.Trim()) == null)
                    throw new InvalidDataException($"Missing required NETS TEST_INFO field '{field.Trim()}'.");

            string serial = GetInfo(log, parameters["serialNumberField"], "SERIAL", "UUT Serial No", "UUT Serial Number")
                ?? log.TestInfo.FirstOrDefault(pair => pair.Key.StartsWith("UUT ", StringComparison.OrdinalIgnoreCase)
                    && pair.Key.IndexOf("serial", StringComparison.OrdinalIgnoreCase) >= 0
                    && !string.IsNullOrWhiteSpace(pair.Value)).Value
                ?? log.Serial;
            if (string.IsNullOrWhiteSpace(serial))
            {
                if (!log.Simulation || !log.Start.HasValue)
                    throw new InvalidDataException("NETS log has no UUT serial number. Configure serialNumberField for custom metadata.");
                serial = "SIM-" + log.Start.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            }

            string part = GetInfo(log, parameters["partNumberField"], "UUT Part Number", "UUT Part No", "Part Number")
                ?? NonEmpty(parameters["partNumber"]) ?? log.PartNumber ?? "NETS-UUT";
            string revision = GetInfo(log, "Revision of UUT", "UUT Revision")
                ?? NonEmpty(parameters["partRevision"]) ?? log.Revision ?? "A";
            string sequence = NonEmpty(log.SequenceName) ?? NonEmpty(parameters["sequenceName"])
                ?? (log.ProgramPath == null ? null : Path.GetFileNameWithoutExtension(log.ProgramPath)) ?? "NETS";
            string version = GetInfo(log, "Test SW Revision") ?? NonEmpty(parameters["sequenceVersion"]) ?? log.Revision ?? "NA";
            var uut = api.CreateUUTReport(GetInfo(log, "Test Operator ID", "Operator", "Test Operator") ?? log.Operator ?? "Unknown",
                part, revision, serial, parameters["operationTypeCode"], sequence, version);
            uut.StationName = NonEmpty(parameters["stationName"]) ?? GetInfo(log, "Test Station Serial No", "Test Station Part No") ?? Environment.MachineName;
            uut.StartDateTime = log.Start.Value;
            if (log.End.HasValue)
                uut.ExecutionTime = Math.Max(0, (log.End.Value - log.Start.Value).TotalSeconds);
            foreach (var pair in log.TestInfo.Concat(log.Summary))
                if (NonEmpty(pair.Value) != null)
                    uut.AddMiscUUTInfo(pair.Key, pair.Value);
            uut.AddMiscUUTInfo("NETS Version", log.Version);
            if (log.ProgramPath != null)
                uut.AddMiscUUTInfo("NETS Program", log.ProgramPath);
            if (log.LogPath != null)
                uut.AddMiscUUTInfo("NETS Log", log.LogPath);
            if (log.Simulation)
                uut.AddMiscUUTInfo("NETS Simulation", "true");
            var root = uut.GetRootSequenceCall();
            foreach (var group in log.Groups.Where(group => group.Measurements.Count > 0))
            {
                var call = root.AddSequenceCall(group.Name);
                foreach (var measurement in group.Measurements)
                {
                    string name = measurement.Code + " " + measurement.StepNumber + " " + measurement.Pin;
                    var step = call.AddNumericLimitStep(name.Trim());
                    if (measurement.Status == StepStatusType.Done)
                    {
                        step.AddTest(measurement.Value, measurement.Unit);
                        step.Status = StepStatusType.Done;
                    }
                    else
                        step.AddTest(measurement.Value, measurement.Unit, measurement.Status);
                    step.ReportText = "Source: " + measurement.Source +
                        (measurement.From == null ? "" : "\nFrom: " + measurement.From) +
                        (measurement.Setup == null ? "" : "\nParameters: " + measurement.Setup);
                }
                call.Status = group.Measurements.Any(measurement => measurement.Status == StepStatusType.Failed)
                    ? StepStatusType.Failed : group.Measurements.Any(measurement => measurement.Status == StepStatusType.Passed)
                    ? StepStatusType.Passed : StepStatusType.Done;
            }
            if (log.Diagnostics.Count > 0)
                root.ReportText = string.Join("\n", log.Diagnostics);
            bool failed = log.FailedBanner || log.Summary.Any(pair => pair.Key.EndsWith("errors", StringComparison.OrdinalIgnoreCase)
                && int.Parse(pair.Value, CultureInfo.InvariantCulture) > 0)
                || log.Groups.SelectMany(group => group.Measurements).Any(measurement => measurement.Status == StepStatusType.Failed);
            var outcome = root.AddPassFailStep("NETS run outcome");
            outcome.AddTest(!failed, failed ? StepStatusType.Failed : StepStatusType.Passed);
            outcome.ReportText = string.Join("\n", log.Summary.Select(pair => pair.Key + ": " + pair.Value));
            root.Status = failed ? StepStatusType.Failed : StepStatusType.Passed;
            uut.Status = failed ? UUTStatusType.Failed : UUTStatusType.Passed;
            return uut;
        }

        private static string GetInfo(ParsedLog log, params string[] keys)
        {
            foreach (var key in keys)
                if (!string.IsNullOrWhiteSpace(key) && log.TestInfo.TryGetValue(key, out var value) && NonEmpty(value) != null)
                    return value.Trim();
            return null;
        }

        private static string NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static readonly Regex Header = new Regex(@"^\s*NETS\s*\(R\)\s*Version\s*(?<version>\S+)", RegexOptions.IgnoreCase);
        private static readonly Regex Timestamp = new Regex(@"^\s*(?<day>\d{1,2})\s+(?<month>[A-Z]{3})\s+(?<year>\d{2,4})\s+(?<time>\d{1,2}:\d{2}:\d{2})\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex KeyValue = new Regex(@"^\s*(?<key>[A-Za-z][A-Za-z0-9 _./-]*?)\s*:\s*(?<value>.*?)\s*$");
        private static readonly Regex Row = new Regex(@"^\s*(?:(?<code>[A-Z]{2})\s+)?(?:(?<step>\d{1,6})\s+|(?=ISOLATED\b))(?<rest>.+?)\s*$");
        private static readonly Regex Number = new Regex(@"^(?<qualifier>[<>]=?)?(?<value>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][-+]?\d+)?)(?<scale>[GMKkmunp]?)$");
        private static readonly Dictionary<string, int> Months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "JAN", 1 }, { "FEB", 2 }, { "MAR", 3 }, { "APR", 4 }, { "MAY", 5 }, { "MAI", 5 },
            { "JUN", 6 }, { "JUL", 7 }, { "AUG", 8 }, { "SEP", 9 }, { "OCT", 10 }, { "OKT", 10 },
            { "NOV", 11 }, { "DEC", 12 }, { "DES", 12 }
        };
        private static readonly Dictionary<string, double> Scales = new Dictionary<string, double>
        {
            { "", 1 }, { "G", 1e9 }, { "M", 1e6 }, { "K", 1e3 }, { "k", 1e3 },
            { "m", 1e-3 }, { "u", 1e-6 }, { "n", 1e-9 }, { "p", 1e-12 }
        };

        private static IEnumerable<IReadOnlyList<string>> SplitRuns(IReadOnlyList<string> lines)
        {
            List<string> run = null;
            foreach (var line in lines)
            {
                if (Header.IsMatch(line))
                {
                    if (run != null)
                        yield return run;
                    run = new List<string>();
                }
                if (run != null)
                    run.Add(line);
                else if (!string.IsNullOrWhiteSpace(line))
                    throw new InvalidDataException("Expected a NETS version header.");
            }
            if (run == null)
                throw new InvalidDataException("No NETS test run found.");
            yield return run;
        }

        private static ParsedLog ParseLog(IReadOnlyList<string> lines)
        {
            var log = new ParsedLog { Version = Header.Match(lines[0]).Groups["version"].Value };
            var setups = new Dictionary<string, string>();
            var group = new ResultGroup { Name = "MAIN" };
            log.Groups.Add(group);
            bool inInfo = false;
            bool inTrailer = false;
            bool seenResult = false;
            string previous = null;
            string code = null;
            string from = null;
            string contextStep = null;
            for (int index = 1; index < lines.Count; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0)
                    continue;
                var timestamp = Timestamp.Match(line);
                if (timestamp.Success)
                {
                    if (!Months.TryGetValue(timestamp.Groups["month"].Value, out var month))
                        throw new InvalidDataException($"Unknown NETS month at line {index + 1}.");
                    int year = int.Parse(timestamp.Groups["year"].Value, CultureInfo.InvariantCulture);
                    if (year < 100)
                        year += 2000;
                    var date = new DateTime(year, month, int.Parse(timestamp.Groups["day"].Value, CultureInfo.InvariantCulture))
                        .Add(TimeSpan.Parse(timestamp.Groups["time"].Value, CultureInfo.InvariantCulture));
                    if (!log.Start.HasValue)
                        log.Start = date;
                    else
                        log.End = date;
                    continue;
                }
                if (line.IndexOf("THIS IS A SIMULATION", StringComparison.OrdinalIgnoreCase) >= 0)
                    log.Simulation = true;
                if (line.StartsWith(";"))
                {
                    var directive = line.Substring(1).Trim();
                    var title = Regex.Match(directive, @"^(?<kind>BTB|ETB)\s+(?<name>[^;]+)", RegexOptions.IgnoreCase);
                    if (title.Success)
                    {
                        bool begin = title.Groups["kind"].Value.Equals("BTB", StringComparison.OrdinalIgnoreCase);
                        string name = title.Groups["name"].Value.Trim();
                        if (name.Equals("TEST_INFO", StringComparison.OrdinalIgnoreCase))
                            inInfo = begin;
                        else
                        {
                            group = new ResultGroup { Name = begin ? name : "MAIN" };
                            log.Groups.Add(group);
                            code = from = null;
                        }
                        continue;
                    }
                    if (directive.StartsWith("SEL ", StringComparison.OrdinalIgnoreCase))
                        log.LogPath = directive.Substring(4).Trim();
                    if (directive.StartsWith("CMP", StringComparison.OrdinalIgnoreCase))
                    {
                        group = new ResultGroup { Name = NonEmpty(directive.Substring(3).Split(';')[0].TrimStart(',', ' ')) ?? "CMP" };
                        log.Groups.Add(group);
                        code = from = null;
                    }
                    if (Regex.IsMatch(directive, @"^ERR(?:,|\s|$)", RegexOptions.IgnoreCase))
                        inTrailer = true;
                    TrySetup(directive, setups);
                    continue;
                }
                var pair = KeyValue.Match(line);
                if (inInfo)
                {
                    if (pair.Success)
                        log.TestInfo[pair.Groups["key"].Value.Trim()] = pair.Groups["value"].Value.Trim().Trim('"');
                    continue;
                }
                if (!seenResult)
                {
                    if (Regex.IsMatch(line, @"^[A-Za-z]:\\.*\.RO$", RegexOptions.IgnoreCase))
                        log.ProgramPath = line;
                    var serial = Regex.Match(line, @"^SERIAL\s+N[O0]\.?:\s*(?<value>.*)$", RegexOptions.IgnoreCase);
                    if (serial.Success)
                        log.Serial = NonEmpty(serial.Groups["value"].Value);
                    var revision = Regex.Match(line, @"^REV[.:\s]+(?<value>\S+)$", RegexOptions.IgnoreCase);
                    if (revision.Success)
                    {
                        log.Revision = revision.Groups["value"].Value;
                        if (previous != null && Regex.IsMatch(previous, @"^[A-Za-z0-9][A-Za-z0-9_.-]*\s+\S+"))
                            log.PartNumber = Regex.Split(previous, @"\s+")[0];
                    }
                    if (pair.Success && pair.Groups["key"].Value.Equals("Operator", StringComparison.OrdinalIgnoreCase))
                        log.Operator = NonEmpty(pair.Groups["value"].Value);
                    var procedure = Regex.Match(line, @"Test\s+Pro(?:c|s)edure\s*:\s*(?<value>.*)$", RegexOptions.IgnoreCase);
                    if (procedure.Success)
                        log.SequenceName = NonEmpty(procedure.Groups["value"].Value);
                }
                if (TrySetup(line, setups))
                    continue;
                if (line.StartsWith(":"))
                {
                    seenResult = true;
                    var row = Row.Match(line.Substring(1));
                    if (!row.Success)
                        throw new InvalidDataException($"Unrecognized NETS result at line {index + 1}.");
                    var fields = Regex.Split(row.Groups["rest"].Value, @"\s+");
                    string rowCode = NonEmpty(row.Groups["code"].Value);
                    bool hasStatus = new[] { "PASS", "FAIL", "LOW", "HIGH", "WIRED", "ISOLATED" }.Contains(fields[0]);
                    if (rowCode != null)
                    {
                        code = rowCode;
                        contextStep = row.Groups["step"].Value;
                        from = hasStatus ? null : row.Groups["step"].Value + " " + row.Groups["rest"].Value;
                    }
                    if (!hasStatus)
                    {
                        if (rowCode == null || !Regex.IsMatch(fields[0], @"^\S*[-\d]\S*$"))
                            throw new InvalidDataException($"Unknown NETS status/context at line {index + 1}.");
                        continue;
                    }
                    if (code == null || fields.Length < 3)
                        throw new InvalidDataException($"Incomplete NETS measurement at line {index + 1}.");
                    var numeric = Number.Match(fields[1]);
                    if (!numeric.Success)
                        throw new InvalidDataException($"Unsupported NETS numeric value at line {index + 1}.");
                    double value = double.Parse(numeric.Groups["value"].Value, CultureInfo.InvariantCulture) * Scales[numeric.Groups["scale"].Value];
                    if (double.IsInfinity(value) || double.IsNaN(value))
                        throw new InvalidDataException("NETS measurement is not finite.");
                    string mode = code.StartsWith("X", StringComparison.Ordinal) ? "C" : code.Substring(0, 1);
                    setups.TryGetValue(mode, out var setup);
                    group.Measurements.Add(new Measurement
                    {
                        Code = code,
                        StepNumber = NonEmpty(row.Groups["step"].Value) ?? contextStep,
                        Pin = fields.Length > 3 ? fields[3] : "",
                        Value = value,
                        Unit = fields[2],
                        Source = line,
                        From = from,
                        Setup = setup,
                        Status = fields[0] == "PASS" ? StepStatusType.Passed : fields[0] == "WIRED" || fields[0] == "ISOLATED" ? StepStatusType.Done : StepStatusType.Failed
                    });
                    continue;
                }
                if (inTrailer && pair.Success && Regex.IsMatch(pair.Groups["key"].Value, @"^(NET errors|Bulk errors|Two point errors|Total Test Count)$", RegexOptions.IgnoreCase))
                {
                    string value = pair.Groups["value"].Value;
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        throw new InvalidDataException($"Invalid NETS summary counter at line {index + 1}.");
                    log.Summary[pair.Groups["key"].Value] = value;
                }
                if (line.IndexOf("TEST FAILED", StringComparison.OrdinalIgnoreCase) >= 0)
                    log.FailedBanner = true;
                if (line.IndexOf("TEST PASSED", StringComparison.OrdinalIgnoreCase) >= 0)
                    log.PassedBanner = true;
                if (!inTrailer && Regex.IsMatch(line, @"^[A-Za-z].+\s[A-Z]\d{3}\s*$"))
                    log.Diagnostics.Add(line);
                previous = line;
            }
            if (!log.Start.HasValue)
                throw new InvalidDataException("NETS run has no valid start timestamp.");
            if (inInfo)
                throw new InvalidDataException("Unclosed NETS TEST_INFO block.");
            bool completeSummary = new[] { "NET errors", "Bulk errors", "Two point errors", "Total Test Count" }.All(log.Summary.ContainsKey);
            if (!log.PassedBanner && !log.FailedBanner && !completeSummary)
                throw new InvalidDataException("Incomplete NETS run: no final outcome or complete summary.");
            return log;
        }

        private static bool TrySetup(string line, Dictionary<string, string> setups)
        {
            var setup = Regex.Match(line, @"^(?<mode>[A-Z])(?:\s+|,)(?<parameters>\S.*)$");
            if (!setup.Success)
                return false;
            setups[setup.Groups["mode"].Value] = line;
            return true;
        }

        private static IReadOnlyList<string> ReadLines(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
            return reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }

        private sealed class ParsedLog
        {
            public string Version, ProgramPath, LogPath, Serial, PartNumber, Revision, Operator, SequenceName;
            public DateTime? Start, End;
            public bool Simulation, FailedBanner, PassedBanner;
            public Dictionary<string, string> TestInfo { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Summary { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<ResultGroup> Groups { get; } = new List<ResultGroup>();
            public List<string> Diagnostics { get; } = new List<string>();
        }

        private sealed class ResultGroup
        {
            public string Name;
            public List<Measurement> Measurements { get; } = new List<Measurement>();
        }

        private sealed class Measurement
        {
            public string Code, StepNumber, Pin, Unit, Source, From, Setup;
            public double Value;
            public StepStatusType Status;
        }
    }
}
