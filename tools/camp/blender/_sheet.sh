#!/bin/bash
# usage: _sheet.sh name1 name2 ...  -> renders each asset preview in parallel and tiles them
PY=/tmp/claude-0/-home-user/20d69e0b-517b-5102-aeaa-07b73581abc7/scratchpad/bl/bin/python; OUT=/tmp/claude-0/-home-user/20d69e0b-517b-5102-aeaa-07b73581abc7/scratchpad/bltest
for a in "$@"; do ( $PY _preview_asset.py $a $OUT/$a.png > $OUT/$a.log 2>&1 || (echo "FAILED $a"; grep -v "^Fra\|Saved:\|^ Time\|^$\|Sample \|render   " $OUT/$a.log | tail -8) ) & done; wait
python3 - "$@" <<'PYEOF'
import sys
from PIL import Image
OUT='/tmp/claude-0/-home-user/20d69e0b-517b-5102-aeaa-07b73581abc7/scratchpad/bltest'
ims=[Image.open(f'{OUT}/{a}.png').convert('RGB') for a in sys.argv[1:]]
cols=min(len(ims),2); rows=(len(ims)+cols-1)//cols
W,H=ims[0].size
sheet=Image.new('RGB',(W*cols,H*rows))
for i,im in enumerate(ims): sheet.paste(im,((i%cols)*W,(i//cols)*H))
sheet.save(f'{OUT}/sheet.jpg',quality=88)
print('sheet', sheet.size)
PYEOF
