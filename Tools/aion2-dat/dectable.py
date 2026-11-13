"""Decrypts one version-13 data table (AION2/Content/Data/Table/<Name>.dat) to its raw body.
Usage: python3 dectable.py <manifest> <table.dat> <out.bin>   (seed = hash of the table name, header kind 1 or 2)
Works with the plain and the versioned (client 2.0.11+) key manifest, see aion2dat.py."""
import json, os, sys
from aion2dat import Aion2Dat
here = os.path.dirname(os.path.abspath(__file__))
c = json.load(open(os.path.join(here, "constants.local.json")))
dat = Aion2Dat(bytes.fromhex(c["manifest_hash_key_hex"]), int(c["header_xor_constant"], 16))
dat.load_manifest(open(sys.argv[1], "rb").read())
name = os.path.splitext(os.path.basename(sys.argv[2]))[0]
body = dat.read_table(open(sys.argv[2], "rb").read(), name)
open(sys.argv[3], "wb").write(body)
print(name, "ok", len(body))
