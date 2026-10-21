"""Builds Client/assets/aion2/craft/{icons/*.png,item_icons.json}: the icon of every item the crafting recipes use that the item icons of the
character pages (Client/assets/aion2/items) do not cover (materials, potions, ores, gems, intermediates).

Usage:  python3 build_craft_icons.py <decoded Item.json> <recipes.json> <raw texture dir> <client assets/aion2 dir>

- Item.json is the decoded `Item` table (SemaAlter output, see Tools/aion2-dat/README.md); its `IconRes` names the texture of an item.
- recipes.json is Client/assets/aion2/craft/recipes.json (build_recipes.py).
- the raw dir holds the cooked texture packages of `UI/Resource/Texture/Item/` (`Extract <Paks dir> dump <outdir> UI/Resource/Texture/Item/`,
  see README.md); they are single-mip DXT1/DXT5 with the pixels 12 bytes before the end of the file (see convert_icons.py).
- the assets dir is Client/assets/aion2: an item whose icon is already in items/item_icons.json + items/icons is left to that folder.
Out: `craft/item_icons.json` {"<item id>": "<icon name>"} and `craft/icons/<icon name>.png` (64 px). The names the table gives are tried first; the client
also has the gear icons under a second name (Icon_GM_n_Tn_Slot = Icon_Equip_AR_L_n_Tn_Slot, Icon_WP_DA_n_Tn = Icon_Equip_WP_L_DA_n_Tn, Craft_ = Material_).
"""
import io
import json
import os
import re
import struct
import sys

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


def candidates(name):
    yield name
    m = re.match(r'Icon_GM_(\d+_T\d+_.+)$', name)
    if m:
        yield 'Icon_Equip_AR_L_' + m.group(1)
        yield 'Icon_Equip_AR_D_' + m.group(1)
    m = re.match(r'Icon_WP_([A-Z]+)_(\d+_T\d+)$', name)
    if m:
        yield f'Icon_Equip_WP_L_{m.group(1)}_{m.group(2)}'
        yield f'Icon_Equip_WP_D_{m.group(1)}_{m.group(2)}'
    if '_Craft_Skin' in name:
        yield name.replace('Craft_Skin', 'Material_Skin')
    if name.startswith('Icon_Item_Craft_'):
        yield name.replace('Icon_Item_Craft_', 'Icon_Item_Material_', 1)


def main():
    item_path, recipes_path, raw, assets = sys.argv[1:5]
    items = {x['ID']['Value']: x for x in json.load(open(item_path, encoding='utf-8'))['Properties']['Data']}
    recipes = json.load(open(recipes_path, encoding='utf-8'))['recipes']
    ids = set()
    for r in recipes:
        ids.add(r['p'])
        ids.update(c[0] for c in r['k'])
    old = json.load(open(os.path.join(assets, 'items', 'item_icons.json')))
    old_dir = os.path.join(assets, 'items', 'icons')
    out_dir = os.path.join(assets, 'craft', 'icons')
    os.makedirs(out_dir, exist_ok=True)
    available = {f[:-7] for f in os.listdir(raw) if f.endswith('.uasset')}
    mapping, covered, missing = {}, 0, []
    cache = {}
    for item in sorted(ids):
        if str(item) in old and os.path.exists(os.path.join(old_dir, old[str(item)] + '.png')):
            covered += 1
            continue
        icon = items.get(item, {}).get('IconRes')
        found = next((c for c in candidates(icon) if c in available), None) if icon else None
        if found is None:
            missing.append((item, items.get(item, {}).get('Name'), icon))
            continue
        if found not in cache:
            im = convert(os.path.join(raw, found + '.uasset'))
            cache[found] = im is not None
            if im is not None:
                im.save(os.path.join(out_dir, found + '.png'), 'PNG', optimize=True)
        if cache[found]:
            mapping[str(item)] = found
        else:
            missing.append((item, items.get(item, {}).get('Name'), icon))
    with open(os.path.join(assets, 'craft', 'item_icons.json'), 'w', encoding='utf-8') as f:
        json.dump(mapping, f, separators=(',', ':'), sort_keys=True)
    print(f'{len(ids)} items: {covered} already in items/icons, {len(mapping)} new ({len(cache)} files), {len(missing)} without a texture')
    for m in missing[:60]:
        print('  missing', m)


main()
