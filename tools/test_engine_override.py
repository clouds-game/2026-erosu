"""独立 Python 进程可使用冻结副本，不依赖默认构建路径。"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from AI.engine import ENGINE


class EngineOverrideTests(unittest.TestCase):
  def test_frozen_engine_override(self):
    with tempfile.TemporaryDirectory() as folder:
      target = Path(folder) / ENGINE.name
      for suffix in ('.dll', '.deps.json', '.runtimeconfig.json'):
        source = ENGINE.with_name(ENGINE.stem + suffix)
        shutil.copy2(source, Path(folder) / source.name)
      code = """import json
from AI.engine import Engine, ENGINE
with Engine() as engine:
  reply = engine.command('start', mode='endless', seed=17)
  print(json.dumps({'path': str(ENGINE), 'ok': reply['ok'], 'locked': reply['state']['locked']}))
"""
      result = subprocess.run([sys.executable, '-c', code], cwd=ROOT,
        env={**os.environ, 'CHROMA_ENGINE': str(target)}, capture_output=True, text=True, timeout=30)
      self.assertEqual(0, result.returncode, result.stderr)
      self.assertEqual({'path': str(target.resolve()), 'ok': True, 'locked': 0}, json.loads(result.stdout))


if __name__ == '__main__':
  unittest.main()
