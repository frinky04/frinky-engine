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
      { text: "Start Here", link: "/getting-started" },
      { text: "Generated API", link: "/api/index" },
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
        text: "First Project",
        items: [
          { text: "Overview", link: "/" },
          { text: "Getting Started", link: "/getting-started" },
          { text: "Editor Workflow", link: "/editor-guide" },
          { text: "Choosing Components", link: "/components" },
        ],
      },
      {
        text: "Gameplay",
        items: [
          { text: "Scripting", link: "/scripting" },
          { text: "Game UI", link: "/ui" },
          { text: "Prefabs & Entity References", link: "/prefabs" },
        ],
      },
      {
        text: "Engine Systems",
        items: [
          { text: "Physics", link: "/physics" },
          { text: "Rendering & Post-Processing", link: "/rendering" },
          { text: "Audio", link: "/audio" },
        ],
      },
      {
        text: "Shipping",
        items: [
          { text: "Exporting & Packaging", link: "/exporting" },
          { text: "Project Settings", link: "/project-settings" },
        ],
      },
      {
        text: "Reference",
        items: [
          { text: "Generated API Reference", link: "/api/index" },
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
