from __future__ import annotations

import pytest

from flow_rl.cli.collect_dino_ppo_demonstrations import (
    _canonical_json_sha256,
    _parser,
    _scan_unity_logs,
)


def test_parser_defaults_to_auditable_16_env_stochastic_collection() -> None:
    args = _parser().parse_args(
        [
            "--checkpoint", "teacher.pt",
            "--build", "Dino.exe",
            "--output", "dataset-run",
        ]
    )
    assert args.num_envs == 16
    assert args.rollout_size == 131_072
    assert args.target_episodes_per_map == 500
    assert args.policy_seed == 20260905


def test_parser_rejects_missing_checkpoint() -> None:
    with pytest.raises(SystemExit):
        _parser().parse_args(["--build", "Dino.exe", "--output", "dataset-run"])


def test_log_audit_separates_known_shutdown_ui_noise_from_critical_errors(
    tmp_path,
) -> None:
    log = tmp_path / "Player.log"
    log.write_text(
        "\n".join(
            (
                "NullReferenceException: Object reference not set.",
                "  at LlamAcademy.Dinos.Utility.HealthBarCanvas.Update ()",
                "InvalidOperationException: Structured observations require sources.",
                "  at LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder.ValidateSources ()",
                "  at Unity.MLAgents.Agent.OnDisable ()",
                "InvalidOperationException: rollout state corrupted",
                "  at Gameplay.Tick ()",
            )
        ),
        encoding="utf-8",
    )

    audit = _scan_unity_logs(tmp_path)

    assert audit["known_benign_match_count"] == 2
    assert audit["critical_match_count"] == 1


def test_canonical_provenance_hash_is_independent_of_mapping_order() -> None:
    assert _canonical_json_sha256({"b": 2, "a": [1, 3]}) == (
        _canonical_json_sha256({"a": [1, 3], "b": 2})
    )
