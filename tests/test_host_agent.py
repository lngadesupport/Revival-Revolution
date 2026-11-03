import unittest
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

from ranked_server import host_agent


class HostAgentTests(unittest.TestCase):
    @patch("ranked_server.host_agent.subprocess.run")
    def test_detects_configured_radmin_address(self, run):
        run.return_value = SimpleNamespace(stdout="26.226.57.19\r\n", returncode=0)
        self.assertTrue(host_agent.has_local_address("26.226.57.19"))

    @patch("ranked_server.host_agent.subprocess.run")
    def test_does_not_match_another_radmin_address(self, run):
        run.return_value = SimpleNamespace(stdout="26.6.193.168\r\n", returncode=0)
        self.assertFalse(host_agent.has_local_address("26.226.57.19"))

    @patch("ranked_server.host_agent.urlopen")
    def test_health_check_accepts_http_200(self, open_url):
        response = MagicMock()
        response.status = 200
        open_url.return_value.__enter__.return_value = response
        self.assertTrue(host_agent.healthy("26.226.57.19", 8765))

    @patch("ranked_server.host_agent.urlopen", side_effect=OSError("offline"))
    def test_health_check_reports_unavailable_server(self, open_url):
        self.assertFalse(host_agent.healthy("26.226.57.19", 8765))


if __name__ == "__main__":
    unittest.main()
