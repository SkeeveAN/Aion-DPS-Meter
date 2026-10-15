# Translation files for native speakers

`export_translations.py` writes one spreadsheet (CSV) per language into `export/` (git-ignored): every text of the program with
the English source, the German text, the current text and a column to edit, plus a short instruction file per language.

`import_translations.py <file.csv>` checks a returned file and prints a report. Only with `--apply` it changes
`Client/assets/i18n/ui_strings.json`; it then still needs a `git diff` look and a build.

A returned file is treated as plain data: it is never run, and only lines that pass the checks (known key, same placeholders,
sensible length, no markup/links/formulas the English text lacks, no instruction-like wording, no hidden characters) are taken over.
Everything rejected is listed with the reason.
