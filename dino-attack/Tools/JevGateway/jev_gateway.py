#!/usr/bin/env python3
"""Small localhost Jev gateway for Dino Attack.

It accepts a deliberately narrow game contract and translates it to TypeSafe's
System One API. The key is read only from TYPESAFE_API_KEY and is never logged
or persisted. No upstream call is made at process startup.
"""
from __future__ import annotations
import json
import os
import re
import sys
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

HOST = "127.0.0.1"
PORT = int(os.environ.get("JEV_GATEWAY_PORT", "8765"))
UPSTREAM = os.environ.get("TYPESAFE_JEV_URL", "https://api.typesafe.ai/v1/systemone")
MODEL = os.environ.get("TYPESAFE_JEV_MODEL", "jev-latest")
MAX_BYTES = 64 * 1024
MODES = {"FollowPlayer", "AutonomousDefense", "HoldPosition", "ProtectPlayer", "Regroup"}


def local_intent(text: str) -> dict[str, Any]:
    """Conservative fallback for offline play; no arbitrary action is accepted."""
    value = text.strip()
    negative_leave = any(x in value for x in ("不要离开", "别离开", "不要走", "别走"))
    negative_follow = any(x in value for x in ("不要跟", "别跟", "不必跟"))
    autonomy = any(x in value for x in ("自主", "自己判断", "自由行动", "你决定", "自行防卫"))
    protect = any(x in value for x in ("保护我", "护着我", "拦住攻击", "优先救我"))
    follow = any(x in value for x in ("待在我身边", "在我身边", "跟着我", "跟我", "靠近我", "跟随"))
    hold = any(x in value for x in ("守住", "守点", "原地", "待在这里", "坚守", "留守"))
    defense = any(x in value for x in ("自主防卫", "自己防守", "离开", "村口", "防守村庄", "独立防守")) and not negative_leave
    if negative_follow and not defense:
        mode, confidence, explicit = "AutonomousDefense", .84, True
    elif negative_leave or protect or follow:
        mode, confidence, explicit = ("ProtectPlayer" if protect else "FollowPlayer"), .9, False
    elif defense or autonomy:
        mode, confidence, explicit = "AutonomousDefense", (.93 if defense and autonomy else .78), autonomy
    elif hold:
        mode, confidence, explicit = "HoldPosition", .84, False
    else:
        mode, confidence, explicit = "FollowPlayer", .18, False
    return {"intent": mode, "confidence": confidence, "explicitAutonomy": explicit,
            "needsClarification": not bool(defense or autonomy or protect or follow or hold or negative_leave or negative_follow),
            "tacticalUrgency": .85 if any(x in value for x in ("立刻", "马上", "紧急", "危险")) else .35}


def validate_payload(value: Any) -> tuple[dict[str, Any] | None, str | None]:
    if not isinstance(value, dict):
        return None, "body must be an object"
    text = value.get("text")
    if not isinstance(text, str) or not (1 <= len(text.strip()) <= 2000):
        return None, "text must be a non-empty string up to 2000 characters"
    profile = value.get("profile", {})
    situation = value.get("situation", {})
    if not isinstance(profile, dict) or not isinstance(situation, dict):
        return None, "profile and situation must be objects"
    return {"text": text.strip(), "profile": profile, "situation": situation}, None


def official_payload(value: dict[str, Any]) -> dict[str, Any]:
    # Keep the language model inside a finite typed intent boundary.
    return {
        "model": MODEL,
        "state": {"utterance": value["text"], "relationship": value["profile"], "battle": value["situation"]},
        "questions": {
            "intent": {"type": "choice", "instructions": "Choose exactly one allowed tactical intent.",
                        "criteria": {"FollowPlayer": "stay near the player", "AutonomousDefense": "leave to defend the village",
                                     "HoldPosition": "hold a safe position", "ProtectPlayer": "protect the player",
                                     "Regroup": "return to the player"}},
            "explicit_autonomy": {"type": "noul", "instructions": "Did the user explicitly permit autonomous action away from the player?"},
            "urgency": {"type": "score", "instructions": "How urgent is the tactical request?",
                        "criteria": ["routine", "normal", "urgent"]},
        },
    }


def normalize_upstream(payload: Any) -> dict[str, Any]:
    answers = payload.get("answers", {}) if isinstance(payload, dict) else {}
    intent_answer = answers.get("intent", {}) if isinstance(answers, dict) else {}
    explicit_answer = answers.get("explicit_autonomy", {}) if isinstance(answers, dict) else {}
    choice = intent_answer.get("choice") if isinstance(intent_answer, dict) else None
    if choice not in MODES:
        raise ValueError("upstream returned an unsupported intent")
    confidence = intent_answer.get("confidence", 0.0) if isinstance(intent_answer, dict) else 0.0
    try:
        confidence = max(0.0, min(1.0, float(confidence)))
    except (TypeError, ValueError):
        confidence = 0.0
    try:
        noul = float(explicit_answer.get("noul", 0.0)) if isinstance(explicit_answer, dict) else 0.0
    except (TypeError, ValueError):
        noul = 0.0
    urgency = answers.get("urgency", {}) if isinstance(answers, dict) else {}
    score = urgency.get("score", 0.0) if isinstance(urgency, dict) else 0.0
    try:
        tactical = max(0.0, min(1.0, float(score) / 2.0))
    except (TypeError, ValueError):
        tactical = 0.0
    return {"intent": choice, "confidence": confidence, "explicitAutonomy": noul >= .5,
            "needsClarification": confidence < .4, "tacticalUrgency": tactical}


def call_upstream(payload: dict[str, Any], timeout: float = 8.0) -> dict[str, Any]:
    key = os.environ.get("TYPESAFE_API_KEY", "").strip()
    if not key:
        raise RuntimeError("TYPESAFE_API_KEY is not configured")
    request = urllib.request.Request(UPSTREAM, data=json.dumps(payload).encode("utf-8"), method="POST",
                                     headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        if response.status < 200 or response.status >= 300:
            raise RuntimeError("upstream status %d" % response.status)
        return normalize_upstream(json.loads(response.read(MAX_BYTES + 1).decode("utf-8")))


class GatewayHandler(BaseHTTPRequestHandler):
    server_version = "DinoAttackJevGateway/1"

    def _write(self, status: int, value: dict[str, Any]) -> None:
        data = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self) -> None:  # noqa: N802
        if self.path == "/health":
            self._write(200, {"ok": True, "gateway": "jev", "upstreamConfigured": bool(os.environ.get("TYPESAFE_API_KEY"))})
        else:
            self._write(404, {"error": "not_found"})

    def do_POST(self) -> None:  # noqa: N802
        if self.path != "/jev":
            self._write(404, {"error": "not_found"})
            return
        try:
            length = int(self.headers.get("Content-Length", "-1"))
        except ValueError:
            length = -1
        if length < 0 or length > MAX_BYTES:
            self._write(413, {"error": "request_too_large"})
            return
        try:
            body = json.loads(self.rfile.read(length).decode("utf-8"))
            valid, error = validate_payload(body)
            if error:
                self._write(400, {"error": error})
                return
            payload = official_payload(valid)
            if os.environ.get("JEV_GATEWAY_OFFLINE", "").lower() in ("1", "true", "yes"):
                answer = local_intent(valid["text"])
                self._write(200, {**answer, "source": "local"})
                return
            try:
                answer = call_upstream(payload)
                self._write(200, {**answer, "source": "jev"})
            except Exception as exc:
                # Do not expose upstream response bodies or secrets. Local fallback is explicit.
                answer = local_intent(valid["text"])
                self._write(200, {**answer, "source": "local-fallback", "status": type(exc).__name__})
        except (UnicodeDecodeError, json.JSONDecodeError):
            self._write(400, {"error": "invalid_json"})
        except Exception:
            self._write(500, {"error": "gateway_error"})

    def log_message(self, format: str, *args: Any) -> None:
        # Never log request bodies or authorization headers.
        sys.stderr.write("jev-gateway " + (format % args) + "\n")


def main() -> None:
    server = ThreadingHTTPServer((HOST, PORT), GatewayHandler)
    print("Jev gateway listening on http://%s:%d" % (HOST, PORT), flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
