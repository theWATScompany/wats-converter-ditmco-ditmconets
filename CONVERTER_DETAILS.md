# DIT-MCO NETS Converter Details

## Overview

The DIT-MCO NETS converter imports ASCII console logs (.LOG) into WATS through
the WATS Client. It supports observed NETS 1.0.6899, 1.5.7809 and 1.7.8440 layouts,
including legacy title banners without TEST_INFO and logs with optional metadata.
Each run becomes a separate UUT report, so a failed attempt remains visible when
a subsequent retest passes. This is a log reader, not a NETS script interpreter.

`IReportConverter_v2`, one-to-many: `ImportReport` splits at NETS version headers,
parses and validates all runs, then calls `api.Submit` for each report and returns
`null`. A submission failure throws; prior successful submissions are not rolled
back. No mutable parser state survives between files.

## File Format Assumptions and Requirements

A run needs a NETS version header, a valid start timestamp, UUT identity and a
final PASS/FAIL banner or complete four-counter summary. The entire TEST_INFO
block may be absent. Unknown result rows, invalid numeric values, unclosed
TEST_INFO blocks and incomplete runs cause the whole file to be rejected before
submission. Blank lines between appended runs are accepted.

### Console Grammar

| Input | Handling |
| --- | --- |
| `NETS(R) Version ...` | New report boundary, including appended retests. |
| `DD MMM YY HH:mm:ss` | Start and optional end time; English/Norwegian month tokens. |
| Standalone `.RO` path | Program identity. |
| `SEL ...` | Log output path, not the program. |
| `BTB/ETB TEST_INFO` | Optional key/value metadata; later duplicate key wins. |
| Other `BTB/ETB` blocks | Named sequence grouping; end resumes `MAIN`. |
| `CMP,<label>` | Sequence group; trailing directive comments excluded from name. |
| Single-letter setup, with or without `;` | Most recent parameter text for that mode. Tabs, spaces and commas accepted. |
| `: CODE point status value unit pin net` | Numeric reading. |
| `: CODE point pin net` | Source context for subsequent readings. |
| `: point status value unit pin net` | Continuation reading inheriting code and source context. |
| `: ISOLATED value unit pin net` | Diagnostic reading inheriting source point number. |
| `ERR,...` | Summary boundary only. |
| Error counters and final banner | Run verdict, combined conservatively with measurement failures. |

Examples use generic values:

```text
: CC 00001 J1-1 NET_A
:    00002 HIGH >30.00M OHM J1-2 NET_A
: XT 00002 J1-2 NET_A
:    ISOLATED >30.00M OHM J1-2 NET_A
```

Codes are preserved without requiring a closed list. The first letter selects
the parameter mode; `XS`/`XT` diagnostic context uses continuity parameters.

### Measurements and Outcomes

Each printed reading becomes one NumericLimitStep, grouped by CMP or named BTB
block. Readings are not combined into multi-measurement steps. Source rows,
inherited source-pin context and active per-mode setup text are retained with
the reading. Header metadata, program/log paths and summary counters are kept
as additional UUT information.

| NETS result | WATS status |
| --- | --- |
| PASS | Passed |
| FAIL, LOW, HIGH | Failed |
| WIRED, ISOLATED | Done (diagnostic, not a pass/fail judgement) |

Any failed reading fails its containing sequence and the run. A failed banner
or positive error counter also fails the run, even if another banner says PASS.
ERR,0 is only a summary marker and does not mean that the run passed. An explicit
run-outcome step preserves failures that are recorded only in summary counters.

Engineering prefixes are scaled case-sensitively (G, M, K/k, m, u, n, p).
For example, 750.0m OHM becomes 0.75 OHM. Units remain as printed, including OHM
and uf. A reading such as >1.200G stores the numeric boundary 1200000000; its
original qualifier is preserved in text. No numeric limits are inferred from
ambiguous setup columns: NETS supplies the verdict.

### Synthetic Examples

The three example files in Data/ are entirely fabricated, not anonymized
production logs. All identifiers, paths, pin/net names and measurements are
demonstration data. Each run is explicitly marked as a simulation. These files
illustrate the log format; they are not executable test programs or electrical
test specifications.

| Example file | Reports | Expected outcomes | Printed readings |
| --- | --- | --- | --- |
| legacy-pass.LOG | 1 | Passed | 3: insulation, continuity and capacitance |
| metadata-fail.LOG | 1 | Failed | 5: PASS, LOW, HIGH, WIRED and ISOLATED |
| appended-retest.LOG | 2 | Failed, then Passed | 4: two per attempt, same synthetic UUT |

legacy-pass.LOG requires no additional metadata. metadata-fail.LOG demonstrates
optional TEST_INFO keys and two diagnostic readings that are not included in
Total Test Count. appended-retest.LOG demonstrates independent run boundaries
and preservation of the first failure. Offline tests check report identities,
outcomes, reading counts, scaling and diagnostic statuses for these examples.

## Converter Parameter Details

All nine parameters are optional. Leave requiredFields empty for standard NETS
logs. Nonempty additional metadata is retained even if no mapping is configured.

- partNumber: fallback before the legacy product banner.
- partNumberField: optional TEST_INFO key to prefer for the part number.
- serialNumberField: optional TEST_INFO key to prefer for the UUT serial number.
- partRevision: fallback before the legacy REV. banner.
- operationTypeCode: WATS operation type code, default 10; select a code valid on your server.
- stationName: station override, otherwise station metadata or the computer name.
- sequenceName: fallback for a blank or missing Test Procedure/Prosedure.
- sequenceVersion: fallback before the legacy revision.
- requiredFields: comma/semicolon-separated TEST_INFO keys to require explicitly.

All defaults except operationTypeCode are empty strings. Do not configure
requiredFields for fields that are optional in your reports.

## Hardcoded Values and Defaults

### UUT Field Mapping

| WATS field | Source precedence |
| --- | --- |
| Serial | Configured key; SERIAL; UUT Serial No/UUT Serial Number; other UUT-prefixed serial metadata; SERIAL NO/SERIAL N0 banner. |
| Part number | Configured key; UUT Part Number/UUT Part No/Part Number; configured value; first token of product line preceding REV.; NETS-UUT. |
| Revision | Revision of UUT/UUT Revision; configured value; REV. banner; A. |
| Operator | Test Operator ID/Operator/Test Operator; legacy Operator banner; Unknown. |
| Station | Configured stationName; Test Station Serial No/Test Station Part No; computer name. |
| Sequence | Test Procedure/Prosedure; configured value; .RO filename; NETS. |
| Sequence version | Test SW Revision; configured value; legacy revision; NA. |
| Start | First timestamp in the run; English months and MAI, OKT, DES accepted. |
| Duration | Difference between start and final emitted timestamp, otherwise zero. |

Station and fixture serials are never used as UUT identity. Production logs
without a UUT serial are rejected. Explicit simulation logs may fall back to
SIM-yyyyMMddHHmmss when no UUT serial is present. The examples supply synthetic
serials explicitly. Unknown metadata keys are retained, not required.

## Known Limitations and Edge Cases

- Limits/stimulus meanings are not guessed from positional setup fields. Original
  setup text remains attached for later validated limit extraction.
- Printed readings include diagnostic scans. Their count need not equal the
  instrument's `Total Test Count`; both are preserved rather than forced equal.
- A source qualifier (`>`, `<`) describes a bound. Do not interpret that numeric
  boundary as an exact resistance in downstream analysis.
- NETS timestamps have no timezone. Conversion uses local timestamps and does
  not invent duration when no end timestamp was emitted.
- Legacy product identity is inferred only from a product line followed by a
  `REV.` banner. Use configured metadata keys for other title layouts.
- The converter does not execute NETS directives or reconstruct unprinted tests.
  The vendor documents configurable reports, including failures-only output.
- File replay is not deduplicated. Importing a growing log repeatedly resubmits
  earlier runs. Only present finalized files to the WATS Client watch folder.
- All runs are parsed and validated first, but submission is not transactional.
  A later submission failure cannot roll back reports already accepted by WATS.
- Supported versions describe verified layouts, not every report customization
  or every NETS version. Validate a representative file before production use.

## Configuration Guidance

### Installation

1. Obtain DITMCONETSConverter.dll built for your WATS Client runtime: net48 or
  net8.0-windows. Contact WATS support for release availability.
2. Add the converter in WATS Client Configurator following
  [Setting up a custom converter](https://support.wats.com/hc/en-us/articles/13344321749788-Setting-up-a-custom-converter).
3. Select Virinco.WATS.Converter.DITMCO.DITMCONETSConverter and configure a watch
  folder for finalized .LOG files.
4. Check operationTypeCode and stationName for your environment. Leave custom
  metadata mappings and requiredFields empty unless your reports need them.
5. Validate the field mapping and outcomes on a test system before enabling
  production ingestion. Do not put demonstration files in a production watch folder.

For custom integration code, ImportReport performs submission internally and
returns null. Do not submit a returned report again. The three examples are
checked offline with simulated submissions; no server upload is needed to run
their conversion tests.

### Vendor References

DIT-MCO publishes relevant manual excerpts freely:

- [Test Results](https://www.ditmco.com/knowledgebase/test-results-2), including
  legacy console layout and print-all versus failures-only options.
- [Parameter Statements](https://www.ditmco.com/knowledgebase/parameter-statements),
  including space/comma separators.
- [Test Info](https://www.ditmco.com/knowledgebase/test-info), describing configurable
  report metadata. These options are not a guarantee of a fixed console banner.
