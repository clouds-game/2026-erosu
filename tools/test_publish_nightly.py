import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import publish_nightly


class NightlyTests(unittest.TestCase):
  def setUp(self):
    self.temporary = tempfile.TemporaryDirectory()
    self.addCleanup(self.temporary.cleanup)
    self.directory = Path(self.temporary.name)
    for platform in ["windows-x64", "macos-universal"]:
      archive = self.directory / f"chroma-drop-{platform}.zip"
      archive.write_bytes(b"test package")
      archive.with_suffix(".zip.sha256").write_text(f"{hashlib.sha256(archive.read_bytes()).hexdigest()}  {archive.name}\n")
    self.environment = {"GITHUB_REPOSITORY": "example/game", "GITHUB_SHA": "a" * 40,
      "GITHUB_RUN_NUMBER": "10", "GITHUB_RUN_ID": "123"}

  def execute(self, response):
    with patch.dict(os.environ, self.environment), patch("sys.argv", ["publish_nightly.py", str(self.directory)]), \
      patch("publish_nightly.subprocess.run", return_value=response), patch("publish_nightly.gh") as command:
      publish_nightly.main()
      return command.call_args_list

  def test_corrupt_package_prevents_publication(self):
    (self.directory / "chroma-drop-windows-x64.zip").write_bytes(b"corrupt")
    with patch("publish_nightly.gh") as command, self.assertRaises(ValueError):
      publish_nightly.verify_packages(self.directory)
    command.assert_not_called()

  def test_first_release_is_prerelease_with_both_platforms(self):
    calls = self.execute(subprocess.CompletedProcess([], 1, "", "HTTP 404"))
    args = calls[0].args
    self.assertEqual(args[:3], ("release", "create", "nightly"))
    self.assertIn("--prerelease", args)
    self.assertIn("--latest=false", args)
    self.assertIn(str(self.directory / "chroma-drop-macos-universal.zip"), args)
    self.assertIn(str(self.directory / "chroma-drop-windows-x64.zip"), args)
    self.assertEqual(json.loads((self.directory / "build-info.json").read_text())["commit"], "a" * 40)

  def test_older_rerun_does_not_mutate_release(self):
    calls = self.execute(subprocess.CompletedProcess([], 0, json.dumps({"body": "<!-- nightly-run: 11 -->"}), ""))
    self.assertEqual(calls, [])

  def test_update_replaces_assets_then_moves_only_nightly_tag(self):
    calls = self.execute(subprocess.CompletedProcess([], 0, json.dumps({"body": "<!-- nightly-run: 9 -->"}), ""))
    self.assertEqual(calls[0].args[:3], ("release", "upload", "nightly"))
    self.assertIn("--clobber", calls[0].args)
    self.assertIn("repos/example/game/git/refs/tags/nightly", calls[1].args)
    self.assertEqual(calls[2].args[:3], ("release", "edit", "nightly"))

  def test_authentication_error_is_not_treated_as_missing_release(self):
    with self.assertRaises(RuntimeError):
      self.execute(subprocess.CompletedProcess([], 1, "", "HTTP 403"))


if __name__ == "__main__":
  unittest.main()
