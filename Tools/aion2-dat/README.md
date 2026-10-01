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
