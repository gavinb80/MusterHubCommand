// Same type scale as Rota/Skills' own tokens -- derived from
// MusterHubApp/Resources/Styles/Styles.xaml.

export const fontFamily = {
  base: '"Open Sans", ui-sans-serif, system-ui, sans-serif',
} as const;

export const fontWeight = {
  regular: 400,
  semibold: 600,
} as const;

export const fontSize = {
  headline: "32px",
  subheadline: "24px",
  pageTitle: "28px",
  cardTitle: "16px",
  body: "14px",
  cardSubtitle: "13px",
  caption: "12px",
} as const;

export const typography = { fontFamily, fontWeight, fontSize } as const;
