"""The opcodes the project knows or has at least described: every 0xNNNN in docs/protocol/*.md, in the frame decoder's source and in assets/aion2/protocol/opcodes.json.
The recorder with debug points (aion2-record <out> --points) beeps for an opcode that is NOT in this list. Writes Client/assets/aion2/protocol/known_opcodes.json.
Run again after a protocol document changed: python -I Tools/aion2-dat/build_known_opcodes.py"""
import glob
import json
import re

root = __file__.replace("\\", "/").rsplit("/Tools/", 1)[0]
known = set()
for path in glob.glob(root + "/docs/protocol/*.md") + [root + "/Client/Aion2/Protocol/Aion2FrameDecoder.cs", root + "/Client/Aion2/Aion2Tickets.cs", root + "/Client/Aion2/Protocol/Aion2Protocol.cs"]:
    for m in re.finditer(r"0x([0-9a-fA-F]{4})\b", open(path, encoding="utf-8").read()):
        known.add(int(m.group(1), 16))
# the numbers of the shipped opcode families and the frames the decoder passes over knowingly (decimal in the file)
def walk(x):
    if isinstance(x, int) and 0 < x < 65536:
        known.add(x)
    elif isinstance(x, list):
        for y in x:
            walk(y)
    elif isinstance(x, dict):
        for y in x.values():
            walk(y)
opcodes = json.load(open(root + "/Client/assets/aion2/protocol/opcodes.json", encoding="utf-8"))
walk(opcodes.get("opcodes", {}))
walk(opcodes.get("syncOpcodes", []))
out = root + "/Client/assets/aion2/protocol/known_opcodes.json"
json.dump(sorted(known), open(out, "w"), separators=(",", ":"))
print(len(known), "known opcodes ->", out)
