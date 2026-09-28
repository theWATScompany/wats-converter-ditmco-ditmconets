# Changelog

All notable changes to the DIT-MCO NETS Converter are documented in this file.

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
