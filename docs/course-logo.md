# Course logo

A course can have a logo, in as many versions as the designer has. Every product prints the version that suits its paper. The logo is set on the **Logo** card in the Studio's Course settings (under Holes), or by hand-writing `Logos.json` beside the products.

## The logo set

There are three kinds, each in three tones (`CourseFormat/CourseLogos.cs`):

| | Colour | Ink (one dark colour) | Light (for dark backgrounds) |
|---|---|---|---|
| **Seal**: round, with the course name | ✓ | ✓ | ✓ |
| **Mark**: the emblem without text | ✓ | ✓ | ✓ |
| **Lockup**: the mark beside a wordmark | ✓ | ✓ | ✓ |

Fill in only the versions you have. Choosing a file on the card copies it into a `Logos` folder in the course's output folder, as `seal-colour.png`, `mark-ink.png` and so on, and lists it in `Logos.json`:

```json
{ "files": { "seal-colour": "seal-colour.png", "seal-ink": "seal-ink.png", "mark-colour": "mark-colour.png" } }
```

PNG with a transparent background works best (JPG and WebP also load). A large file is fine, because each product scales it, so small icon sizes aren't needed. A slot whose file has been deleted is ignored.

**Fallbacks** (`CourseLogos.Pick`): a product asks for a kind and a tone and gets the nearest version supplied. The tone comes first: ink falls back to colour, colour to ink, and light to colour (never ink, which would vanish on a dark ground). Then the kind: a seal and a mark stand in for each other, and a lockup falls back to the seal, then the mark. With no logo set, every product prints as it did before.

## Where it prints

| Product | Logo | Where |
|---|---|---|
| Scorecard maps: Modern, Modern Illustrated | Seal, colour | Strip title column, before the course name (0.58 in); on a portrait page, before the title |
| Scorecard maps: Vintage, Illustrated | Seal, ink | As above, to match the engraved ink |
| Scorecard map: Plan Sheet | Seal, ink | On a parchment disc in the emptiest top corner of the map (the title column is full) |
| Scorecard map: Routing Plan | Seal, colour | In the title box, before the course name |
| Scorecard map: Course Portrait | None | A wall print: title, credit and style credit only |
| Yardage book pages | Mark, colour | The page header becomes one line: the mark, the course name in spaced capitals, a gold divider, then the hole line (shrinking together if the name is long). The north arrow keeps the right end. |
| Greens book sheet | Seal, colour | Before the title |
| Green study pages | Mark, colour | Before the hole title |
| Course cards: course data, hole data, green size, Wind | Seal, colour | Before the title. A `logoPath` in the card settings file still wins. |
| Terrain map | Seal, colour | Centred in the empty foot of the side panel, when there's room for it to read |
| Hole sketch sheets | Mark, ink | Before the hole title (the sheets are for pencil and photocopying) |
| Construction estimate | Lockup, colour (summary page); mark, colour (every page) | Right of the summary title; in the running header before the course name |
| Readiness record | Mark, colour | Before the title, embedded in the HTML so the record stays one file |

No product prints on a dark background yet, so the light versions are kept for a future dark style.

## Code

- `CourseFormat/CourseLogos.cs`: the set, `Pick`, and `CourseLogosFile` (load, save, import, remove).
- `GreensBook/CourseLogoArt.cs`: decodes the images once per build, shared by a product's PNG and PDF; `Draw` fits an image into a box.
- Each builder loads the set from its output folder when its options don't carry one (`Logos`), so the command line picks up `Logos.json` in the `-o` folder.
- Studio: `WorkspaceViewModel.Logos.cs` and the Logo card in `WorkspaceView.axaml`.
