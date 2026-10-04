# Interface localization

`Strings.es.xaml` and `Strings.en.xaml` contain the same `Text.*` keys. Spanish is
the canonical language and the default for both new and older configuration files.
`LocalizationService.Languages` supplies the Settings dropdown; adding a language
requires a dictionary with the same keys and one entry in this registry.

Static XAML labels use `DynamicResource`. The selected dictionary is merged before
the first window is created. `Language` is persisted alongside `Theme` in the
existing portable `config.json`; resetting UI preferences restores Spanish and Light.

Generated messages and stored results remain canonical through `Source(key)`.
`LocalizationPresentation` binds their visible values together with the catalog
revision, so changing language updates existing controls without replacing commands,
results, navigation or history. Complete messages take precedence over reusable
format fragments. Keep formatting placeholders and significant whitespace aligned
between dictionaries; prefer complete messages when possible.

Hardware identities, interface names, paths, commands and native stdout/stderr are
opaque. Diagnostic details translate only application context around explicitly
recorded native segments. The UI language never changes the process culture, Windows
output decoding, parsers, worker protocol or technical log format.

Run `LocalizationChecks.ps1` for key parity and wiring, and
`LocalizationExperienceChecks.ps1` for live WPF switching, preference migration,
theme independence and native-output/parser regressions. The latter uses inert
fixtures and isolated preferences; it never runs maintenance or requests UAC.
