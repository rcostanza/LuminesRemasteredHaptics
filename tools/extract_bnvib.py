"""Extract TextAssets from Unity bundles. Usage: extract_bnvib.py <bundleDir> <outDir> [glob]"""
import sys, pathlib, UnityPy
src, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
pat = sys.argv[3] if len(sys.argv) > 3 else "skin*haptic*"
n = 0
for f in sorted(src.glob(pat)):
    if f.suffix == ".manifest" or f.name.endswith(".manifest"):
        continue
    env = UnityPy.load(str(f))
    for path, obj in env.container.items():
        if obj.type.name != "TextAsset":
            continue
        data = obj.read()
        raw = data.m_Script
        if isinstance(raw, str):
            raw = raw.encode("utf-8", "surrogateescape")
        dst = out / f.name / pathlib.PurePosixPath(path).name
        dst.parent.mkdir(parents=True, exist_ok=True)
        dst.write_bytes(raw)
        n += 1
print(n, "assets")
