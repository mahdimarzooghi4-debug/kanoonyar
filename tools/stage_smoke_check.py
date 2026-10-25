#!/usr/bin/env python3
"""Read-only smoke checks for an already deployed KanoonYar Stage API."""

from __future__ import annotations

import argparse
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, OpenerDirector, Request, build_opener


class SmokeCheckError(Exception):
    """A Stage endpoint did not satisfy the expected smoke-check contract."""


class _RejectRedirects(HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None


def _validated_base_url(base_url: str, *, allow_http_loopback_for_tests: bool = False) -> str:
    if not base_url or base_url != base_url.strip() or any(ord(char) < 32 for char in base_url):
        raise SmokeCheckError("base URL is malformed")

    try:
        parsed = urlsplit(base_url)
        hostname = parsed.hostname
        _ = parsed.port
    except ValueError as exc:
        raise SmokeCheckError("base URL is malformed") from exc

    if not hostname or parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise SmokeCheckError("base URL must not include credentials, query, or fragment")

    secure = parsed.scheme.lower() == "https"
    loopback_test = (
        allow_http_loopback_for_tests
        and parsed.scheme.lower() == "http"
        and hostname.lower() in {"localhost", "127.0.0.1", "::1"}
    )
    if not secure and not loopback_test:
        raise SmokeCheckError("base URL must use HTTPS")

    return base_url.rstrip("/")


def _read_status(opener: OpenerDirector, url: str, path: str, timeout: float) -> int:
    request = Request(url, method="GET", headers={"Accept": "application/json"})
    try:
        response = opener.open(request, timeout=timeout)
    except HTTPError as response:
        status = response.code
        response.close()
        return status
    except URLError as exc:
        reason = type(exc.reason).__name__
        raise SmokeCheckError(f"{path} could not be reached ({reason})") from None

    try:
        return response.getcode()
    finally:
        response.close()


def run_smoke_check(
    base_url: str,
    *,
    timeout: float = 5.0,
    allow_http_loopback_for_tests: bool = False,
) -> dict[str, int]:
    """Check liveness, readiness, and unauthenticated identity denial without sending credentials."""
    base = _validated_base_url(base_url, allow_http_loopback_for_tests=allow_http_loopback_for_tests)
    if timeout <= 0:
        raise SmokeCheckError("timeout must be positive")

    opener = build_opener(_RejectRedirects())
    expected = (
        ("/health/live", 200, "liveness"),
        ("/health/ready", 200, "readiness"),
        ("/api/v1/identity/me", 401, "unauthenticated identity route"),
    )
    results: dict[str, int] = {}
    for path, expected_status, label in expected:
        status = _read_status(opener, f"{base}{path}", path, timeout)
        if status != expected_status:
            raise SmokeCheckError(f"{label} returned HTTP {status}; expected HTTP {expected_status}")
        results[label] = status
    return results


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage_api_base_url", help="HTTPS base URL of the already deployed Stage API")
    parser.add_argument("--timeout", type=float, default=5.0, help="per-request timeout in seconds")
    args = parser.parse_args()

    try:
        result = run_smoke_check(args.stage_api_base_url, timeout=args.timeout)
    except SmokeCheckError as exc:
        print(f"Stage smoke check failed: {exc}", file=sys.stderr)
        return 1

    print(
        "Stage smoke check passed: "
        f"liveness={result['liveness']}, readiness={result['readiness']}, "
        f"unauthenticated_identity={result['unauthenticated identity route']}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
