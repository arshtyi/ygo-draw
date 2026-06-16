# Asset Scripts

This script downloads:

- `assets.tar.xz` from `arshtyi/ygo-assets`
- `ot.json` and `rd.json` from `arshtyi/ygo-cards`
- the current `lib/` files from `arshtyi/typst-ygo`

It lays out resources as `assets/ot`, `assets/rd`, and root `lib/`, then imports both card series into PostgreSQL with `series` set to `ot` or `rd`.
