# Dino Attack Jev Gateway

```powershell
$env:TYPESAFE_API_KEY = "..." # keep outside the repository
python Tools/JevGateway/jev_gateway.py
```

The gateway binds to `127.0.0.1:8765` and accepts `POST /jev`. It translates
only the finite companion intent contract to TypeSafe's official
`POST https://api.typesafe.ai/v1/systemone` endpoint. If no key is configured,
set `JEV_GATEWAY_OFFLINE=1` (or leave Unity's gateway option disabled) and the
same conservative local parser is used. No request is sent during startup and
keys are never written to disk or returned in errors.

`GET /health` reports whether an upstream key is configured without revealing it.
