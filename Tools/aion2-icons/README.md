# AION 2 icons

Pulls the item and skill icons for the character pages out of an installed AION 2 client. Local use only; the
client's pak key is **not** stored anywhere (pass it as `AION_KEY`). The icons are NCSoft artwork (see the
site disclaimer); nothing else from the client is shipped.

Pipeline (what produced `Web-Frontend/images/aion2/icons` and `Backend/src/data/aion2/{item,skill}_icons.json`):
1. `Extract` (C#, needs a checkout of [CUE4Parse](https://github.com/FabianFG/CUE4Parse) next to it, build with dotnet):
   `Extract <Paks dir> list <files.txt>`, `dump <outdir> <path substring>...` (cooked texture packages),
   `table <outdir> <name>...|*` (decrypted DataTable `.dat` files). No `.usmap` exists for this game, so textures
   are not decoded by CUE4Parse: the raw packages are dumped and decoded by `convert_icons.py`.
2. Textures (`Item/Weapon|Armor|Accessory|ETC`, `Skill`) are single-mip DXT1/DXT5 with the pixel data 12 bytes
   before the end of the package; `convert_icons.py` wraps them in a DDS header (Pillow) and writes 128 px WebP.
3. DataTable strings are UTF-16 XOR-ed with `25 00 a8 00 7e 00 91 00` per string (`dat.py`). Items carry their
   `Icon_*` name directly (`build_item_map.py`). Skills only carry `<Class>_Skill###`, which maps to
   `ICON_<CC>_SKILL_###` (`build_maps.py`); passives/stigmas have no icon name in the tables, so they fall back
   to the initials tile.

Added 2026-10-04:
- Passive skills: the client names no icon, but the in-game order is the skill-id order per class, so the n-th passive
  (ids `<class>71..80 0000`) uses `ICON_<CC>_SKILL_Passive_<n>` (checked against the user's Gladiator passives).
- Daevanion: `build_nodes.py` rebuilds `Backend/src/data/aion2/daevanion_nodes.json` from `DaevanionNode.dat` (the old
  third-party layout was wrong: real boards are 11x11 inside the 15x15 grid, Azphel is board 16). The node art
  (`Web-Frontend/images/aion2/daevanion`) is cropped from `Atlas_FWindow_Daevanion` (mip 0 starts at the data offset,
  B8G8R8A8 512x512); the grey "common" tile is not in the atlas and was drawn to match.
