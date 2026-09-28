# DIT-MCO NETS Converter

WATS Client converter for DIT-MCO NETS ASCII console logs (`.LOG`).

## Supported Input

Supports legacy console logs from NETS 1.0.6899, 1.5.7809 and 1.7.8440,
including optional `BTB TEST_INFO` metadata and named test blocks.
Verified offline against 100 supplied logs containing 157 runs and 91,441
printed numeric/diagnostic records. Customer files are not included here.

Each NETS version header begins a separate UUT report. Appended retests remain
separate, preserving failed attempts before a later pass. The converter submits
each report internally through the WATS Client API and returns `null`.
Custom integrations must not submit a returned report a second time.

## Installation

1. Build `DITMCONETSConverter.csproj` for the framework used by your WATS Client
   (`net48` or `net8.0-windows`).
2. Add the resulting `DITMCONETSConverter.dll` in WATS Client Configurator.
3. Configure the watch folder and parameters below.
4. Import finalized files only. Reimporting a growing/appended file resubmits all
   its runs; the converter does not maintain an ingestion checkpoint.

## Parameters

All configuration parameters are optional. Additional metadata is never required
unless named in `requiredFields`. The entire `TEST_INFO` block may be absent.

| Parameter | Default | Purpose |
| --- | --- | --- |
| `partNumber` | empty | Part-number fallback before the legacy banner. |
| `partNumberField` | empty | Custom `TEST_INFO` key for UUT part number. |
| `serialNumberField` | empty | Custom `TEST_INFO` key for UUT serial number. |
| `partRevision` | empty | Revision fallback before the legacy `REV.` banner. |
| `operationTypeCode` | `10` | WATS operation type code. |
| `stationName` | empty | Station override; otherwise station metadata or machine name. |
| `sequenceName` | empty | Fallback for blank/missing Test Procedure/Prosedure. |
| `sequenceVersion` | empty | Version fallback before the legacy revision. |
| `requiredFields` | empty | Comma/semicolon-separated metadata keys to require. |

## Field Mapping

| WATS field | Source precedence |
| --- | --- |
| Serial | Configured key; `SERIAL`; `UUT Serial No`/`UUT Serial Number`; other `UUT ... serial ...` metadata; `SERIAL NO`/`SERIAL N0` banner. |
| Part number | Configured key; `UUT Part Number`/`UUT Part No`/`Part Number`; configured value; first token of product line preceding `REV.`; `NETS-UUT`. |
| Revision | `Revision of UUT`/`UUT Revision`; configured value; `REV.` banner; `A`. |
| Operator | `Test Operator ID`/`Operator`/`Test Operator`; legacy Operator banner; `Unknown`. |
| Sequence | Test Procedure/Prosedure; configured value; `.RO` program filename; `NETS`. |
| Sequence version | `Test SW Revision`; configured value; legacy revision; `NA`. |
| Start | First timestamp in each run; English months and `MAI`, `OKT`, `DES` accepted. |
| Duration | Difference between start and final emitted timestamp; zero when absent. |

Station and fixture serial numbers are not used as UUT identity. A production run
without UUT serial is rejected. Explicit simulation logs may use a timestamp-based
`SIM-...` identifier. Nonempty metadata values are also retained as MiscUUTInfo.
The standalone `.RO` path is stored as `NETS Program`; `SEL` is stored as `NETS Log`.

## Measurements And Status

Each printed reading becomes one numeric step, grouped by `CMP` or named `BTB`
block. Continuation rows retain the preceding code and source-pin context.
Active per-mode parameters and original result text accompany each measurement.

- `PASS` is passed; `FAIL`, `LOW` and `HIGH` are failed.
- `WIRED` and numberless `ISOLATED` are diagnostic readings, retained as `Done`.
- Any failed measurement, failed banner or nonzero error counter fails the run.
- `ERR,0` is not treated as a passing verdict. A complete summary can provide the
  verdict when there is no PASS/FAIL banner.
- Numeric prefixes are case-sensitive: `M` is mega, `m` is milli; `G` and `K` are
  also normalized. Units such as `OHM` and `uf` are preserved.
- For `>1.200G`, the numeric field contains the boundary (1.2e9), not an exact
  measurement. The original qualifier remains in the step text.

Limits are not inferred from ambiguous parameter columns. NETS supplies the
measurement verdict; parameter text is retained for review. Unknown result rows,
bad numbers, missing timestamps and incomplete runs are rejected before any run
from that file is submitted. Submission itself is not transactional across runs.

## Testing

### Public Examples

These three independently fabricated simulation logs contain no real customer
or product identifiers. They are log-format examples, not executable test programs.

| File | Expected result | Readings |
| --- | --- | --- |
| [legacy-pass.LOG](Data/legacy-pass.LOG) | One passed report, no TEST_INFO required | 3 |
| [metadata-fail.LOG](Data/metadata-fail.LOG) | One failed report with optional metadata and diagnostics | 5 |
| [appended-retest.LOG](Data/appended-retest.LOG) | Failed attempt followed by a separate passed retest | 4 |

Do not import demonstration data into a production WATS server.

### Offline Checks

Run from this converter directory:

```powershell
dotnet test DITMCONETSConverterTests.csproj --filter "TestMode=ConvertOnly|TestMode=ConvertAndValidate"
$env:DITMCO_TEST_ARCHIVE = 'C:\PRIVATE\nets-samples.zip'
dotnet test DITMCONETSConverterTests.csproj --filter "TestMode=ExternalArchive"
Remove-Item Env:DITMCO_TEST_ARCHIVE
```

Offline tests use `SimulatedTDM`, including interception of internal submissions.
The external-archive test is skipped unless explicitly configured. It checks every
run's identity, date, status and every printed reading's value, unit and status.
`ConvertAndUpload` remains available only behind the shared server-safety gate;
it was not run for the supplied samples. Public `Data/` contains synthetic data only.

## References

- [Parser details](CONVERTER_DETAILS.md)
- [DIT-MCO NETS knowledge base](https://www.ditmco.com/knowledgebase/nets)
- [DIT-MCO report options](https://www.ditmco.com/knowledgebase/test-results-2)
- [Setting up a custom converter](https://support.wats.com/hc/en-us/articles/13344321749788-Setting-up-a-custom-converter)
