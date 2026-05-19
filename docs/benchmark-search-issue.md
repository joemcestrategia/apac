# Benchmark: Employee Search Performance

> **Type:** Performance / Benchmark  
> **Priority:** Medium  
> **Labels:** `perf`, `benchmark`, `search`  
> **Component:** `DatabaseService.fetchRecords(filter:sort:)` (Swift/GRDB/SQLite)

---

## Summary

The Apac macOS app (`Sources/Apac/`) performs employee-record search via raw SQL `LIKE '%text%'` queries against an on-disk SQLite database (`apac_data.sqlite`) managed by GRDB. Every keystroke in the search text field triggers a full query with no debounce, no in-memory cache, and no FTS index. The searchable columns (`name`, `jobTitle`, `city`) have no indexes. This issue defines a benchmark suite to establish baseline performance numbers, identify bottlenecks, and guide future optimisation decisions.

---

## Current Architecture (for context)

| Aspect | Current state |
|---|---|
| **Database** | SQLite on-disk, single `employee_records` table, opened via `GRDB.DatabaseQueue` (serialised reads/writes) |
| **Search algorithm** | `WHERE (name LIKE '%X%' OR jobTitle LIKE '%X%' OR city LIKE '%X%')` — leading-wildcard substring matching |
| **Filter algorithm** | `WHERE (country = ?) AND (department = ?) AND (employmentType = ?) AND (timezone = ?)` — chained exact-match |
| **Sort** | `ORDER BY <single_column> ASC/DESC` |
| **Indexes** | 4 B-tree indexes on filter columns only: `country`, `department`, `employmentType`, `timezone` |
| **Searchable columns** | `name`, `jobTitle`, `city` — **no indexes** |
| **Caching** | None. Every keystroke → full DB query |
| **Debounce** | None. `TextField` binding fires on every character change |
| **Result ranking** | None. All matches returned with equal weight; sorted only by user-selected sort column |
| **Result limiting** | None. All matching rows returned (no `LIMIT`, no pagination) |
| **Query entry point** | `DatabaseService.fetchRecords()` → `ApacViewModel.applyFilters()` → SwiftUI `TextField` binding |

---

## Test Datasets

Benchmarks must run against four curated datasets that isolate different performance regimes:

### D1: Tiny (100 records)
- 2 countries, 3 departments, 2 employment types, 2 timezones
- Purpose: establish zero-load baseline / warm-cache overhead

### D2: Medium (10,000 records)
- 25 countries, 40 departments, 4 employment types, 8 timezones
- Realistic text: real employee names, job titles, cities (generate from a CSV seed or faker)
- Purpose: representative production workload

### D3: Large (100,000 records)
- 50 countries, 80 departments, same enumerated types
- Purpose: stress test full-table-scan on LIKE queries; measure degradation curve

### D4: Extra-large (500,000 records)
- 100 countries, 200 departments
- Purpose: identify ceiling before UX becomes unusable (target: identify where query latency exceeds 500 ms)

### Dataset generation requirements
- Each dataset must be **deterministically reproducible** (same seed → same rows).
- Text columns must contain realistic-length strings (name: 10–40 chars, jobTitle: 20–60 chars, city: 5–20 chars).
- Search terms must appear in varying positions (prefix, mid-string, suffix) and at varying frequencies.
- Datasets must be provided as a Swift CLI tool or shell script that writes directly to SQLite.

---

## Benchmark Scenarios

Each scenario must be measured in **isolation** (clean DB, single operation) and with **repeated trials** (10 iterations, drop best/worst, average remaining 8). All scenarios run against every dataset size.

### Search-only scenarios
| # | Scenario | Parameters | What it exercises |
|---|---|---|---|
| S1 | **Cold-start single char** | `searchText = "a"`, no filters | Worst case: full table scan, maximum match density |
| S2 | **Cold-start multi-char** | `searchText = "eng"`, no filters | Typical user query (3 chars = enough to narrow) |
| S3 | **Cold-start long query** | `searchText = "software engineer"`, no filters | Multi-word, low match density |
| S4 | **Cold-start no-match** | `searchText = "xyzwq9"`, no filters | Zero-result worst case (must scan entire table) |
| S5 | **Keystroke sequence** | Simulate typing "mar" → "mark" → "market" → "marketing" | Measures progressive narrowing; real UX path |
| S6 | **Post-filter search** | All 4 filter dropdowns set → `searchText = "dev"` | Search over already-filtered subset |

### Filter-only scenarios
| # | Scenario | Parameters | What it exercises |
|---|---|---|---|
| F1 | **Single filter (indexed)** | `country = "Australia"` | B-tree index lookup |
| F2 | **All four filters** | country + dept + empType + timezone set | Compound filter, index merge |
| F3 | **No-match filter combo** | valid country + non-existent dept | Early pruning path |

### Sort scenarios
| # | Scenario | Parameters | What it exercises |
|---|---|---|---|
| R1 | **Sort on indexed column** | `sort = .country` (ascending) | Leverages existing country index |
| R2 | **Sort on unindexed column** | `sort = .managerName` (ascending) | FileSort / temp B-tree overhead |
| R3 | **Sort ascending vs descending** | Compare both directions on same column | Symmetry check |

### Combined scenarios
| # | Scenario | Parameters |
|---|---|---|
| C1 | Search + Sort | `searchText = "dev"` sorted by `jobTitle` DESC |
| C2 | Filter + Search + Sort | 2 filters set + `searchText = "london"` sorted by `name` ASC |

### Concurrency scenarios
| # | Scenario | Parameters |
|---|---|---|
| T1 | **Rapid-fire (debounce simulation)** | Submit S2 10× in 200 ms intervals; measure queue depth and tail latency |

---

## Metrics to Collect

Every scenario must report the following per trial:

### Primary (time)
| Metric | Unit | Instrumentation |
|---|---|---|
| `query_wall_ms` | milliseconds (2 d.p.) | `CFAbsoluteTimeGetCurrent()` around `fetchRecords` call |
| `query_cpu_ms` | milliseconds (2 d.p.) | `clock_gettime(CLOCK_THREAD_CPUTIME_ID)` or `task_info` Mach API |
| `p50 / p95 / p99 / max` | milliseconds | Computed across 10 trials after dropping best/worst |
| `first_vs_nth_ratio` | scalar | First trial wall time divided by average of trials 5–10 (cold vs warm cache ratio) |

### Secondary (SQLite)
| Metric | Unit | Source |
|---|---|---|
| `sqlite_rows_scanned` | count | `sqlite3_stmt_status(SQLITE_STMTSTATUS_FULLSCAN_STEP, …)` or `EXPLAIN QUERY PLAN` row estimate |
| `sqlite_index_used` | boolean | Did the query planner use an index? (from `EXPLAIN QUERY PLAN`) |
| `temp_b_tree_size` | bytes | Check `sqlite3_temp_directory` or `SQLITE_DBSTATUS_SCHEMA_USED` if sort spills to disk |

### Tertiary (system)
| Metric | Unit | Instrumentation |
|---|---|---|
| `peak_memory_delta_mb` | MiB | `task_info` resident memory delta (before/after) |
| `db_file_size_mb` | MiB | `FileManager` attributes of `apac_data.sqlite` after dataset load |

### Metadata
| Metric | Unit | Description |
|---|---|---|
| `match_count` | integer | Number of rows returned |
| `total_rows` | integer | Total rows in `employee_records` |
| `db_page_size` | bytes | `PRAGMA page_size` |
| `db_cache_size` | pages | `PRAGMA cache_size` |
| `wal_mode` | boolean | Whether WAL journal mode is active |

---

## Methodology

### Instrumentation harness
Create a new Swift Package Manager executable target: `Sources/ApacBench/`. It must:

1. Accept CLI arguments for dataset size and scenario selection:
   ```
   ApacBench --dataset medium --scenario S2 --trials 10 --output report.json
   ```

2. Generate or load the specified dataset into a fresh SQLite DB before each scenario run (or use the pre-built dataset files).

3. For each trial:
   a. Flush page cache (macOS: `sudo purge` or open a temp DB copy to remove OS-level FS cache). Skip flushing when explicitly measuring warm cache.
   b. Start timers.
   c. Execute `fetchRecords()` or an equivalent extracted method.
   d. Stop timers.
   e. Collect SQLite and system metrics.
   f. Discard result objects to avoid ARC retain/release noise.

4. Compute aggregate statistics (p50, p95, p99, mean, stddev, min, max).

5. Output a JSON report conforming to a schema (see Deliverables).

### Execution environment

| Constraint | Value |
|---|---|
| **OS** | macOS 14+ (Sonoma or later) |
| **Hardware** | Report `sysctl hw.model`, `hw.memsize`, `hw.ncpu` in output |
| **Disk** | Apple internal SSD (report model / IOPS in report metadata) |
| **Swift version** | Report `swift --version` |
| **GRDB version** | Report version from `Package.resolved` |
| **Other processes** | Close all other apps; `sudo purge` before each cold-start trial |

### Reproducibility

- All dataset generation seeds must be checked into the repo.
- Benchmark script must be a single-command invocation: `make bench` or `swift run ApacBench --all`.
- Output report must be committed as `bench/reports/YYYY-MM-DD/baseline.json`.

---

## Deliverables

### Code
1. **`Sources/ApacBench/`** — standalone Swift executable with the benchmark harness.
2. **`bench/datasets/`** — dataset generator, plus committed `.sqlite` files (Git LFS or compressed) for D1–D4.
3. **`Package.swift`** — updated with `ApacBench` product/target.

### Documentation
4. **`bench/README.md`** — how to run benchmarks, regenerate datasets, interpret results.
5. **`bench/reports/baseline.json`** — JSON report for the initial baseline run.

### JSON report schema
```json
{
  "metadata": {
    "hardware": { "model": "MacBookPro18,1", "memory_gb": 32, "cpu_cores": 10 },
    "swift_version": "5.9",
    "grdb_version": "6.2.0",
    "sqlite_version": "3.43.0",
    "dataset": "medium",
    "total_rows": 10000,
    "db_file_size_mb": 5.2,
    "date": "2026-05-18T00:00:00Z"
  },
  "results": [
    {
      "scenario": "S2",
      "description": "Multi-char search, no filters",
      "params": { "searchText": "eng", "filters": 0, "sort": "name_asc" },
      "trials": 10,
      "dropped": ["best", "worst"],
      "match_count": 247,
      "latency": { "min": 2.3, "max": 8.9, "mean": 3.4, "p50": 3.2, "p95": 5.1, "p99": 8.1 },
      "cold_warm_ratio": 1.8,
      "sqlite": { "index_used": false, "rows_scanned_est": 10000, "temp_b_tree_bytes": 0 },
      "memory_delta_mb": 0.4
    }
  ]
}
```

---

## Acceptance Criteria

- [ ] All 13 scenarios (S1–S6, F1–F3, R1–R3, C1–C2, T1) run to completion against all 4 dataset sizes without crashing.
- [ ] JSON report is machine-readable and validates against the schema above.
- [ ] No scenario on D2 (Medium / 10k) exceeds 100 ms p50.
- [ ] Cold/warm ratio is measured and < 5× (if > 5×, document and file a follow-up issue).
- [ ] `EXPLAIN QUERY PLAN` output is captured for every unique query shape to document index usage.
- [ ] Benchmark is runnable via a single CLI command with no manual setup beyond `swift build`.
- [ ] Results are summarised in a GitHub-flavoured markdown table in `bench/reports/README.md`.
- [ ] An identified "performance ceiling" row count is documented (the dataset size at which p95 exceeds 500 ms for scenario S2).

---

## Open Questions (to resolve before implementation)

1. Should we benchmark the full `ApacViewModel.applyFilters()` (includes `metricsCalc.compute()`) or measure `DatabaseService.fetchRecords()` in isolation? The former includes the metrics overhead which confuses the SQL query measurement — recommend measuring **both** and reporting separately.

2. Should the benchmark use WAL mode or rollback-journal? The current `DatabaseService` does not explicitly configure journal mode — verify current mode and document.

3. Should we add an FTS5 index as a comparison point in the same benchmark, or defer that to a follow-up issue?

4. What is the minimum acceptable p95 for user-facing search on D2? Suggest 200 ms as the initial threshold based on usability research (Nielsen, 1993: 100 ms feels instant, 1 s is the flow-interruption threshold).

---

## Related Issues / Future Work

- **FTS5 full-text search** — Replace `LIKE '%x%'` with SQLite FTS5 for ranked, tokenized search (dramatic latency improvement expected for D3/D4).
- **Debounce** — Add 150–250 ms debounce on `TextField` binding.
- **In-memory result cache** — Cache the last N query results to avoid duplicate DB hits on toggling sort direction.
- **Pagination / virtual scrolling** — Add `LIMIT`/`OFFSET` or keyset pagination; the SwiftUI `Table` loads all records into view.
- **Async / non-blocking queries** — Move `fetchRecords` off `@MainActor` to avoid UI jank.
