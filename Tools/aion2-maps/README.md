# AION 2 world maps

`build_maptiles.py [Verteron|Altgard|Abyss ...]` assembles the game's own world maps (`UI/Map/WorldMap/<map>/Res/<map>_<RR>_<CC>`; dumped
with `Tools/aion2-icons` Extract: `dump <out> "WorldMap/World_L_A/Res/World_L_A_0"`) into tiles with a transparent background.

- **Layout:** in the file names RR is the **column** and CC the **row** (the other way round looks like a jigsaw with wrong edges).
- **Tiles:** 8x8 for the two continents (`World_L_A` = Verteron/Elyos, `World_D_A` = Altgard/Asmodian), 4x4 for `Abyss_Reshanta_A`. Verteron keeps
  2048 px per tile in the client; for Altgard and Abyss the client only ships the 1024 px mip (data in the `.ubulk`, 696,320 bytes = 1024..128).
- **Transparency:** land = saturated or dark pixels; thin lines, text and the compass are removed by an opening, holes in the land are filled, small
  pieces dropped; the blue-grey sea mist is removed per map (hue / value ranges in the `cfg` line, a coastal band for the continents).
- **Output:** `Karte_<Name>.png` (4096, transparent) and `<Name>_tiles/<col>_<row>.webp` (native resolution, alpha) next to the old pictures.
- **World coordinates -> map:** not calibrated yet with the right tile layout (the first fit used the wrong layout); fit again with the spawn points
  in `Client/assets/aion2/pets/spawns.json` before the maps are used for positions.
