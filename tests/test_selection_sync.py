import json
import pathlib
import tempfile
import unittest
from types import SimpleNamespace

from selection_sync import SelectionSync


class SelectionSyncValidationTests(unittest.TestCase):
    def make_sync(self, slot=0):
        sync = SelectionSync.__new__(SelectionSync)
        sync.catalog = {"session_id": "session-1"}
        sync.catalog_hash = "catalog-hash"
        sync.slot = slot
        sync.characters = {7: {"id": 7, "outfits": [0, 1]}}
        sync.stages = {3, 4}
        sync.remote_revision = -1
        sync.remote = None
        sync.remote_owned = None
        sync.locked = False
        return sync

    @staticmethod
    def wire(revision=1, *, ready=True, stage=-1):
        return {
            "catalog": "catalog-hash",
            "revision": revision,
            "owned": {
                "choice": {"character": 7, "costume": 1},
                "stage": stage,
                "ready": ready,
            },
            "proposal": None,
            "proposal_hash": "",
            "accepted": "",
            "commit": False,
            "applied": "",
        }

    def test_accepts_valid_owned_selection(self):
        sync = self.make_sync(slot=0)
        sync.receive(self.wire())
        self.assertEqual(sync.remote_revision, 1)
        self.assertEqual(sync.remote_owned["choice"], {"character": 7, "costume": 1})

    def test_rejects_non_object_owned_payload_without_type_error(self):
        sync = self.make_sync()
        message = self.wire()
        message["owned"] = []
        with self.assertRaisesRegex(RuntimeError, "owned-slot"):
            sync.receive(message)

    def test_rejects_invalid_readiness_type(self):
        sync = self.make_sync()
        with self.assertRaisesRegex(RuntimeError, "owned-slot"):
            sync.receive(self.wire(ready=1))

    def test_ignores_older_revision(self):
        sync = self.make_sync()
        sync.receive(self.wire(revision=4))
        previous = sync.remote
        sync.receive(self.wire(revision=3, ready=False))
        self.assertIs(sync.remote, previous)
        self.assertEqual(sync.remote_revision, 4)

    def test_rejects_changed_payload_for_same_revision(self):
        sync = self.make_sync()
        sync.receive(self.wire(revision=4))
        with self.assertRaisesRegex(RuntimeError, "revision changed contents"):
            sync.receive(self.wire(revision=4, ready=False))

    def test_rejects_guest_attempt_to_select_stage(self):
        sync = self.make_sync(slot=0)
        with self.assertRaisesRegex(RuntimeError, "Guest attempted"):
            sync.receive(self.wire(stage=3))

    def test_rejects_catalog_entries_with_wrong_shapes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            (root / "selection-catalog.json").write_text(json.dumps({
                "session_id": "session-1", "local_slot": 0, "pid": 123,
                "characters": ["not-an-object"],
                "stages": [{"id": 3}],
            }), encoding="utf-8")
            sync = SelectionSync.__new__(SelectionSync)
            sync.root = root
            sync.bridge = SimpleNamespace(session="session-1", command={"target_pid": 123})
            sync.slot = 0
            with self.assertRaisesRegex(RuntimeError, "character/outfit catalog"):
                sync.load_catalog()

    def test_rejects_duplicate_outfit_ids(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            (root / "selection-catalog.json").write_text(json.dumps({
                "session_id": "session-1", "local_slot": 0, "pid": 123,
                "characters": [{"id": 7, "outfits": [0, 0]}],
                "stages": [{"id": 3}],
            }), encoding="utf-8")
            sync = SelectionSync.__new__(SelectionSync)
            sync.root = root
            sync.bridge = SimpleNamespace(session="session-1", command={"target_pid": 123})
            sync.slot = 0
            with self.assertRaisesRegex(RuntimeError, "character/outfit catalog"):
                sync.load_catalog()


if __name__ == "__main__":
    unittest.main()
