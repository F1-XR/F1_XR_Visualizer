# yundonggeurami.github.io integration

The only public deployment target for this case study is the personal portfolio repository:

- Repository: `YunDonggeurami/Yundonggeurami.github.io`
- Root portfolio: `https://yundonggeurami.github.io/`
- MLGP case study: `https://yundonggeurami.github.io/f1/`
- Child source directory: `f1/`

The team Unity repository must not publish a Pages site for this portfolio.

## Integrated structure

The personal repository keeps its existing React/Vite root site. The audited MLGP static site is copied into `f1/` as an independent Vite child project. The existing root Pages workflow performs this order:

1. clean-install, lint, and build the root portfolio;
2. clean-install, build, and verify the `f1/` child with `VITE_BASE_PATH=/f1/` and `VITE_SITE_URL=https://yundonggeurami.github.io/f1/`;
3. copy `f1/dist` into `dist/f1`;
4. upload and deploy one combined `dist` artifact.

Using one workflow prevents a child deployment from replacing the root portfolio.

## Main project card

The former planned “Main Project” card is replaced by MY LITTLE GRAND PRIX. Its Case Study button points to `/f1/`. The GitHub button is labeled “Team GitHub” because the source repository belongs to the team and is not the portfolio hosting target.

The card retains a visible contribution-verification note and a missing-media label until the project owner supplies an evidenced personal role and rights-cleared Quest capture.

## Publication checks

- Root `/` and child `/f1/` must both return HTTP 200 from the same artifact.
- `/f1/` canonical and Open Graph URLs must use the personal domain.
- No workflow in the team Unity repository may deploy this portfolio.
- A real thumbnail is added only after capture and trademark/media rights are reviewed.
