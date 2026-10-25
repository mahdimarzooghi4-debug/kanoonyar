from __future__ import annotations

import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer

from stage_smoke_check import SmokeCheckError, run_smoke_check


class _FakeStageHandler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        self.server.requests.append((self.path, self.headers.get("Authorization")))
        status, headers = self.server.responses.get(self.path, (404, {}))
        self.send_response(status)
        for name, value in headers.items():
            self.send_header(name, value)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, format: str, *args: object) -> None:
        return


class StageSmokeCheckTests(unittest.TestCase):
    def _start_server(self, responses: dict[str, tuple[int, dict[str, str]]]):
        server = HTTPServer(("127.0.0.1", 0), _FakeStageHandler)
        server.responses = responses
        server.requests = []
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        return server

    def _base(self, server: HTTPServer) -> str:
        return f"http://127.0.0.1:{server.server_address[1]}"

    def _healthy_routes(self) -> dict[str, tuple[int, dict[str, str]]]:
        return {
            "/health/live": (200, {}),
            "/health/ready": (200, {}),
            "/api/v1/identity/me": (401, {}),
        }

    def test_passes_health_and_unauthenticated_identity_denial_without_credentials(self) -> None:
        server = self._start_server(self._healthy_routes())

        result = run_smoke_check(self._base(server), allow_http_loopback_for_tests=True)

        self.assertEqual(
            {"liveness": 200, "readiness": 200, "unauthenticated identity route": 401},
            result,
        )
        self.assertEqual(
            [
                ("/health/live", None),
                ("/health/ready", None),
                ("/api/v1/identity/me", None),
            ],
            server.requests,
        )

    def test_fails_when_readiness_is_unhealthy(self) -> None:
        routes = self._healthy_routes()
        routes["/health/ready"] = (503, {})
        server = self._start_server(routes)

        with self.assertRaisesRegex(SmokeCheckError, "readiness returned HTTP 503"):
            run_smoke_check(self._base(server), allow_http_loopback_for_tests=True)

        self.assertEqual(["/health/live", "/health/ready"], [path for path, _ in server.requests])

    def test_fails_if_identity_route_is_accessible_without_authentication(self) -> None:
        routes = self._healthy_routes()
        routes["/api/v1/identity/me"] = (200, {})
        server = self._start_server(routes)

        with self.assertRaisesRegex(SmokeCheckError, "unauthenticated identity route returned HTTP 200"):
            run_smoke_check(self._base(server), allow_http_loopback_for_tests=True)

    def test_does_not_follow_redirects(self) -> None:
        routes = self._healthy_routes()
        routes["/health/live"] = (302, {"Location": "https://other.example.invalid/"})
        server = self._start_server(routes)

        with self.assertRaisesRegex(SmokeCheckError, "liveness returned HTTP 302"):
            run_smoke_check(self._base(server), allow_http_loopback_for_tests=True)

        self.assertEqual(["/health/live"], [path for path, _ in server.requests])

    def test_rejects_insecure_non_loopback_or_ambiguous_base_urls(self) -> None:
        for url in (
            "http://stage.example.invalid",
            "https://user:secret@stage.example.invalid",
            "https://stage.example.invalid/path?token=secret",
            "https://stage.example.invalid/#fragment",
        ):
            with self.subTest(url=url), self.assertRaises(SmokeCheckError):
                run_smoke_check(url)


if __name__ == "__main__":
    unittest.main()
