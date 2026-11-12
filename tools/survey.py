import sys, pathlib, numpy as np
sys.path.insert(0, str(pathlib.Path(__file__).parent)); import bnvib
fs = [bnvib.load(p) for p in pathlib.Path(sys.argv[1]).rglob("*.bnvib.bytes")]
F = np.concatenate([f.frames for f in fs])
act_l, act_h = F[:,0] > 0, F[:,2] > 0
fl, fh = 10*2**(F[act_l,1]/32), 10*2**(F[act_h,3]/32)
print("files", len(fs), "frames", len(F), "durations s: min %.2f max %.2f" % (min(f.duration for f in fs), max(f.duration for f in fs)))
print("low  freq Hz (active) pct 1/50/99:", np.percentile(fl,[1,50,99]).round(1), "min/max", fl.min().round(1), fl.max().round(1))
print("high freq Hz (active) pct 1/50/99:", np.percentile(fh,[1,50,99]).round(1), "min/max", fh.min().round(1), fh.max().round(1))
print("amp low max", F[:,0].max()/255, "amp high max", F[:,2].max()/255, " max(aL+aH)", ((F[:,0].astype(int)+F[:,2])/255).max())
print("loops:", sum(f.loop_start is not None for f in fs))
