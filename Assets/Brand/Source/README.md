# Brand geometry sources

These original SVG files preserve the geometry sources used to build
[`Themes/Branding.xaml`](../../../Themes/Branding.xaml):

- `WinSereno-Mark-Color.svg`: source for the mark's outer shapes and central band.
- `WinSereno-Logo-Horizontal-Color.svg`: source for the wordmark and its proportions.
- `WinSereno-Logo-Horizontal-Dark.svg`: source for the letter counters recovered
  during conversion to WPF geometry.

They are retained unchanged as design sources. Their original tracing artifacts
and canvases are not the runtime implementation; the cleaned native WPF geometry
is authoritative for application rendering. No SVG is loaded at runtime.

The Windows icon remains `../WinSereno.ico`. The brand guide remains
`../WinSereno-BrandGuide.png`, and `../Banner.png` is used by the public READMEs.
