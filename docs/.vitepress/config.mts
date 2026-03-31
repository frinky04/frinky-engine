import { defineConfig } from "vitepress";

export default defineConfig({
  title: "FrinkyEngine",
  description: "Documentation for the FrinkyEngine editor, runtime, and game development workflow.",
  lastUpdated: true,
  cleanUrls: true,
  base: "/FrinkyEngine/",
  // Generated docs/api pages currently contain some unresolved xmldoc2md links.
  // Keep the site buildable without editing generated output.
  ignoreDeadLinks: true,
  themeConfig: {
    nav: [
      { text: "Guides", link: "/getting-started" },
      { text: "API", link: "/api/index" },
      { text: "GitHub", link: "https://github.com/frinky04/FrinkyEngine" },
    ],
    search: {
      provider: "local",
    },
    outline: {
      level: [2, 3],
    },
    sidebar: [
      {
        text: "Start Here",
        items: [
          { text: "Overview", link: "/" },
          { text: "Getting Started", link: "/getting-started" },
          { text: "Editor Guide", link: "/editor-guide" },
          { text: "Scripting", link: "/scripting" },
        ],
      },
      {
        text: "Core Guides",
        items: [
          { text: "Components Reference", link: "/components" },
          { text: "Physics", link: "/physics" },
          { text: "Audio", link: "/audio" },
          { text: "Rendering & Post-Processing", link: "/rendering" },
          { text: "Game UI", link: "/ui" },
          { text: "Prefabs & Entity References", link: "/prefabs" },
          { text: "Exporting & Packaging", link: "/exporting" },
          { text: "Project Settings", link: "/project-settings" },
        ],
      },
      {
        text: "Reference",
        items: [
          { text: "API Reference", link: "/api/index" },
        ],
      },
      {
        text: "Roadmaps",
        items: [
          { text: "CanvasUI Roadmap", link: "/CANVASUI_ROADMAP" },
          { text: "Audio Roadmap", link: "/roadmaps/audio_roadmap" },
          { text: "UI Roadmap (Legacy)", link: "/roadmaps/ui_roadmap" },
          { text: "Asset Icon Guide", link: "/roadmaps/asset_icon_implementation_guide" },
        ],
      },
    ],
    footer: {
      message: "FrinkyEngine documentation is built with VitePress and published via GitHub Pages.",
      copyright: "Copyright © FrinkyEngine contributors",
    },
    socialLinks: [
      { icon: "github", link: "https://github.com/frinky04/FrinkyEngine" },
    ],
  },
});
