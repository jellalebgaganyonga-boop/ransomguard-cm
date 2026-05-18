# ADR-016: Test Assertion Library Selection

## Status
Accepted

## Context
FluentAssertions 8.x changed to commercial license by Xceed Software in 2024.
RansomGuard-CM is a commercial product targeting Cameroonian hospitals.
Continuing with FluentAssertions would require per-developer licensing fees.

## Decision
Migrate to Shouldly (MIT licensed, free forever).

## Consequences
Positive:
- No licensing fees, ever
- MIT license aligned with project commercial goals
- Comparable readability and feature set
- Active maintenance and community

Negative:
- One-time refactoring effort (79 tests)
- Team must learn Shouldly syntax differences

## Alternatives Considered
- FluentAssertions 7.x (last MIT version): rejected, security and feature updates would stop
- Xunit.Assert built-in: rejected, less readable
- NUnit assertions: rejected, requires switching test framework
- Pay FluentAssertions license: rejected, recurring cost for indie student project commercializing in Africa
