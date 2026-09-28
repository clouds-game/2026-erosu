import copy
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import pages_site


class PagesSiteTests(unittest.TestCase):
  def setUp(self):
    self.temporary = tempfile.TemporaryDirectory()
    self.addCleanup(self.temporary.cleanup)
    self.directory = Path(self.temporary.name)
    self.artifacts = self.directory / "artifacts"
    self.selector = self.directory / "selector"
    self.selector.mkdir()
    for name in ["index.html", "selector.css", "selector.js"]:
      (self.selector / name).write_text(name, encoding="utf-8")
    self.selector_patch = patch.object(pages_site, "SELECTOR_DIRECTORY", self.selector)
    self.selector_patch.start()
    self.addCleanup(self.selector_patch.stop)
    self.output = self.directory / "site"
    self.snapshot = {
      "repository": "example/game",
      "branches": [
        {"name": name, "commit": str(index + 1) * 40, "path": name + "/"}
        for index, name in enumerate(["main", "develop20260926", "experiment1"])
      ],
    }

  def prepare_builds(self, snapshot=None):
    snapshot = snapshot or self.snapshot
    for branch in snapshot["branches"]:
      directory = self.artifacts / ("pages-web-" + branch["name"])
      for name in pages_site.REQUIRED_BUILD_FILES:
        path = directory / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(branch["name"], encoding="utf-8")
      base_path = pages_site.branch_base_path(snapshot["repository"], branch["name"])
      (directory / "index.html").write_text(f'<html><head><base href="{base_path}" /></head></html>', encoding="utf-8")
      (directory / "extra" / "nested").mkdir(parents=True, exist_ok=True)
      (directory / "extra" / "nested" / "game.wasm").write_bytes(b"\x00asm")

  def assemble(self, snapshot=None):
    pages_site.assemble(snapshot or self.snapshot, self.artifacts, self.selector / "selector.js", self.output)

  def site_files(self):
    return {str(path.relative_to(self.output)): path.read_bytes() for path in self.output.rglob("*") if path.is_file()}

  def remote_response(self, branches):
    return subprocess.CompletedProcess([], 0, "".join(f"{branch['commit']}\trefs/heads/{branch['name']}\n" for branch in branches), "")

  def test_snapshot_captures_exact_heads_in_config_order(self):
    config = self.directory / "branches.json"
    config.write_text(json.dumps([branch["name"] for branch in self.snapshot["branches"]]))
    with patch("pages_site.subprocess.run", return_value=self.remote_response(self.snapshot["branches"])) as run, \
      patch("pages_site.repository_name", return_value="example/game"):
      self.assertEqual(pages_site.create_snapshot(config), self.snapshot)
    self.assertEqual(run.call_args.args[0], ["git", "ls-remote", "--heads", "origin",
      "refs/heads/main", "refs/heads/develop20260926", "refs/heads/experiment1"])

  def test_missing_remote_head_does_not_write_snapshot(self):
    output = self.directory / "snapshot.json"
    output.write_text("previous snapshot", encoding="utf-8")
    with patch("pages_site.subprocess.run", return_value=self.remote_response(self.snapshot["branches"][:2])), \
      patch("sys.stderr", new_callable=io.StringIO), self.assertRaises(SystemExit) as error:
      pages_site.main(["snapshot", "--output", str(output)])
    self.assertEqual(error.exception.code, 1)
    self.assertEqual(output.read_text(), "previous snapshot")

  def test_malformed_names_commits_and_paths_are_rejected(self):
    for name in ["../main", "main/child", ".", "..", "-main", "main..old", "main.lock", "index.html", "main\n"]:
      with self.subTest(name=name), self.assertRaises(ValueError):
        pages_site.validate_branch_name(name)
    for field, value in [("commit", "A" * 40), ("commit", "f" * 39), ("path", "../main/")]:
      snapshot = copy.deepcopy(self.snapshot)
      snapshot["branches"][0][field] = value
      with self.subTest(field=field, value=value), self.assertRaises(ValueError):
        self.assemble(snapshot)
    self.assertFalse(self.output.exists())

  def test_assembly_retains_every_branch_and_hidden_runtime_resources(self):
    self.prepare_builds()
    self.assemble()
    self.assertEqual(json.loads((self.output / "branches.json").read_text()), self.snapshot)
    for branch in self.snapshot["branches"]:
      directory = self.output / branch["name"]
      self.assertTrue((directory / ".nojekyll").is_file())
      self.assertEqual((directory / "extra/nested/game.wasm").read_bytes(), b"\x00asm")
      self.assertEqual(json.loads((directory / "build-info.json").read_text()),
        {"name": branch["name"], "commit": branch["commit"]})
      self.assertIn(f'base href="/game/{branch["name"]}/"', (directory / "index.html").read_text())
    self.assertTrue((self.output / ".nojekyll").is_file())
    self.assertEqual((self.output / "selector.js").read_text(), "selector.js")

  def test_game0_is_retained_with_licensed_images_and_pointer_bridge(self):
    self.snapshot["branches"].append({"name": "lingjiuu-game0", "commit": "d" * 40, "path": "lingjiuu-game0/"})
    self.prepare_builds()
    directory = self.artifacts / "pages-web-lingjiuu-game0"
    index = directory / "index.html"
    index.write_text(index.read_text().replace("</head>", '<meta name="game" content="game0" /></head>'))
    (directory / "interop.js").write_text("export function connectGame0() {}")
    for name in pages_site.GAME0_ASSET_FILES:
      path = directory / name
      path.parent.mkdir(parents=True, exist_ok=True)
      path.write_bytes(b"\x89PNG\r\n\x1a\n" if name.endswith(".png") else b"Asset source and license")
    self.assemble()
    self.assertTrue((self.output / "lingjiuu-game0/assets/Cats/SOURCE.md").is_file())
    self.assertTrue((self.output / "main/index.html").is_file())
    previous = self.site_files()
    for name in ["assets/Cats/cat-cell.png", "assets/Cats/SOURCE.md"]:
      path = directory / name
      content = path.read_bytes()
      path.unlink()
      with self.subTest(name=name), self.assertRaisesRegex(ValueError, "Missing Game0 resource"):
        self.assemble()
      self.assertEqual(self.site_files(), previous)
      path.write_bytes(content)
    (directory / "interop.js").write_text("Old game keyboard bridge")
    with self.assertRaisesRegex(ValueError, "pointer bridge"):
      self.assemble()
    self.assertEqual(self.site_files(), previous)

  def test_user_pages_has_no_repository_prefix(self):
    self.snapshot["repository"] = "Example/example.github.io"
    self.prepare_builds()
    self.assemble()
    self.assertIn('base href="/experiment1/"', (self.output / "experiment1/index.html").read_text())

  def test_missing_branch_leaves_existing_site_intact(self):
    self.prepare_builds()
    self.assemble()
    previous = self.site_files()
    (self.artifacts / "pages-web-experiment1/_framework/blazor.webassembly.js").unlink()
    with self.assertRaisesRegex(ValueError, "Missing build resource"):
      self.assemble()
    self.assertEqual(self.site_files(), previous)

  def test_copy_failure_leaves_existing_site_intact(self):
    self.prepare_builds()
    self.assemble()
    previous = self.site_files()
    original_copy = pages_site.shutil.copytree
    count = 0

    def interrupted_copy(*args, **kwargs):
      nonlocal count
      count += 1
      if count == 2:
        raise OSError("Interrupted copy")
      return original_copy(*args, **kwargs)

    with patch("pages_site.shutil.copytree", side_effect=interrupted_copy), self.assertRaises(OSError):
      self.assemble()
    self.assertEqual(self.site_files(), previous)

  def test_wrong_base_path_prevents_output_creation(self):
    self.prepare_builds()
    (self.artifacts / "pages-web-main/index.html").write_text('<base href="/" />')
    with self.assertRaisesRegex(ValueError, "base href"):
      self.assemble()
    self.assertFalse(self.output.exists())

  def test_unrelated_directory_is_never_replaced(self):
    self.prepare_builds()
    self.output.mkdir()
    (self.output / "keep.txt").write_text("unrelated data")
    with self.assertRaisesRegex(ValueError, "unrecognized nonempty"):
      self.assemble()
    self.assertEqual(self.site_files(), {"keep.txt": b"unrelated data"})

  def test_unvalidated_existing_manifest_cannot_authorize_replacement(self):
    self.prepare_builds()
    self.assemble()
    (self.output / "branches.json").write_text(json.dumps({
      "repository": "example/game",
      "branches": [{"name": "../outside", "commit": "a" * 40, "path": "../outside/"}],
    }))
    previous = self.site_files()
    with self.assertRaises(ValueError):
      self.assemble()
    self.assertEqual(self.site_files(), previous)

  def test_output_cannot_overlap_artifacts(self):
    self.prepare_builds()
    self.output = self.artifacts
    previous = self.site_files()
    with self.assertRaisesRegex(ValueError, "overlaps an input"):
      self.assemble()
    self.assertEqual(self.site_files(), previous)

  def test_existing_generated_site_can_be_replaced_with_new_heads(self):
    self.prepare_builds()
    self.assemble()
    self.snapshot["branches"][1]["commit"] = "f" * 40
    self.assemble()
    self.assertEqual(json.loads((self.output / "branches.json").read_text()), self.snapshot)
    self.assertTrue((self.output / "main/index.html").is_file())
    self.assertTrue((self.output / "experiment1/index.html").is_file())

  def test_current_guard_handles_changed_and_deleted_heads(self):
    with patch("pages_site.subprocess.run", return_value=self.remote_response(self.snapshot["branches"])):
      self.assertTrue(pages_site.is_current(self.snapshot))
    changed = copy.deepcopy(self.snapshot["branches"])
    changed[0]["commit"] = "a" * 40
    for branches in [changed, changed[:2]]:
      with self.subTest(branches=branches), patch("pages_site.subprocess.run", return_value=self.remote_response(branches)):
        self.assertFalse(pages_site.is_current(self.snapshot))

  def test_current_guard_fails_on_remote_errors(self):
    with patch("pages_site.subprocess.run", side_effect=subprocess.CalledProcessError(128, "git")), \
      self.assertRaises(subprocess.CalledProcessError):
      pages_site.is_current(self.snapshot)

  def test_stale_guard_command_returns_false_without_failing(self):
    snapshot = self.directory / "snapshot.json"
    snapshot.write_text(json.dumps(self.snapshot), encoding="utf-8")
    with patch("pages_site.subprocess.run", return_value=self.remote_response(self.snapshot["branches"][:2])), \
      patch("sys.stdout", new_callable=io.StringIO) as output:
      pages_site.main(["is-current", "--snapshot", str(snapshot)])
    self.assertEqual(output.getvalue(), "false\n")
    with patch("pages_site.subprocess.run", return_value=subprocess.CompletedProcess([], 0, "broken response", "")), \
      self.assertRaises(ValueError):
      pages_site.is_current(self.snapshot)


if __name__ == "__main__":
  unittest.main()
