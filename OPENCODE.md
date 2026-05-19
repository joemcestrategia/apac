# OPENCODE.md — Apac Project

## Project Overview

Apac is an early-stage polyglot application for APAC-region employee/headcount management, comprising three tiers:

| Layer | Stack | Status |
|-------|-------|--------|
| Web frontend | Next.js 15 + React 19 + Tailwind CSS 4 + TypeScript | Configured, no source yet |
| Backend / agent | Python with a custom tool/function router (`router.py`) | Active (1 module) |
| Native app | Swift + GRDB (SQLite), MVVM architecture | Active (2 model files) |

## Directory Layout

```
C:\src\Apac\
├── package.json            # Node.js / Next.js project config
├── pyproject.toml          # Python project config (setuptools, pytest, pytest-bdd)
├── router.py               # Python tool/function router (LLM agent use)
├── Sources/Apac/
│   ├── Models/             # Swift data models (GRDB-backed)
│   │   ├── CountryMetrics.swift
│   │   └── EmployeeRecord.swift
│   ├── Services/           # (empty)
│   ├── ViewModels/         # (empty)
│   └── Views/              # (empty)
├── Tests/
│   ├── ApacTests/          # (empty, for Swift/XCTest)
│   └── features/           # (empty, for Gherkin .feature files)
├── Resources/              # (empty, for static assets)
└── venv/                   # Python 3.14 virtual environment
```

## Build / Test / Lint Commands

### Node.js (Next.js)
```bash
npm install                # Install dependencies
npm run dev                # Start Next.js dev server
npm run build              # Production build
npm run start              # Start production server
npx tsc --noEmit           # Type-check (no tsconfig.json yet — add one first)
```

### Python
```bash
.venv\Scripts\activate     # Activate virtualenv (Windows)
pytest                     # Run Python tests (supports BDD via pytest-bdd)
python router.py           # Run the router module
```

### Swift
No `Package.swift` yet. Build/test commands are not yet configured.

## Key Dependencies

### Node.js (`package.json`)
- next `^15.1.0`, react `^19.0.0`, react-dom `^19.0.0`
- tailwindcss `^4.0.0`, @tailwindcss/postcss `^4.0.0`
- typescript `^5.7.0`

### Python (`pyproject.toml`)
- setuptools `>=61.0`
- pytest, pytest-bdd

### Swift (inferred from imports)
- GRDB (SQLite library — `FetchableRecord`, `MutablePersistableRecord`)

## Architecture Notes

- **Swift layer** follows MVVM: `Models/` holds GRDB-persisted data, `Services/` for business logic, `ViewModels/` for state, `Views/` for UI. The `EmployeeRecord` model maps to a `employee_records` DB table with columns for employee ID, name, country, timezone, department, job title, employment type, city, manager, and import batch tracking.
- **Python layer** (`router.py`) implements a generic tool/function router with: `FunctionCall` dataclass (serializes/deserializes JSON), `Tool` dataclass (wraps a function with name/description/parameters/schema), and `Router` class (registers tools, emits OpenAI-compatible tool schemas, dispatches function calls).
- **No root `.gitignore` exists.** `venv/` has its own `.gitignore` but the rest of the project does not.
- **No `tsconfig.json` exists yet** — TypeScript is listed as a dependency but not configured.

## Testing

- **Python**: pytest with pytest-bdd (Gherkin `.feature` files go in `Tests/features/`).
- **Swift**: XCTest (test files go in `Tests/ApacTests/`).
- **Node.js**: No test framework configured yet.
