"""Builds Client/assets/aion2/pets/pets.json: the pets of the pet window, their names, and which monsters belong to which pet.

Usage:  python3 build_pets.py <VehicleList.bin> <NpcData.bin> <npc_names.json> <l10n dir> <out.json> [extra.json] [Item.bin]

VehicleList.bin and NpcData.bin are the decrypted tables (dectable.py, from pakchunk401000-Windows_0_P.pak), the l10n dir is the
output of extract_l10n.py. A pet row is `[i32 petId][str_veh_<key>]...`; a monster row is `[i32 npcId]...[MOB_<model>]`. The game
names a pet after the model of the monsters that drop its soul (Cherubim_02 <- MOB_Cherubim_02), which was checked against
1,022 soul drops in real captures (59 of 63 souls: the monster killed right before the drop had exactly that model). The few
that do not follow the rule live in `extra.json` ({"models": {"<pet key>": "<model>"}, "npcs": {"<npc id>": "<pet key>"}}).
"""
import bisect
import json
import struct
import sys

KEY = bytes([0x25, 0, 0xA8, 0, 0x7E, 0, 0x91, 0])
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU"}


def strings(b):
    """The XOR-ed UTF-16 FStrings of a table: (offset of the length field, text)."""
    out, i = [], 0
    while i < len(b) - 8:
        n = struct.unpack_from("<i", b, i)[0]
        if -90 <= n <= -2:
            raw = b[i + 4:i + 4 + 2 * -n]
            if len(raw) == 2 * -n and raw[-2:] == b"\0\0":
                text = bytes(raw[k] ^ KEY[k % 8] for k in range(len(raw)))[:-2]
                try:
                    s = text.decode("utf-16-le")
                except UnicodeDecodeError:
                    s = ""
                if s and s.isprintable():
                    out.append((i, s))
                    i += 4 + 2 * -n
                    continue
        i += 1
    return out


def main():
    vehicle_path, npc_path, npc_names_path, l10n_dir, out_path = sys.argv[1:6]
    extra = json.load(open(sys.argv[6])) if len(sys.argv) > 6 else {}
    vehicles = open(vehicle_path, "rb").read()
    pets = {}
    for offset, s in strings(vehicles):
        if s.startswith("str_veh_"):
            pets[struct.unpack_from("<I", vehicles, offset - 4)[0]] = s[len("str_veh_"):]

    l10n = {lang: json.load(open(f"{l10n_dir}/l10n_{code}.json", encoding="utf-8")) for lang, code in LANGS.items()}
    out_pets = {}
    for pet_id, key in sorted(pets.items()):
        names = {}
        for lang, table in l10n.items():
            text = table.get(f"String_STR_ITEM_VEHICLE_{key.upper()}_A_01_B_body")
            if text:
                names[lang] = text.split(":", 1)[1].strip() if ":" in text else text
        out_pets[str(pet_id)] = {"key": key, "names": names}

    npcs = open(npc_path, "rb").read()
    table = strings(npcs)
    offsets = [o for o, _ in table]
    by_key = {key.lower(): pet_id for pet_id, key in pets.items()}
    models = {k.lower(): v for k, v in extra.get("models", {}).items()}
    # a pet may be reached by a differently spelled model (Beritra03 <- BeritraC_03)
    wanted = {models.get(key.lower(), key).lower(): pet_id for key, pet_id in ((k, by_key[k.lower()]) for k in pets.values())}
    # every monster the meter names: find its id in the table, the model is one of the next strings of that row
    monsters = {}
    for npc_id in json.load(open(npc_names_path)):
        needle = struct.pack("<i", int(npc_id))
        at = npcs.find(needle)
        while at != -1:
            first = bisect.bisect_right(offsets, at)
            row = table[first:first + 6]
            if row and row[0][0] - at <= 16:
                model = next((t[4:] for _, t in row if t.startswith("MOB_")), None)
                if model:
                    if model.lower() in wanted:
                        monsters[npc_id] = wanted[model.lower()]
                    break
            at = npcs.find(needle, at + 1)
    for npc_id, key in extra.get("npcs", {}).items():
        monsters[npc_id] = by_key[key.lower()]
    # the soul items ("VehicleSoul_<key>_A_01_b", what a monster drops of a pet) by item id, from the Item table
    souls = {}
    if len(sys.argv) > 7:
        items = open(sys.argv[7], "rb").read()
        for offset, s in strings(items):
            if s.startswith("VehicleSoul_") and s.endswith("_A_01_b") and s[12:-7].lower() in by_key:
                souls[str(struct.unpack_from("<I", items, offset - 4)[0])] = by_key[s[12:-7].lower()]
    json.dump({"pets": out_pets, "monsters": monsters, "souls": souls}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
    print(f"{len(out_pets)} pets, {len(monsters)} monsters of {len(set(monsters.values()))} pets, {len(souls)} souls")


if __name__ == "__main__":
    main()
