import pathlib
import tempfile
import time
import unittest
from unittest.mock import patch

import peer_session


class PeerConnectionCodeTests(unittest.TestCase):
    def setUp(self):
        self.key = bytes(range(16))

    def test_mr1_code_round_trip(self):
        code = peer_session.encode_code("26.10.20.30", 8766, self.key)
        endpoint, key, relay = peer_session.decode_connection_code(code)
        self.assertEqual(endpoint, ("26.10.20.30", 8766))
        self.assertEqual(key, self.key)
        self.assertIsNone(relay)

    def test_autopunch_and_mr2_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "AutoPunch"):
            peer_session.encode_code("26.10.20.30", 8766, self.key, relay=("26.10.20.31", 14763))
        with self.assertRaisesRegex(ValueError, "MR1"):
            peer_session.decode_connection_code("MR2-INVALID")

    def test_invalid_prefix_is_rejected(self):
        with self.assertRaises(ValueError):
            peer_session.decode_connection_code("XX1-INVALID")

    def test_tampered_checksum_is_rejected(self):
        code = peer_session.encode_code("26.10.20.30", 8766, self.key)
        replacement = "A" if code[4] != "A" else "B"
        tampered = code[:4] + replacement + code[5:]
        with self.assertRaises(ValueError):
            peer_session.decode_connection_code(tampered)


    def test_encode_rejects_invalid_endpoint_and_key(self):
        with self.assertRaises(ValueError):
            peer_session.encode_code("0.0.0.0", 8766, self.key)
        with self.assertRaises(ValueError):
            peer_session.encode_code("26.10.20.30", 0, self.key)
        with self.assertRaises(ValueError):
            peer_session.encode_code("26.10.20.30", 8766, b"short")
        with self.assertRaises(ValueError):
            peer_session.encode_code(
                "26.10.20.30", 8766, self.key, relay=("224.0.0.1", 14763)
            )

    def test_decoder_rejects_non_text_code(self):
        with self.assertRaises(ValueError):
            peer_session.decode_connection_code(None)

    def test_public_ipv4_connection_code_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Radmin VPN"):
            peer_session.encode_code("203.0.113.10", 8766, self.key)

    def test_peer_session_refuses_autopunch(self):
        with self.assertRaisesRegex(ValueError, "AutoPunch is disabled"):
            peer_session.Session(".", "host", autopunch=True)

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
            with patch("peer_session.require_radmin_ipv4", side_effect=lambda address: str(address)):
                host = peer_session.Session(
                    host_root, "host", bind="127.0.0.1", port=8766, advertise="127.0.0.1",
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
