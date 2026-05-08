# CLAUDE.md

Project-specific instructions for Claude Code agents working in TrayAppWPFSkeleton.

## Workflow rules

- **Never coauthor commits.** Do not append `Co-Authored-By` trailers. The user authors all commits.
- **Do not build the Release profile.** Release runs publish + AOT + trimming and bumps `buildnumber.txt` (see `Directory.Build.props`). Use `dotnet build -c Debug` for verification.
- **Always verify solutions before reporting.** Run a Debug build (or the relevant test) and confirm the symptom is gone. Do not claim success based solely on a code change looking correct.
- **Use subagents whenever possible.** Delegate codebase exploration, multi-file research, and parallelizable lookups to the `Explore` or `general-purpose` agent. Reserve the main context for synthesis and editing.
- **Output `.md` audits and writeups to `claudeit/`.** Never drop audit/analysis markdown at the project root or into `documentation/`. Create `claudeit/` if it does not exist.
- **Do not guess logic or API usage.** Read the actual local code. For external APIs (Win32, .NET BCL, third-party packages) check the local source / decompiled assembly or fetch the upstream repository — never invent signatures or behavior.

## CRITICAL

- **ALWAYS Break comments on real phrase boundaries** — items, sentences, or natural clauses. Only break when forced to by the line limit. Never artificially fragment a continuous thought across lines. Rephrase comments to fit this idea. Make them similar to lists.
- **Line wrap at 120 chars** (enforced by `.editorconfig` for code; applies equally to comments).
- **Concise but descriptive.** Comments are conceptual and relaxed: what a reader or agent really needs to know, not exposition.
- **Space after `//`.** No period at the end of fragments; full sentences keep their period.
- **ASCII only** in code and comments. Run `fix-ascii.py` if you suspect drift.

These comment rules must be followed.

## References

- `.editorconfig` — whitespace, brace style, analyzer severities (the enforceable subset of this guide)
- `Directory.Build.props` — build-number embedding, `obj/` redirect
- `src/TrayAppWPFSkeleton.csproj` — Release publish settings (single-file, trimmed, ReadyToRun)
- `fix-ascii.py` — ASCII normalization pass


## Style guide

The `.editorconfig` is authoritative for whitespace, line length, brace style, and analyzer severities. The rules below are the conceptual conventions agents should follow on top of that.

### Code organization

- **Separate by concept, not layer.** Group code that belongs to the same feature or domain together; don't split a feature across `Models/`, `Services/`, `Helpers/` just for the sake of layering.
- **Avoid deep inheritance.** Prefer composition or static helpers.
- **Avoid premature abstraction.** Three similar lines is better than a half-fitting interface. Extract only after a real second use site appears.

### Control flow

- **Switch over if-else** for branching with three or more arms.
- **Guard clauses over nesting.** Use `continue` / `break` / `return` to exit early; do not wrap the happy path inside nested `if` blocks.
- **Iterative over recursive.** Use `while (!done)` or explicit work-stack loops for traversal.
- **Initialize then populate.** Construct the collection first, fill it second — don't interleave allocation and population.
- **Inline calculations in tight loops.** Avoid method calls per iteration when the body is small.

### Types and constants

- **Explicit types — never `var` or `auto`.**
- **Hoist string and constant literals.** Repeated literals must become `const` or `static readonly` members. No magic strings sprinkled at call sites.
- **Respect nullability annotations.** No `?.`, `??`, or `is null` against a non-nullable-typed expression. If it can really be null, fix the annotation.

### Naming

Use descriptive variable names. Single-letter names are reserved for tight loop counters and well-known math conventions.

**Acronym casing** (treat as a single word — uppercase the whole thing):

| Acronym | Form    |
|---------|---------|
| VCP     | `VCP`   |
| DDC     | `DDC`   |
| CCD     | `CCD`   |
| EDID    | `EDID`  |
| DWM     | `DWM`   |
| API     | `API`   |
| UI      | `UI`    |
| PID      | `PID`    |

Examples: `DDCMonitor`, `EDIDParser`, `VCPCode`, `CCDInterop`, `UIThread`, `APIClient`, `PIDController`, `WPFDispatcher`.