import unittest
from kanoonyar_ai.contracts import AssistRequest, draft_placeholder


class ContractTests(unittest.TestCase):
    def test_result_is_never_an_authoritative_decision(self):
        result = draft_placeholder(AssistRequest("draft content", "synthetic-only"))
        self.assertEqual(result.kind, "PROPOSAL")

    def test_empty_context_fails_closed(self):
        with self.assertRaises(ValueError):
            draft_placeholder(AssistRequest("draft content", ""))


if __name__ == "__main__":
    unittest.main()
