# Frontend cleanup — validation record

All tests ran against local code or isolated synthetic fixtures. No production database or website was changed.

| Gate | Outcome | Evidence |
|---|---|---|
| Git diff whitespace | PASS | git diff --check |
| Release build | PASS | .NET 10; 0 warnings, 0 errors |
| Local Release publish | PASS | 41 published CSS files; no obsolete source file retained |
| UI DOM regression | PASS | 934 checks |
| Full SQL-backed integration | PASS | 277 release checks + 128 independent findings; exit code 0 |
| Synthetic browser GET smoke | PASS | 39/39 HTTP 200 |
| Additional public site CSS smoke | PASS | 7/7 HTTP 200; no broken CSS requests |
| Responsive horizontal overflow | PASS | None in 39 cases (1440px / 768px / 390px) |
| Sampled computed style/geometry | PASS | 39/39 values matched before/after |
| Screenshot byte equality | PARTIAL | 30/39 identical PNGs; 9 showed changing synthetic timestamps |
| Cross-role JavaScript handler contracts | PASS (automated coverage) | 934 DOM checks; transactional paths covered by isolated integration |
| Manual authenticated production workflows | NOT TESTED | Deliberately excluded because no production deploy was authorized |
| Real-user load timing | NOT TESTED | Only CSS request count / browser transfer-size sampling captured |

## CSS requests observed in isolated browser

| Role | Before | After | Difference |
|---|---:|---:|---:|
| Staff | 32 | 27 | -5 |
| Owner | 28 | 25 | -3 |
| Client | 30 | 29 | -1 |

Browser-reported CSS transfer sizes on representative 1440px pages:

| Role | Before (bytes) | After (bytes) |
|---|---:|---:|
| Staff dashboard | 833,539 | 832,348 |
| Owner dashboard | 716,580 | 716,764 |
| Client bookings | 1,124,167 | 1,121,436 |

These local browser samples are not a benchmark of real-world load speed; note that Owner transfer size slightly increased.

## Visual comparison

The nine nonidentical screenshots were limited to Staff and Owner notifications and Staff profile screens. On visual inspection, the differences were changing synthetic relative timestamps or login times. No geometry or sampled computed-style changes were detected in any of the 39 cases.

Source evidence in the authorized computer's recovery folder:

- AriesRecovery/frontend-cleanup-20261117/visual-before/
- AriesRecovery/frontend-cleanup-20261117/visual-after/
- AriesRecovery/frontend-cleanup-20261117/visual-comparison.json
- AriesRecovery/frontend-cleanup-20261117/integration-full-attempt2.log
- AriesRecovery/frontend-cleanup-20261117/publish-candidate/

The full integration log includes expected internal error-level diagnostic messages from deliberately rejected requests and simulated failures. The runner exited with code 0 and reported 277 plus 128 successful checks.

## Remaining validation limits

Browser snapshots exercised 13 representative routes across three viewports, not every Razor page, state transition or modal. No production workflow testing was performed. The stylesheet concatenation was intentionally lossless to reduce visual risk, rather than deleting unproven unused selectors.
