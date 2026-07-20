// Same brand palette as Rota/Skills' own design-tokens packages -- derived
// from MusterHubApp/Resources/Styles/Colors.xaml, the one canonical source
// for the MusterHub brand, so Command's console looks like a sibling of the
// other bolt-on consoles rather than a different product.

export const brand = {
  primary: "#C8102E",
  primaryDark: "#FF3B30",
  secondary: "#0B1F3A",
  tertiary: "#1B3358",
} as const;

export const neutral = {
  white: "#FFFFFF",
  black: "#000000",
  gray100: "#E1E1E1",
  gray200: "#C8C8C8",
  gray300: "#ACACAC",
  gray400: "#919191",
  gray500: "#6E6E6E",
  gray600: "#404040",
  gray700: "#333333",
  gray900: "#212121",
  gray950: "#141414",
} as const;

export const surface = {
  pageBackgroundLight: "#F5F6FA",
  pageBackgroundDark: "#0A0E14",
  surfaceLight: "#FFFFFF",
  surfaceDark: "#141A24",
  surfaceAltLight: "#EEF1F6",
  surfaceAltDark: "#1C2430",
  borderLight: "#E3E7ED",
  borderDark: "#232B38",
  textPrimaryLight: "#15202B",
  textPrimaryDark: "#F5F7FA",
  textSecondaryLight: "#67727E",
  textSecondaryDark: "#8B97A8",
} as const;

// Incident/appliance status colours -- Command's own vocabulary, distinct
// from Rota's availability statuses. Hazard uses the same red as
// unavailable/critical elsewhere on the platform: a hazard entry should
// read as urgent at a glance, not as a neutral log line.
export const status = {
  open: "#FF9F0A",
  closed: "#8E8E93",
  cancelled: "#8E8E93",
  mobilised: "#0A84FF",
  enRoute: "#FF9F0A",
  onScene: "#34C759",
  stoodDown: "#8E8E93",
  hazard: "#FF453A",
} as const;

export const colors = { brand, neutral, surface, status } as const;
