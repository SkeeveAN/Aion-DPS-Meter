"""Builds Client/assets/aion2/craft/recipes.json: every crafting recipe of the client, with item ids instead of the client's internal names.

Usage:  python3 build_recipes.py <decoded table dir> <out.json>

The table dir holds the decoded tables (`CraftRecipe.json`, `CraftComponentReplacement.json`, `Item.json`, SemaAlter output, see README). The names
and icons of the items come from `assets/aion2/items`; this file only has the ids. Out:
`{"v":1,"recipes":[{"id":111013001,"race":"L","cat":"Blacksmithing","sub":"Weapon","tab":"Sword","lv":55,"learn":"Auto","type":"Common",
"p":<product id>,"n":1,"combo":2500,"cr":<combo recipe id or 0>,"k":[[<item id>,<count>,[[<alt id>,<count>],...]]...]}]}`.
`race` is L (Elyos) or D (Asmodians): the client has one recipe per faction. `cr` is the hidden combo recipe whose product is the first component
of the recipe (a craft that has to be done first); `learn` Combo marks those hidden ones. The replacement list of a component is the alternative
material the client lets the player use instead.
"""
import json
import os
import sys


def table(path):
    return json.load(open(path, encoding="utf-8"))["Properties"]["Data"]


def main():
    src, out_path = sys.argv[1:3]
    ids = {x["Name"]: x["ID"]["Value"] for x in table(os.path.join(src, "Item.json"))}
    replace = {x["Name"]: [[ids[r["Item"]], r["Count"]] for r in x["ReplacementItemList"] if r["Item"] in ids]
               for x in table(os.path.join(src, "CraftComponentReplacement.json"))}
    rows = table(os.path.join(src, "CraftRecipe.json"))
    by_name = {x["Name"]: x["ID"]["Value"] for x in rows}
    out = []
    for x in rows:
        comps = []
        for c in x["ComponentList"]:
            if c["Item"] not in ids:
                continue
            alt = replace.get(c["ReplacementItem"], []) if c["ReplacementItem"] != "None" else []
            comps.append([ids[c["Item"]], c["Count"]] + ([alt] if alt else []))
        out.append({
            "id": x["ID"]["Value"],
            "race": "L" if x["QualificationRace"].endswith("Light") else "D",
            "cat": x["Category"].split("::")[1],
            "sub": x["SubCategory"].split("::")[1],
            "tab": "" if x["SubTab"] == "None" else x["SubTab"],
            "lv": x["MasteryLevel"],
            "learn": x["LearnType"].split("::")[1],
            "type": x["Unknown2_Name"].split("::")[1],
            "order": x["Order"],
            "p": ids[x["Product"]],
            "n": x["ProductCount"],
            "combo": x["ComboProbability"],
            "cr": by_name.get(x["ComboRecipe"], 0),
            "k": comps,
        })
    out.sort(key=lambda r: (r["cat"], r["sub"], r["tab"], r["order"], r["id"]))
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"v": 1, "recipes": out}, f, ensure_ascii=False, separators=(",", ":"))
    print(len(out), "recipes,", sum(len(r["k"]) for r in out), "components,", sum(1 for r in out for c in r["k"] if len(c) > 2), "with alternatives")


main()
