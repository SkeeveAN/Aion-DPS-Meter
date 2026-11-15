"""Builds Client/assets/aion2/maps/gather_icons.json and gather_icons/*.png: the icon of every collectible of the interactive map (what the game shows for
the item the gather source gives: Odyle, Orichalcum Ore, Yggdrasil Log, the gemstones, herbs and food plants; the "Empyrean Trace" and the Hidden Cube, whose picture build_cube_icon.py draws: the client has none, its item is a key).

Usage:  python3 build_gather_icons.py <decoded Item.json> <raw texture dir> <client assets/aion2 dir> [<item_names.json>]

The key of a collectible is its English object name (maps/gather.json). Its item is the one of that name (or "<name> Ore", "<name> Log", "<name> Gemstone"); the
item's IconRes names the texture. Textures come from the already converted icons (craft/icons, items/icons) or, where those lack one, from the raw packages dumped with
`Extract <Paks> dump <outdir> <icon name>` (single-mip DXT, see build_craft_icons.py). An item whose texture the client does not have (Food_L_c_002, _005) uses the picture the conversion menu shows (Icon_Gather_*);
the map falls back to the coloured diamond for anything without an icon.
"""
import json
import os
import re
import shutil
import sys

import io
import struct

from PIL import Image

SIZE = 64


def dds(w, h, fourcc, data):
    hdr = struct.pack('<4sIIIIIII44sII4sIIIII', b'DDS ', 124, 0x81007, h, w, len(data), 0, 1, b'\0' * 44, 32, 4, fourcc, 0, 0, 0, 0, 0)
    return hdr + struct.pack('<IIIII', 0x1000, 0, 0, 0, 0) + data


def convert(path):
    d = open(path, 'rb').read()
    i = d.rfind(b'PF_')
    j = d.find(b'\0', i)
    fmt = d[i:j].decode()
    w, h, _ = struct.unpack('<iii', d[i - 16:i - 4])
    if fmt == 'PF_DXT5':
        n, fc = w * h, b'DXT5'
    elif fmt == 'PF_DXT1':
        n, fc = w * h // 2, b'DXT1'
    else:
        return None
    im = Image.open(io.BytesIO(dds(w, h, fc, d[len(d) - n - 12:len(d) - 12]))).convert('RGBA')
    return im.resize((SIZE, SIZE), Image.LANCZOS)




# the two food items whose Icon_Item texture the client lacks show the Icon_Gather_* picture in the conversion menu (dump those two next to the others first)
STAND_IN = {"Icon_Item_Gather_Food_L_c_002": "Icon_Gather_Fruit_L_lv2_01a", "Icon_Item_Gather_Food_L_c_005": "Icon_Gather_Food_L_lv1_01c"}
SPECIAL = {"Hidden Cube": "HiddenCube", "Empyrean Trace: Verteron": "Icon_Item_Monolith_Material_L_001"}


def main():
    item_path, raw, assets = sys.argv[1:4]
    names_path = sys.argv[4] if len(sys.argv) > 4 else os.path.join(assets, 'items', 'item_names.json')
    items = {x['ID']['Value']: x for x in json.load(open(item_path, encoding='utf-8'))['Properties']['Data']}
    names = json.load(open(names_path, encoding='utf-8'))
    by_name = {}
    for k, v in names.items():
        by_name.setdefault(v[1].lower(), []).append(int(k))
    objects = json.load(open(os.path.join(assets, 'maps', 'gather.json'), encoding='utf-8'))['objects']
    keys = sorted({o['names']['en'] for o in objects.values()})
    out_dir = os.path.join(assets, 'maps', 'gather_icons')
    os.makedirs(out_dir, exist_ok=True)
    existing = {}
    for folder in ('craft/icons', 'items/icons'):
        d = os.path.join(assets, *folder.split('/'))
        for f in os.listdir(d):
            existing[f[:-4]] = os.path.join(d, f)
    mapping = {}
    for key in keys:
        icon = SPECIAL.get(key)
        if icon is None:
            for cand in (key, key + ' Ore', key + ' Log', key + ' Gemstone'):
                ids = [i for i in by_name.get(cand.lower(), []) if i in items and items[i].get('IconRes') not in (None, 'None')]
                if ids:
                    icon = items[min(ids)]['IconRes']
                    break
        if icon is None:
            continue
        use = icon if (icon in existing or os.path.exists(os.path.join(raw, icon + '.uasset')) or os.path.exists(os.path.join(out_dir, icon + '.png'))) else STAND_IN.get(icon)
        if use is None:
            continue
        target = os.path.join(out_dir, use + '.png')
        if not os.path.exists(target):
            if use in existing:
                shutil.copyfile(existing[use], target)
            else:
                im = convert(os.path.join(raw, use + '.uasset'))
                if im is None:
                    continue
                im.save(target, 'PNG', optimize=True)
        mapping[key] = use
        print(f'{key:28} -> {use}' + ('' if use == icon else f'   (stand-in for {icon})'))
    with open(os.path.join(assets, 'maps', 'gather_icons.json'), 'w', encoding='utf-8') as f:
        json.dump(mapping, f, separators=(',', ':'), sort_keys=True, ensure_ascii=False)
    print(len(mapping), 'of', len(keys), 'collectibles have an icon')


if __name__ == "__main__":
    main()
