# HistorianSyncTool — Technical Documentation

Two versions of the same document:

| File | Language |
|---|---|
| [`index.html`](index.html)    | English |
| [`index.de.html`](index.de.html) | Deutsch |

Each file has a small **EN · DE** switch in the top-right corner that links to the other.

## What's inside

A single self-contained HTML file with:

- Sticky sidebar table of contents with live filter (Ctrl+K)
- Architecture diagram + 7 sequence diagrams (Mermaid)
- File-by-file walkthrough of every Form, Service, Model, and UI Control
- Code excerpts for every key method (`ExecuteBackfill`, `RunGapAnalysis`, etc.)
- Configuration reference (`app.config` + `Properties.Settings`)
- Test coverage summary
- Build & deploy instructions
- Glossary of domain terms

## Print layout

The print stylesheet is the second-pass version that fixes the issues from the first
print attempt:

- **Each major section (H2) starts on a new page.**
- **Headings stick to the content that follows them** (no orphans — H3 + H4 never end up
  on the bottom of a page with their body on the next).
- **Code blocks and diagrams never split across page breaks** (`break-inside: avoid`).
- **Diagrams render at full page width.** After Mermaid finishes rendering, a small piece
  of JS strips the hard-coded `width`/`height` attributes from the generated SVG so the
  diagrams scale to the page. There's also an extra fire on `beforeprint` so the resize
  takes effect even if the diagrams haven't been visible on screen yet.
- **A4 page size** with reasonable margins (`@page` rules) and a tighter font for code
  (`8.2pt` Consolas).
- **Header colors print** thanks to `print-color-adjust: exact` on the navy table headers
  and callouts.

### How to print

1. Open `index.html` (or `index.de.html`) in Chrome, Edge, or Firefox.
2. Wait a couple of seconds for the Mermaid diagrams to render.
3. `File → Print` (`Ctrl+P`).
4. Destination → "Save as PDF" — or send straight to a printer.
5. Settings: **A4**, **Default margins**, **Background graphics ON** (so the navy headers
   come out correctly).

### Offline reading

Mermaid and highlight.js load from CDN. For pure offline use, open the file once with
internet so the libraries cache; or print to PDF while online — the diagram SVGs get
embedded into the PDF and remain visible offline.

## Updating

Edit the HTML directly. Search for the section heading (e.g. `<!-- 5.3 GapAnalysisService -->`)
to jump to the relevant block. Mermaid diagram sources are embedded as
`<pre class="mermaid">…</pre>` — edit the syntax in place.

When you change content, keep the two language files in sync:

| English file (`index.html`) | German file (`index.de.html`) |
|---|---|
| sidebar nav items | identical anchor IDs |
| section IDs (`#flow-backfill` etc.) | identical |
| code identifiers (`ExecuteBackfill`, `MainForm`) | unchanged (English code) |
| prose paragraphs | translate to German |
| file paths and config keys | unchanged |

Last source review: 2026-05-28 (after Phases 4 + 7 landed).
