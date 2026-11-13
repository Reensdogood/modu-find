# 모두찾아줘 icon

Original code-native artwork created for this project. Three stacked documents
represent file and content search; the large cyan magnifying glass remains
recognizable in Windows title bars and taskbars. No lettering, font, emoji,
third-party logo, stock imagery, or downloaded asset is included.

- Source: `tools/build_icon.py`; editable vector: `assets/modufind.svg`.
- License: project-internal original artwork, distributed under the project's
  existing MIT license (`LICENSE.txt`).
- Palette authority: the requested blue/cyan icon direction, with white papers
  and a dark blue outline for a legible search symbol.
- Raster: `modufind.png`, RGBA 1024 × 1024, transparent outer corners.
- Windows icon: `modufind.ico`, RGBA 16, 24, 32, 48, 64, 128, and 256 px.
- Individual PNG variants are provided at those seven Windows icon sizes.
- Rebuild: `python tools/build_icon.py` from the `document-finder` directory.
  Pillow is a build-time tool only; the Windows application needs no Python
  or image library at runtime.
