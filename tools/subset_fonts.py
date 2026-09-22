"""使用 fonttools 从 Noto CJK Sans 2.004 字体生成 UI 子集：python tools/subset_fonts.py .local/fonts。"""
import json
from pathlib import Path
import sys
from fontTools import subset
from fontTools.ttLib import TTFont

root = Path(__file__).resolve().parents[1]
catalog = json.loads((root / 'Localization/strings.json').read_text())
text = ''.join(value for language in catalog.values() for value in language.values())
text += 'English简体中文日本語中文 EN / ←→↑↓↻×·' + ''.join(chr(code) for code in range(32, 127))
for source, name in [('sc', 'ChromaUI-SC'), ('jp', 'ChromaUI-JP')]:
  font = TTFont(Path(sys.argv[1]) / f'{source}.otf')
  options = subset.Options()
  options.name_IDs = ['*']
  required = set(''.join(value for language in catalog.values() for value in language.values()))
  missing = required - set(map(chr, font.getBestCmap()))
  if missing:
    raise ValueError(f'Missing translated characters: {sorted(missing)}')
  subsetter = subset.Subsetter(options=options)
  subsetter.populate(text=text)
  subsetter.subset(font)
  # 子集使用独立名称，保留版权与许可证字段。
  for record in font['name'].names:
    if record.nameID in (1, 3, 4, 6, 16):
      record.string = name.encode(record.getEncoding())
  font.save(root / f'Assets/Fonts/{name}.otf')
