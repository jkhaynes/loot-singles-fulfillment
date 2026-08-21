# Test Fixtures

This folder contains packing-slip PDF fixtures used by the test suite.

## Sanitization Requirement

Every fixture **MUST be sanitized before being committed**. Do not include real customer names, addresses, phone numbers, email addresses, or any other personally identifiable information (PII), even though the fixtures represent real packing-slip *structure*.

This requirement applies to all test data, not just production code paths. Data minimization (FR-009/FR-019) principles extend to fixtures and test environments.
