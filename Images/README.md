# Roslynk logos

The selected terminal design uses a cyan prompt and purple cursor. The large lockup reads "Roslynk" and "SEMANTIC C# CLI". The small icon contains the terminal symbol only.

- `roslynk-logo-large.png`: 1024 × 1024, transparent, white wordmark for dark backgrounds.
- `roslynk-logo-small.png`: 64 × 64, transparent.
- Matching SVGs have outlined text and require no installed fonts.
- `designs/*.json` are the editable vectorctl sources. Do not edit SVG paths by hand.

The standalone skill includes matching SVG copies in `skills/roslynk/assets`; keep them synchronized with `Images` when changing the artwork.

## Regenerate

Run from the repository root with vectorctl. The sources use a 101.6 mm square canvas so exports at 256 and 16 DPI produce exactly 1024 and 64 pixels. Use bundled Liberation Sans; do not enable system fonts.

```sh
vectorctl generate Images/designs/roslynk-logo-large.json --output /tmp/roslynk-logo-large.svg --preset print-four-colour --no-preview
vectorctl run /tmp/roslynk-logo-large.svg --workflow outline-text --preset print-four-colour --no-preview
vectorctl validate /tmp/roslynk-logo-large.svg --preset print-four-colour
vectorctl export /tmp/roslynk-logo-large.svg --preset print-four-colour --format svg --format png --stem roslynk-logo-large --dpi 256 --output-directory Images --no-preview

vectorctl generate Images/designs/roslynk-logo-small.json --output /tmp/roslynk-logo-small.svg --preset print-four-colour --no-preview
vectorctl validate /tmp/roslynk-logo-small.svg --preset print-four-colour
vectorctl export /tmp/roslynk-logo-small.svg --preset print-four-colour --format svg --format png --stem roslynk-logo-small --dpi 16 --output-directory Images --no-preview

cp Images/roslynk-logo-large.svg Images/roslynk-logo-small.svg skills/roslynk/assets/
```

Both final SVGs passed `print-four-colour` validation with exit 0, no errors and no warnings. PNG dimensions and transparency were verified and renders inspected. vectorctl 0.1.0 emitted this advisory during text outlining:

> outlining moved the artwork bounds by 31.541 mm, more than the 0.100 mm expected; check the font metrics

The exported wordmark and descriptor were visually checked against the selected concept. This workflow advisory is distinct from final validation, which has no findings.
