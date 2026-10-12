"""Pure contract types; this module intentionally has no I/O."""
from dataclasses import dataclass
from typing import Literal


@dataclass(frozen=True)
class AssistRequest:
    purpose: str
    permitted_context: str


@dataclass(frozen=True)
class AssistResult:
    kind: Literal["PROPOSAL"]
    text: str


def draft_placeholder(request: AssistRequest) -> AssistResult:
    if not request.purpose.strip() or not request.permitted_context.strip():
        raise ValueError("purpose and permitted_context are required")
    return AssistResult(kind="PROPOSAL", text="AI runtime is not configured")
