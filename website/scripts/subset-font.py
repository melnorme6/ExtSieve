"""Rebuild the local Inter Latin webfont with FontTools 4.66.1 and Brotli 1.2.0."""
from hashlib import sha256
from pathlib import Path
import fontTools
import brotli
from fontTools import subset
from fontTools.ttLib import TTFont

root = Path(__file__).resolve().parent.parent / "public" / "fonts"
assert fontTools.__version__ == "4.66.1" and brotli.__version__ == "1.2.0"
source = root / "InterVariable.woff2"
assert sha256(source.read_bytes()).hexdigest() == "693b77d4f32ee9b8bfc995589b5fad5e99adf2832738661f5402f9978429a8e3"
ranges = "0-FF,131,152-153,2BB-2BC,2C6,2DA,2DC,300-36F,1E9E,2000-206F,20AC,2122,2190-2199,2212,FEFF,FFFD"
options = subset.Options()
# Keep standard shaping and the stylesheet's tabular-number feature.
# Unused optional stylistic sets remain available in the complete upstream font.
options.layout_features = ["ccmp", "locl", "rlig", "liga", "clig", "calt", "kern", "mark", "mkmk", "tnum", "rvrn"]
options.name_IDs = ["*"]
options.name_languages = ["*"]
options.name_legacy = True
options.recalc_timestamp = False
options.flavor = "woff2"
font = TTFont(source, recalcTimestamp=False)
processor = subset.Subsetter(options=options)
processor.populate(unicodes=subset.parse_unicodes(ranges))
processor.subset(font)
destination = root / "InterVariable-latin.woff2"
font.save(destination)
print(destination.name, destination.stat().st_size, sha256(destination.read_bytes()).hexdigest())
