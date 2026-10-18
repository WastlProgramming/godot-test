import { defineConfig } from 'vitepress'

// Für GitHub Pages: DOCS_BASE=/<repo>/ npm run docs:build
const base = process.env.DOCS_BASE || '/'

export default defineConfig({
  lang: 'de-DE',
  title: 'JumpRun',
  description: 'Endloses 3D-Jump-and-Run mit Godot 4.6 .NET – Dokumentation',
  base,
  cleanUrls: true,
  lastUpdated: false,
  head: [['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}logo.svg` }]],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Start', link: '/' },
      { text: 'Anleitung', link: '/guide/installation' },
      { text: 'Spiel', link: '/spiel/welt' },
      { text: 'Technik', link: '/technik/architektur' },
      { text: 'Mitwirken', link: '/entwicklung/mitwirken' },
    ],
    sidebar: [
      {
        text: 'Anleitung',
        items: [
          { text: 'Installation & Starten', link: '/guide/installation' },
          { text: 'Steuerung', link: '/guide/steuerung' },
        ],
      },
      {
        text: 'Spiel',
        items: [
          { text: 'Spielprinzip & endlose Welt', link: '/spiel/welt' },
          { text: 'Plattformen & Sprungmuster', link: '/spiel/plattformen' },
          { text: 'Items', link: '/spiel/items' },
          { text: 'Charaktere', link: '/spiel/charaktere' },
        ],
      },
      {
        text: 'Technik',
        items: [
          { text: 'Architektur', link: '/technik/architektur' },
          { text: 'Audio', link: '/technik/audio' },
          { text: 'Grafik', link: '/technik/grafik' },
          { text: 'Tests ausführen', link: '/technik/tests' },
        ],
      },
      {
        text: 'Entwicklung',
        items: [{ text: 'Mitwirken', link: '/entwicklung/mitwirken' }],
      },
    ],
    search: {
      provider: 'local',
      options: {
        translations: {
          button: { buttonText: 'Suchen', buttonAriaLabel: 'Suchen' },
          modal: {
            noResultsText: 'Keine Ergebnisse für',
            resetButtonTitle: 'Suche zurücksetzen',
            footer: { selectText: 'auswählen', navigateText: 'navigieren', closeText: 'schließen' },
          },
        },
      },
    },
    outline: { level: [2, 3], label: 'Auf dieser Seite' },
    docFooter: { prev: 'Vorherige Seite', next: 'Nächste Seite' },
    darkModeSwitchLabel: 'Darstellung',
    lightModeSwitchTitle: 'Helles Design',
    darkModeSwitchTitle: 'Dunkles Design',
    sidebarMenuLabel: 'Menü',
    returnToTopLabel: 'Nach oben',
    langMenuLabel: 'Sprache',
    notFound: {
      title: 'Seite nicht gefunden',
      quote: 'Hier ist nur Nebel – dieser Abschnitt wurde wohl schon despawnt.',
      linkText: 'Zur Startseite',
    },
    footer: {
      message: 'Alle Meshes prozedural, alle Sounds synthetisiert – keine externen Assets.',
      copyright: 'JumpRun',
    },
  },
})
