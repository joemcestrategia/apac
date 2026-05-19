# Apac — macOS Desktop App

A native macOS app for processing APAC timezone workforce data from Excel (.xlsx) files.

## Features

- **Import Excel** — drag & drop or open .xlsx files; headers are detected automatically from the first row.
- **Data Grid** — sortable, filterable table of every imported record (employee ID, name, country, timezone, department, job title, employment type, city, manager).
- **Charts** — headcount by country (colored bar chart), headcount by department (orange bar chart), employment type distribution (donut/ring chart).
- **Metrics** — summary cards showing total records, unique countries, and departments.
- **SQLite Storage** — all data persisted automatically to `~/Library/Application Support/Apac/apac_data.sqlite`.
- **Export CSV** — export either the full data grid or the aggregated country metrics to a CSV file.
- **Filters** — filter by country, department, timezone, employment type, or free-text search (name, title, city).

## Requirements

- macOS 14 (Sonoma) or later
- Xcode 15.0 or later
- Swift 5.9+

## Quick Start

```bash
# Clone or open this directory
cd Apac

# Open in Xcode (SwiftPM project)
open Package.swift

# Select the "Apac" scheme and target "My Mac"
# Press Cmd+R to build and run
```

Or build from the command line:

```bash
swift build
swift run
```

## Project Structure

```
Sources/Apac/
├── ApacApp.swift              # @main entry point, menu commands
├── Models/
│   ├── EmployeeRecord.swift   # GRDB-persisted row model
│   └── CountryMetrics.swift   # Aggregated metrics & filter/sort types
├── Services/
│   ├── ExcelParserService.swift   # CoreXLSX-based .xlsx reader
│   ├── DatabaseService.swift     # SQLite CRUD via GRDB
│   ├── MetricsCalculator.swift   # Headcount/aggregation logic
│   └── CSVExporter.swift         # CSV file writer
├── ViewModels/
│   └── ApacViewModel.swift       # @MainActor MVVM coordinator
└── Views/
    ├── ContentView.swift         # Navigation split view with sidebar
    ├── DataGridView.swift        # SwiftUI Table + summary cards
    └── ChartView.swift           # Swift Charts bar + donut charts

Tests/ApacTests/
└── ApacTests.swift              # Unit tests for models & services
```

## Expected Excel Format

The first row must contain column headers. Expected columns (order does not matter, but they are read positionally):

| Column | Description |
|--------|-------------|
| Employee ID | Unique identifier |
| Name | Full name |
| Country | Country name (e.g., Singapore, Japan, Australia) |
| Timezone | IANA timezone (e.g., Asia/Singapore, Asia/Tokyo) |
| Department | Department name |
| Job Title | Role / title |
| Employment Type | e.g., Full-time, Contractor |
| City | Office city |
| Manager Name | Direct manager |

## Dependencies

| Library | Purpose |
|---------|---------|
| [CoreXLSX](https://github.com/CoreOffice/CoreXLSX) | Parsing .xlsx (Excel) files |
| [GRDB.swift](https://github.com/groue/GRDB.swift) | SQLite database with Swift-native ORM |
| Swift Charts (Apple) | Bar & donut chart rendering |
