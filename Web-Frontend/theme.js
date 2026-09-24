// Theme switcher (aiondps_design_pack_v1). The actual colors live entirely in style.css as
// `[data-theme]` attribute selectors - this module only ever toggles that one attribute on <html>
// and remembers the choice, so no component here needs to know a single hex value. The accent is
// a single fixed brand orange (see style.css's own Accent block), not a visitor choice.
//
// The very first paint already has the right theme applied without this module: index.html's own
// inline bootstrap script sets the attribute from localStorage before the stylesheet is even
// parsed. Importing this module just keeps that in sync (e.g. after a change in another tab) and
// wires up the visible switcher control in the header.
//
// Default is always Dark, never the OS's own light/dark preference - the whole visual identity
// (aiondps_design_pack_v1's cinematic hero art, this brand's dark-navy/orange look) is designed
// dark-first; a visitor whose OS merely prefers light shouldn't see a different first impression
// than the one the brand was actually designed around. Light stays a real, manually-picked option.
const STORAGE_THEME = "dpsmeter.theme";
const DEFAULT_THEME = "dark";

export const THEMES = [
  { id: "dark", labelKey: "theme.dark" },
  { id: "light", labelKey: "theme.light" },
];

function currentTheme() {
  const value = localStorage.getItem(STORAGE_THEME);
  return THEMES.some((th) => th.id === value) ? value : DEFAULT_THEME;
}

export function applyTheme(theme) {
  document.documentElement.setAttribute("data-theme", theme);
  localStorage.setItem(STORAGE_THEME, theme);
}

applyTheme(currentTheme());

/** Wires the header's theme control. Call once at startup, alongside setupLanguageSwitcher. */
export function initThemeSwitcher(t) {
  const themeSelect = document.getElementById("theme-switcher");
  themeSelect.replaceChildren(
    ...THEMES.map((th) => Object.assign(document.createElement("option"), { value: th.id, textContent: t(th.labelKey) })),
  );
  themeSelect.value = currentTheme();
  themeSelect.addEventListener("change", () => applyTheme(themeSelect.value));
}
