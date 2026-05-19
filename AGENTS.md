# AGENTS.md — Apac

## Project Overview

Apac is a multi-project workspace containing:

1. **Frontend** (`src/`) — Next.js 15 (App Router) web app for EU compliance automation
2. **Python library** — LLM function-calling router (AutoGen-based)
3. **Amazon SP-API Models** (`Amazon/SPAPI/`) — vendored third-party API schemas (read-only)

The frontend uses a dark "cyberpunk/violet" aesthetic with Tailwind CSS v4 and Radix UI primitives.

---

## Setup & Commands

### First-time setup

```bash
npm install           # Install Node.js dependencies
```

The Python venv already exists at `venv/`. Activate it with:

```powershell
venv\Scripts\activate
```

### Development

```bash
npm run dev           # Start Next.js dev server (Turbopack, default port 3000)
npm run build         # Production build (output: standalone mode)
npm run start         # Start production server
npm run lint          # ESLint (via eslint-config-next)
npm run typecheck     # tsc --noEmit (TypeScript type-checking)
```

### Python

```bash
# The venv already exists at venv/. To recreate:
python -m venv venv
venv\Scripts\activate

# Install test dependencies
pip install pytest pytest-bdd

# Run tests (from project root)
pytest

# Run tests with verbose output
pytest -v
```

Tests use `pytest` with `pytest-bdd` for behavior-driven development (Given/When/Then).
Feature files live in `Tests/features/` and step definitions in `Tests/test_*.py`.
A `Tests/conftest.py` injects `src/` into `PYTHONPATH` so `apac` is importable without installing the package.

---

## Architecture

### Frontend (Next.js App Router)

```
src/
├── app/
│   ├── layout.tsx          # Root layout: metadata, fonts (Inter + JetBrains Mono), global CSS
│   └── globals.css         # Tailwind v4: @theme tokens, utility classes (text-gradient, glass, glow)
├── components/
│   ├── Navbar.tsx           # Fixed glass-morphism navbar
│   ├── Footer.tsx           # Multi-column footer
│   └── Button.tsx           # Reusable button (primary / secondary / accent variants)
└── apac/                    # Reserved for future code
```

- **Routing:** Next.js App Router — pages go in `src/app/` as `page.tsx` files
- **Styling:** Tailwind CSS v4 via PostCSS plugin (`@tailwindcss/postcss`). Theme tokens are defined in `globals.css` with `@theme { ... }`. Custom utilities: `.text-gradient`, `.glass`, `.glow`
- **UI primitives:** Radix UI (@radix-ui/react-checkbox, @radix-ui/react-label, @radix-ui/react-slot)
- **Forms:** react-hook-form with Zod validation (@hookform/resolvers)
- **Icons:** lucide-react
- **Class utilities:** clsx, tailwind-merge, class-variance-authority available (not yet used in components)
- **Build output:** `standalone` mode (`next.config.ts`) — produces self-contained deployment artifacts

### Design Tokens (Tailwind v4)

| Token | Value |
|---|---|
| `--color-accent` | `#7c3aed` (violet-600) |
| `--color-accent-light` | `#a78bfa` (violet-400) |
| `--color-accent-dark` | `#5b21b6` (violet-800) |
| `--color-surface` | `#0f0f23` (dark background) |
| `--color-surface-card` | `#1a1a2e` |
| `--color-surface-border` | `#2a2a4a` |
| `--font-sans` | Inter |
| `--font-mono` | JetBrains Mono |

---

## Coding Conventions

### TypeScript / React

- TypeScript strict mode is **on**
- Use `@/*` path alias for imports from `src/` (configured in `tsconfig.json`)
- Components use **default exports** with `export default function ComponentName()`
- Component props use **inline interfaces** (not separate type files)
- **No comments** in component code — keep code self-documenting
- Use Tailwind utility classes directly in JSX; avoid custom CSS unless adding a new reusable utility
- Semantic HTML: use `<Link>` from `next/link` for internal navigation
- CSS class composition uses template literals, not `clsx` (though `clsx` is available)

### Python

- Use dataclasses for structured objects
- Type hints preferred where practical
- Decorator-based API patterns (see `Router.register()` style)

---

## Current State & Known Gaps

- **No page routes exist yet** — `src/app/` has no `page.tsx`. The app is a layout-only skeleton.
- **No frontend tests** — no frontend test suite has been written.
- **Node modules not installed** — run `npm install` before any frontend work.
- **No CI/CD** — no `.github/workflows/` for the main project.
- **No README** — project documentation is absent.
- PyAutoGen is declared as a dependency but `router.py` does not currently exist on disk.
