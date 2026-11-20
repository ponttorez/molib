# Localization

## Decision

Gum does localization; molib adds none of its own. Gum reads translations from CSV or RESX, replaces string keys in any
`Text` and switches the language on the fly (`LocalizationService.CurrentLanguage`, the `CurrentLanguageChanged`
event). Everything molib shows as text (toasts, standard visuals) goes through ordinary Gum text elements and is
translated the same way. molib has no strings of its own.

## How to use

- Translations are attached to the Gum project in the editor or loaded in code through `LocalizationService` (Gum
  docs, section Localization): `AddCsvDatabase` reads a CSV whose first column is the key and the others are languages.
- The language is switched by `GumService.Default.LocalizationService.CurrentLanguage = index`: 0 is the key column,
  1 the first language and so on.
- Toasts and labels get a string key, like any `Text`.

## What Gum does

Verified against the sources and by running Gum 2026.11.2.1:

- `Text` of `TextRuntime`, `Label`, `Button`, `CheckBox` and other controls goes through `Translate` of the current
  localization service; text set through `SetTextNoTranslate` does not. `ListBoxItem.UpdateToObject` does not translate.
- The Gum V3 list row visual (`ListBoxItemVisual`) writes the placeholder «ListBox Item» through the translatable
  `Text`, and Gum remembers it as a key; `ListBoxItem.UpdateToObject` writes the item text around it (`RawText`), and
  the key stays. A language change translates the remembered keys again — every row of any Gum list becomes «ListBox
  Item». The molib row visual (`Molib.Visuals.ListBoxItemVisual`) and the default `VirtualList` rows keep the
  placeholder as plain text (`SetTextNoTranslate`); a game fixes its own row visual the same way.
- Gum writes the rows of `ListBox` and `ComboBox` from `ToString` of the item and does not translate them: the Gum docs
  ask to translate items before they go into the list. So on a language change the game replaces the items with
  translated ones itself and restores the selected row. Text built in code from a translated key (numbers, names) is
  also written again on `CurrentLanguageChanged`.
- A `CurrentLanguage` change translates the texts already shown in `Root`, `PopupRoot` and `ModalRoot` again: button
  captions, check boxes and toasts change by themselves, and content-sized visuals change their width.
- Gum's `LocalizationService` with a loaded database appends `(loc)` to every text that is not a key (except texts
  without letters). This gets in the way of a game that writes part of its text in code.
- The service can be replaced with one's own: the `ILocalizationService` interface (`Translate`, `CurrentLanguage`,
  `Languages`, `CurrentLanguageChanged`, `AddDatabase`, `Clear`) and the public property
  `CustomSetPropertyOnRenderable.LocalizationService`. Gum subscribes to `CurrentLanguageChanged` of the new service and
  translates the shown texts on a language change the same way. `AddCsvDatabase` is an extension of the interface and
  fills one's own service too.

## Fonts and scripts

Verified on Gum 2026.11.2.1 with KernSmith 0.28:

- Gum's built-in font is a bitmap font with Latin only. Gum falls back to it if KernSmith did not find the font: the
  default font «Arial» is looked up among the system fonts and, on Linux for example, is not found. There is no error on
  screen — just `?` instead of Cyrillic.
- So a game with Russian text registers its own TTF (`KernSmithFontCreator.RegisterFont`) and makes it the font of all
  three text styles of the theme — `Styling.ActiveStyle.Text.Normal`, `Strong` and `Emphasis`. `Normal` alone is not
  enough: V3 visuals use the other styles too, for example `ComboBox` shows the selected row in the `Strong` style.
- A style left on «Arial» is expensive with `TextRuntime.UseFontOversampling` and zoom ≠ 1: Gum rasterizes the font for
  the on-screen size again, does not find it and does not remember the failure — it tries again every frame, with an
  exception and the line `Error creating oversampled font` in the log. Verified: on a screen with one `ComboBox` this
  raised the frame from 0.8 to 4.9 ms of drawing. At zoom 1 there is no rerasterization, and the error does not show.
- KernSmith builds bold and italic faces from a regular TTF: separate files for `Strong` and `Emphasis` are not needed
  (verified on Noto Sans Regular, Cyrillic draws in bold). A face file can be registered as the bold of the same family
  — `RegisterFont(family, bytes, style: "Bold")`: this way a semibold file (for example, Inter SemiBold) goes to text
  with `IsBold`.
- The character set of a font is given by Gum ranges: Latin only by default (`32-126,160-255`). Cyrillic, Greek,
  Armenian, Georgian and typographic marks (dashes, guillemets) are added with `BmfcSave.AddFontRange` before any text
  is created.
- `TextRuntime.UseAutomaticFontGrowth` (creating missing characters on the fly) exists in this package but did not add
  characters in the check; rely on explicit ranges.
- The font must contain the needed scripts: Noto Sans covers Latin, Cyrillic, Greek; Armenian and Georgian need separate
  Noto fonts.

## What it means for molib

- No molib module puts English text of its own into the interface. If default text is needed (for example, a button
  caption in a standard visual), the game sets it.
- Text width differs between languages: molib's standard visuals size to their content (`RelativeToChildren`) and do
  not cut text off.
- Formatting numbers, dates and plurals is the game's job (.NET `CultureInfo`, a message library of the game's choice).
