# MY LITTLE GRAND PRIX — portfolio case study

This directory contains an isolated, static portfolio site for the Unity project. It does not alter or package the Unity runtime.

## Local development

Requirements: Node.js 22.12 or newer.

```bash
npm install
npm run dev
```

Open `http://127.0.0.1:4173/f1/`.

## Production build

```bash
npm ci
npm run build
npm run preview
```

The production output is written to `dist/` and is ignored by Git.

The deployed copy lives in the personal portfolio repository at `https://yundonggeurami.github.io/f1/`. To test the same path locally:

```powershell
$env:VITE_BASE_PATH = "/f1/"
$env:VITE_SITE_URL = "https://yundonggeurami.github.io/f1/"
npm run build
```

Do not add a Pages deployment workflow to the team Unity repository. The personal portfolio repository builds this child site and merges it into its single root Pages artifact.

## Media

The page remains usable without unpublished project media. See [`public/assets/README.md`](public/assets/README.md) for the exact replacement filenames and capture guidance.
