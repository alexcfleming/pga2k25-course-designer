# Terrain brush shapes

`shape_00.png` … `shape_68.png` are the 69 landscaping brushes in the PGA TOUR 2K25 Course Designer's
palette order (page 1 top-left is 0, page 5 bottom-right is 68), cut from screenshots of the Raise
palette (92 × 92 px, dark background subtracted so empty = 0). `catalog.png` shows them numbered.

A height stamp's `type` is the palette slot + 8 for slots 0–29 and + 9 for slots 30–68 (type 38 is not
in the palette). Established on a test plot with one dab of each brush (2026-10-03); see
docs/terrain-map.md, "Terrain model".

The image spans ±`scale` metres around the stamp at rotation 0, with its top towards +Z and its right
towards +X; `rotation.y` turns it like a Unity Y rotation. Brightness is the brush weight (0–1).
