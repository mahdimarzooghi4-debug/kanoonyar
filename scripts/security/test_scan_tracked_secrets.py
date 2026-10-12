import importlib.util
import pathlib
import unittest

SOURCE = pathlib.Path(__file__).with_name("scan_tracked_secrets.py")
spec = importlib.util.spec_from_file_location("secret_gate", SOURCE)
scanner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scanner)


class SecretPatternTests(unittest.TestCase):
    def test_private_key_header_rejected(self):
        self.assertIn("private-key", scanner.scan("-----BEGIN " + "PRIVATE KEY-----"))

    def test_github_token_rejected(self):
        self.assertIn("github-token", scanner.scan("ghp_" + "A" * 36))

    def test_aws_key_rejected(self):
        self.assertIn("aws-access-key", scanner.scan("AKIA" + "A" * 16))

    def test_normal_configuration_is_allowed(self):
        self.assertEqual([], scanner.scan("OIDC_CLIENT_ID=example-public-client"))


if __name__ == "__main__":
    unittest.main()
