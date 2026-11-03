import pathlib
import tempfile
import time
import unittest

import peer_session


class PeerConnectionCodeTests(unittest.TestCase):
    def setUp(self):
        self.key = bytes(range(16))

    def test_mr1_code_round_trip(self):
        code = peer_session.encode_code("203.0.113.10", 27015, self.key)
        endpoint, key, relay = peer_session.decode_connection_code(code)
        self.assertEqual(endpoint, ("203.0.113.10", 27015))
        self.assertEqual(key, self.key)
        self.assertIsNone(relay)

    def test_mr2_code_round_trip(self):
        code = peer_session.encode_code(
            "198.51.100.12", 31000, self.key, relay=("192.0.2.20", 14763)
        )
        endpoint, key, relay = peer_session.decode_connection_code(code)
        self.assertEqual(endpoint, ("198.51.100.12", 31000))
        self.assertEqual(key, self.key)
        self.assertEqual(relay, ("192.0.2.20", 14763))

    def test_invalid_prefix_is_rejected(self):
        with self.assertRaises(ValueError):
            peer_session.decode_connection_code("XX1-INVALID")

    def test_tampered_checksum_is_rejected(self):
        code = peer_session.encode_code("203.0.113.10", 27015, self.key)
        replacement = "A" if code[4] != "A" else "B"
        tampered = code[:4] + replacement + code[5:]
        with self.assertRaises(ValueError):
            peer_session.decode_connection_code(tampered)


    def test_encode_rejects_invalid_endpoint_and_key(self):
        with self.assertRaises(ValueError):
            peer_session.encode_code("0.0.0.0", 27015, self.key)
        with self.assertRaises(ValueError):
            peer_session.encode_code("203.0.113.10", 0, self.key)
        with self.assertRaises(ValueError):
            peer_session.encode_code("203.0.113.10", 27015, b"short")
        with self.assertRaises(ValueError):
            peer_session.encode_code(
                "203.0.113.10", 27015, self.key, relay=("224.0.0.1", 14763)
            )

    def test_decoder_rejects_non_text_code(self):
        with self.assertRaises(ValueError):
            peer_session.decode_connection_code(None)

    def test_module_import_does_not_require_legacy_diagnostic_file(self):
        # fighter_input_test.py is not included in this repository snapshot.
        # Its absence must not prevent loading the supported live-play module.
        self.assertTrue(hasattr(peer_session, "Session"))
        self.assertTrue(hasattr(peer_session, "LiveFighter"))


class DirectUdpPeerSessionTests(unittest.TestCase):
    class FakeBridge:
        def __init__(self, root, slot, session):
            self.root = root
            self.slot = slot
            self.session = session
            self.action = None

        def begin(self, action, **extra):
            self.action = action

        def poll(self):
            if self.action == "peer_prepare":
                self.action = None
                return "peer_prepared"
            if self.action == "open_local_vs":
                self.action = None
                return "vs_ready"
            return None

    def test_mr1_udp_handshake_and_coordinated_vs_without_native_preflight(self):
        with tempfile.TemporaryDirectory() as temp:
            host_root = pathlib.Path(temp) / "host"
            guest_root = pathlib.Path(temp) / "guest"
            host = peer_session.Session(
                host_root, "host", bind="127.0.0.1", port=0, advertise="127.0.0.1",
                bridge_factory=self.FakeBridge, selection_sync=False,
                skip_native_preflight=True, setup_timeout=5,
            )
            guest = peer_session.Session(
                guest_root, "join", code=host.code, bind="127.0.0.1", port=0,
                bridge_factory=self.FakeBridge, selection_sync=False,
                skip_native_preflight=True, setup_timeout=5,
            )
            try:
                deadline = time.monotonic() + 4
                while time.monotonic() < deadline:
                    host.step()
                    guest.step()
                    if host.preflight_done and guest.preflight_done:
                        break
                    time.sleep(0.001)

                self.assertTrue(host.remote_nonce)
                self.assertTrue(guest.remote_nonce)
                self.assertEqual(host.peer, ("127.0.0.1", guest.logical_port))
                self.assertEqual(guest.peer, host.advertised_endpoint)
                self.assertTrue(host.vs_ready)
                self.assertTrue(guest.vs_ready)
                self.assertTrue(host.preflight_done)
                self.assertTrue(guest.preflight_done)
                self.assertIsNone(host.native)
                self.assertIsNone(guest.native)
            finally:
                host.peer = None
                guest.peer = None
                host.close()
                guest.close()


if __name__ == "__main__":
    unittest.main()
