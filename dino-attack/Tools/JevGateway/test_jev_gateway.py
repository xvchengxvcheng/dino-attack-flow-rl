import json
import os
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parent))
import jev_gateway


class MockResponse:
    status = 200
    def __enter__(self): return self
    def __exit__(self, *args): pass
    def read(self, _limit):
        return json.dumps({"answers": {
            "intent": {"choice": "AutonomousDefense", "confidence": 0.91},
            "explicit_autonomy": {"noul": 0.88},
            "urgency": {"score": 2},
        }}).encode()


class GatewayContractTests(unittest.TestCase):
    def test_official_shape_and_normalization(self):
        value, error = jev_gateway.validate_payload({"text": "自主防卫", "profile": {}, "situation": {}})
        self.assertIsNone(error)
        payload = jev_gateway.official_payload(value)
        self.assertEqual(payload["model"], jev_gateway.MODEL)
        self.assertEqual(payload["questions"]["intent"]["type"], "choice")
        with patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-only"}), patch("urllib.request.urlopen", return_value=MockResponse()) as call:
            answer = jev_gateway.call_upstream(payload)
        self.assertEqual(answer["intent"], "AutonomousDefense")
        self.assertTrue(answer["explicitAutonomy"])
        request = call.call_args.args[0]
        self.assertEqual(request.full_url, jev_gateway.UPSTREAM)
        self.assertEqual(request.get_header("Authorization"), "Bearer test-only")

    def test_invalid_payload_is_rejected(self):
        _, error = jev_gateway.validate_payload({"text": ""})
        self.assertIsNotNone(error)


if __name__ == "__main__":
    unittest.main()
