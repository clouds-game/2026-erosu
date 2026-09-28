from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parent.parent


class WebInteropTests(unittest.TestCase):
  def test_game0_input_adapter(self):
    node = shutil.which("node")
    compiler = ROOT / "Web/node_modules/typescript/lib/tsc.js"
    if not node or not compiler.is_file():
      self.skipTest("Web input checks require Node and npm ci --prefix Web")
    with tempfile.TemporaryDirectory() as temporary:
      directory = Path(temporary)
      compile_result = subprocess.run([
        node, str(compiler), "--target", "ES2022", "--module", "ES2022", "--lib", "ES2022,DOM", "--strict",
        "--rootDir", str(ROOT / "Web"), "--outDir", str(directory),
        str(ROOT / "Web/Interop/interop.ts"), str(ROOT / "Web/Tests/interop.test.ts"),
      ], capture_output=True, text=True)
      self.assertEqual(compile_result.returncode, 0, compile_result.stdout + compile_result.stderr)
      (directory / "package.json").write_text('{"type":"module"}\n', encoding="utf-8")
      result = subprocess.run([node, str(directory / "Tests/interop.test.js")], capture_output=True, text=True)
      self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
      self.assertIn("Game0 input adapter: 10 tests passed", result.stdout)


if __name__ == "__main__":
  unittest.main()
