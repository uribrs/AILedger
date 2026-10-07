import argparse
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("worker", Path(__file__).with_name("worker.py"))
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class WorkerBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.snapshot = self.root / "snapshot"
        self.snapshot.mkdir()
        self.solution = self.snapshot / "Fixture.slnx"
        self.solution.write_text('<Solution />')
        self.probe = object.__new__(worker.Worker)
        self.probe.args = argparse.Namespace(snapshot=self.snapshot)
        self.probe.initialized = True
        self.probe.digest = worker.source_digest(self.snapshot)

    def tearDown(self):
        self.temporary.cleanup()

    def load(self, path):
        self.probe.validate({"method": "tools/call", "params": {
            "name": "load_solution", "arguments": {"path": str(path)}}})

    def test_existing_snapshot_solution_is_accepted(self):
        self.load(self.solution)

    def test_existing_solution_outside_snapshot_is_refused(self):
        outside = self.root / "Outside.slnx"
        outside.write_text('<Solution />')
        with self.assertRaises(ValueError):
            self.load(outside)

    def test_prefix_sibling_does_not_count_as_snapshot_scope(self):
        sibling = self.root / "snapshot-other"
        sibling.mkdir()
        outside = sibling / "Outside.slnx"
        outside.write_text('<Solution />')
        with self.assertRaises(ValueError):
            self.load(outside)

    def test_symlink_input_is_refused(self):
        (self.snapshot / "alias.slnx").symlink_to(self.solution)
        with self.assertRaises(ValueError):
            self.load(self.snapshot / "alias.slnx")

    def test_changed_snapshot_refuses_semantic_query_instead_of_stale_results(self):
        self.solution.write_text('<Solution><Project Path="New.csproj" /></Solution>')
        with self.assertRaisesRegex(ValueError, "Snapshot changed"):
            self.probe.validate({"method": "tools/call", "params": {
                "name": "search_symbols", "arguments": {"query": "Example"}}})

    def test_non_navigation_tools_and_methods_are_refused(self):
        for request in [
            {"method": "resources/read"},
            {"method": "tools/call", "params": {"name": "execute_command"}},
            {"method": "tools/call", "params": {"name": "apply_code_action"}},
        ]:
            with self.subTest(request=request), self.assertRaises(ValueError):
                self.probe.validate(request)

    def test_second_client_cannot_reinitialize_existing_worker(self):
        with self.assertRaises(ValueError):
            self.probe.validate({"method": "initialize"})

    def test_embedded_json_result_paths_translate_back_to_snapshot(self):
        value = {"content": [{"type": "text", "text": '{"file":"/workspace/a.cs"}'}]}
        result = worker.translate(value, "/workspace", str(self.snapshot))
        self.assertIn(str(self.snapshot / "a.cs"), result["content"][0]["text"])


if __name__ == "__main__":
    unittest.main()
