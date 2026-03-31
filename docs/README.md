# Docs Workspace

This file is for maintaining the documentation workspace in the repository. The public VitePress home page lives at [`docs/index.md`](index.md).

## Local Development

Run the docs site from the repository root with:

```bash
npm install
npm run docs:dev
```

Create a production build with:

```bash
npm run docs:build
```

## Structure

- `index.md` is the VitePress home page.
- Top-level guide files map to the main docs sidebar.
- `roadmaps/` contains planning and implementation roadmap pages.
- `api/` contains generated API docs.

## Generated API Docs

The files under [`docs/api/`](api/index.md) are auto-generated from XML comments. Do not edit them by hand.

Regenerate them with:

```bash
.\generate-api-docs.bat
```
