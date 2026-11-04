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

    def test_invalid_prefix_is_rejected(self):
        with self.assertRaises(ValueError):
            peer_session.decode_connection_code("XX1-INVALID")

    def test_tampered_checksum_is_rejected(self):
        code = peer_session.encode_code("203.0.113.10", 27015, self.key)
        replacement = "A" if code[4] != "A" else "B"
        with self.assertRaises(ValueError):
            peer_session.decode_connection_code(code[:4] + replacement + code[5:])

    def test_peer_transport_uses_reserved_radmin_udp_port(self):
        import inspect
        self.assertEqual(inspect.signature(peer_session.Session).parameters["port"].default, 8766)

    def test_module_import_does_not_require_legacy_diagnostic_file(self):
        self.assertTrue(hasattr(peer_session, "Session"))
        self.assertTrue(hasattr(peer_session, "LiveFighter"))


if __name__ == "__main__":
    unittest.main()
