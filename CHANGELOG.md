# Changelog

All notable changes to the DIT-MCO NETS Converter are documented in this file.

## [0.2.2] - 2026-11-24

### Changed

- Readings measured from a context row against a reference point (all modes except
  F, C and T) are now named after the net under test, followed by the reference:
  `HH 00011 J5-6 NET_B - J5-1 GND` instead of `HH 00002 J5-1`. Previously every
  reading against a common reference (typically GND) got the same step name, so
  failure analysis only showed the reference point as failing.
- Step names longer than 100 characters are shortened to the WATS limit.

**Note:** affected steps get new names, so step statistics recorded under the old
names do not continue under the new ones. Existing reports are unchanged.

## [0.2.1] - 2026-11-11

### Fixed

- Logs whose `.RO` program path or `SEL` log path exceeds 100 characters were
  rejected with "Error setting property DataString". MiscUUTInfo values are now
  shortened to the WATS limit; paths keep their end (the file name).
- Logs without a `TEST_INFO` block that print `SERIAL:`, `Test Operator ID:`,
  `Test SW Revision:` or `UUT ...:` lines in the header were rejected with
  "NETS log has no UUT serial number". These header lines are now read as metadata.

### Added

- Part-number fallback from a `<number> <description>` title line when no part
  number field, configured value or `REV.` banner is present.
- `****SIMULATION TEST RESULTS****` also marks a run as a simulation.

## [0.2.0] - 2026-09-25

### Publication Preparation

- Local Zendesk info page and documentation article with official vendor branding.
- Three wholly synthetic simulation logs: legacy-pass.LOG, metadata-fail.LOG and
  appended-retest.LOG. No real customer or product identifiers are used.
- Example-specific assertions cover four reports and twelve printed readings.
- Article drafts document optional metadata, DLL setup, result mapping and replay
  limitations. No article, binary or release has been published by this preparation.

### Changed

- One report per NETS run, including appended retests. ImportReport now submits
  internally and returns null; mappingType is one-to-many.
- Support legacy product/revision/serial/operator banners without TEST_INFO,
  Norwegian month names, named blocks and blank procedure names.
- Preserve engineering prefixes, censored readings, LOW/HIGH failures, WIRED
  and ISOLATED diagnostics, source-pin context and active parameter text.
- Derive failure from measurements, banners and error counters; ERR,0 alone is
  not a verdict. Reject malformed or incomplete runs before submission.
- Keep custom metadata optional. Add serialNumberField and remove the incorrect
  station-part-number default for partNumberField.
- Replace the public customer-derived fixture with synthetic data.

### Verification

- 100 local logs, 157 runs, 91,441 readings and 27 failed runs verified offline.
- Synthetic coverage includes malformed input, missing optional metadata,
  configurable identity, retests, diagnostic retention and failure aggregation.
- Raw samples stay external; no server upload performed.

## [0.1.0] - 2026-09-14

### Added

- Initial scaffold and M1 parser.
- Directive-driven state machine: NETS header, `BTB/ETB TEST_INFO`, `CMP` groups,
  parameter setup rows, `:` result rows, `; TIM` / `; ERR` trailer, PASS/FAIL banner.
- `BTB TEST_INFO` metadata mapped to standard WATS UUT fields with generic fallbacks.
  All unknown keys preserved as `MiscUUTInfo`.
- One `SequenceCall` per `; CMP` block, one `NumericLimitStep` per parameter setup —
  a step passes only if every measurement in it passes.
- xUnit test project with the three standard modes: `ConvertOnly`,
  `ConvertAndValidate`, `ConvertAndUpload`.

### Known limitations

- Numeric limits are log-only (`CompOperatorType.LOG`); actual limits will be
  extracted from setup columns once a production sample is available.
- `WIRED` diagnosis lines are captured but not yet attached to their parent
  measurement.
