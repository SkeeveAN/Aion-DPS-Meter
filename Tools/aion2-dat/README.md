# AION 2 text tables

A reader for the localisation tables of an installed AION 2 client (`L10NString.dat`, one per language),
written from the public description of the format (the CUE4Parse project documents it). It reads local
files only; nothing from it is shipped with the meter or the website.

## What the format is
1. The game's `.pak` files are AES-256 encrypted with the community-documented Unreal key
   (not stored here). `repak` (`cargo install --git https://github.com/trumank/repak repak_cli`) unpacks
   single files: `repak --aes-key 0x<key> unpack -i '<path>' -o <out> <pak>`.
2. `L10NString.dat` is a second layer: `i32 2`, a 16 byte XOR-ed header (packed/encryption type/aligned/raw
   size), then AES-256-ECB data (key looked up by a seed in `key_manifest.dat`, which itself is encrypted),
   whose decrypted block holds a 0x20 byte prefix and an LZ4 block. The result is `i32 1`, an FString
   namespace and a map of FString key -> FString value. `aion2dat.py` implements all of it, including the
   small BLAKE3 needed for the key derivation (self-checked against the official test vectors).

## Local, not committed
`constants.local.json`:
```
{ "manifest_hash_key_hex": "<32 byte constant of the format, hex>", "header_xor_constant": "0x<u64>" }
```
Both constants are in the public CUE4Parse sources (`FAion2KeyManifestFile._manifestHashKey`,
`Aion2DatFileEncryption.HeaderXorConstant`). The file is in `.gitignore`.

## Where the files are
- `key_manifest.dat`: `Aion2/Content/StartUpData/Table/` inside `pakchunk0-Windows.pak`.
- `L10NString.dat`: `AION2/Content/L10N/Text/<locale>/` inside the per-language paks under `Paks/L10N/Text/`.
  The EU client ships de-DE, en-US, es-ES, fr-FR, ja-JP, ko-KR, pt-BR, ru-RU.

## How the names were matched (2026-10-02)
String keys do not follow our internal names, so instances and bosses are matched by their English text:
the key whose en-US value equals our English name gives the translations at the same key in every language.
Skills are `SkillString_STR_SKILL_<CLASS>_<id>_skill_name` and match by id. Texts the client has no value for
(`???`, content not in the EU client) are left out. Skill icons are textures in the IoStore containers
(`.utoc/.ucas`) and are not read by this tool.

## Data tables (`AION2/Content/Data/Table/*.dat`, added 2026-10-06)
`dectable.py <key_manifest.dat> <table.dat> <out.bin>` decrypts them (seed = hash of the table name). Two containers:
`i32 13` + header kind 1 (AES-ECB + LZ4, small tables) and header kind 2 + type 3 (AES-CTR style stream, big tables such as
`NpcData`, `Item`, `Skill`: keystream block n = AES(key, `u64 nonce | u64 n`), nonce = first 8 bytes of blake3(key + "nonc"),
as in CUE4Parse's `Aion2DatFileEncryption.DataTable.cs`). Rows are typed structs and need a `.usmap` to decode properly;
without one, strings (UTF-16, XOR `25 00 a8 00 7e 00 91 00` per string) can be scanned. `NpcData` rows are
`[i32 npcId][name FString][STR_ text key FString][subtitle key][MOB_ model]...` - the text key + `String_<key>_body` in
`L10NString.dat` gives the names. `WorldMapFieldNamed` lists the named field bosses by internal NPC name.

## NPC names (2026-10-08)
`build_npcs.py <NpcData.bin> <l10n dir> <out.json>` turns the decrypted `NpcData` table (`dectable.py`, from `pakchunk401000-Windows_0_P.pak`)
into `{npcId: {en, de, fr, es, ru, pt, ja, ko}}` (12,589 monsters). The meter ships English, German, French, Spanish and
Russian as `Client/assets/aion2/npcs/npc_names.json` and names a monster from the NPC id of its spawn frame.

## Pets (2026-10-08)
`build_pets.py <VehicleList.bin> <NpcData.bin> <npc_names.json> <l10n dir> <out.json> [extra_pets.json] [Item.bin]` writes
`Client/assets/aion2/pets/pets.json` for the pet farming overlay: the 207 pets (`VehicleList` row = `[i32 petId][str_veh_<key>]`) with their
names in five languages (item text `STR_ITEM_VEHICLE_<KEY>_A_01_B`, "Pet: " prefix removed), the monsters that belong to each pet and the soul
item ids. A pet is named after the model of the monsters that drop its soul (`MOB_Cherubim_02` <- pet `Cherubim_02`); 59 of 63 souls seen in
real captures matched that rule, the rest are in `extra_pets.json`. The pet list of the login frame (opcode 144) is
`u32, varint n, n x (id, id, level 1-3), varint m, m x (id, progress)`; progress needed is 25 at level 1, 75 at level 2.

## Spawn points (MapData.dat, 2026-10-08)
`build_spawns.py <unpacked AION2/Content/Data/Map> <pets.json> <out.json>` reads the spawn points of the pet monsters out of every `MapData.dat`
(745 maps) into `Client/assets/aion2/pets/spawns.json` (`{"maps": {map: {npcId: [[x,y,z],...]}}}`, 246 maps, 20,791 points, 190 of 195 soul pets).
`MapData.dat` is *not* the table container: the whole file is XOR-ed with the 4 bytes `25 a8 7e 91`. Decoded, it is a list of spawn groups with
text-named properties (`NpcIdList` as u64 ids, `Positions` = x,y,z as the upper five bytes of a little-endian double + 3 zero bytes) in the same
world units as the position in the spawn frame 0x4136. Checked against 6,151 spawns from recordings: median distance to the nearest map point under
a metre in the open-world maps. Main maps: `World/World_L/World_L_A` (Elyos), `World_D_A` (Asmodian), `Abyss_Reshanta_*`.

## Collectibles (MapData.dat + EnvObjData, 2026-10-08)
`build_gather.py <unpacked AION2/Content/Data/Map> <EnvObjData.bin> <l10n dir> <out.json>` writes `Client/assets/aion2/maps/gather.json` for the
interactive map: the placements of gather sources and of the region "traces" on the three world maps. An `EnvObjData` row is `[i32 id][key]...`
with the usage string `EEnvObjectUsage::GatherSource` followed by `Gather_<Type>_...` (Od, Herb, Food, Ore, RareOre, Wood, Cotton, Gemstone);
the map files hold them as `EnvObjIdList` (u64 ids) followed by `Positions`, next to the spawn groups of `build_spawns.py`. Names come from the l10n
key `EnvObjData_<key>_desc`. Only about 740 placements exist in the map files (Verteron: Od 57, Ore 19, Herbs 13, Food 27, Gems 10, Wood 7, 559
"Empyrean traces"); Cotton, rare ores and the Od energy cubes have none there, so they are not offered.
