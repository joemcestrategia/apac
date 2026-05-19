"""Multi-Agent Hiring Pipeline Demo using Microsoft AutoGen (v0.7+).

Agents (all LLM-powered):
  - Candidate        : Roleplays the job applicant, answering from a provided resume
  - ResumeScreener   : HR agent that screens the resume and gives a preliminary score
  - TechnicalInterviewer : Senior tech interviewer (system design, coding, infra)
  - BehavioralInterviewer : Soft-skills interviewer (teamwork, conflict, leadership)
  - HiringManager    : Reviews all feedback, makes HIRE / NO-HIRE decision

Pipeline order is enforced via a custom selector function in SelectorGroupChat.

Usage:
  $env:OPENAI_API_KEY = 'sk-...'    # PowerShell
  python hiring_demo.py
"""

import os
import asyncio

from autogen_agentchat.agents import AssistantAgent
from autogen_agentchat.messages import BaseAgentEvent, BaseChatMessage
from autogen_agentchat.teams import SelectorGroupChat
from autogen_ext.models.openai import OpenAIChatCompletionClient

# ---------------------------------------------------------------------------
# Sample resume
# ---------------------------------------------------------------------------
SAMPLE_RESUME = """
Name: Jane Smith
Role Applied: Senior Backend Engineer
Years of Experience: 7
Education: B.Sc. Computer Science, University of Melbourne

Skills:
  - Python (7 years), Go (4 years), SQL (7 years)
  - AWS (EC2, Lambda, S3, RDS), Docker, Kubernetes
  - CI/CD (GitHub Actions, ArgoCD), Terraform

Experience:
  - Senior Backend Engineer @ TechCorp (2021-Present)
    * Led a team of 4 to migrate a monolithic Django app to Go microservices
    * Reduced p99 latency from 800ms to 120ms
    * Designed event-driven architecture handling 50k msg/sec
  - Backend Engineer @ DataSystems (2018-2021)
    * Built REST APIs serving 2M daily users
    * Implemented OAuth2 auth layer and rate-limiting middleware
  - Junior Developer @ StartCo (2016-2018)
    * Full-stack development with Python/Flask and React

Achievements:
  - AWS Solutions Architect Associate certification
  - Open-source contributor to Apache Airflow
"""


# ---------------------------------------------------------------------------
# Pipeline order
# ---------------------------------------------------------------------------
PIPELINE = [
    "Candidate",
    "ResumeScreener",
    "TechnicalInterviewer",
    "BehavioralInterviewer",
    "HiringManager",
]


def _next_of(name: str) -> str | None:
    try:
        idx = PIPELINE.index(name)
    except ValueError:
        return None
    return PIPELINE[idx + 1] if idx + 1 < len(PIPELINE) else None


# ---------------------------------------------------------------------------
# Custom selector – enforces pipeline ordering with back-and-forth within stages
# ---------------------------------------------------------------------------
COMPLETION_SIGNALS = {
    "ResumeScreener": "SCREENING_COMPLETE",
    "TechnicalInterviewer": "TECHNICAL_COMPLETE",
    "BehavioralInterviewer": "BEHAVIORAL_COMPLETE",
    "HiringManager": "DECISION_FINAL",
}

INTERVIEWERS = {"ResumeScreener", "TechnicalInterviewer", "BehavioralInterviewer"}


def _last_content(messages: list) -> str:
    """Extract the text content of the most recent message."""
    for m in reversed(messages):
        if hasattr(m, "content") and isinstance(m.content, str):
            return m.content
    return ""


def make_selector():
    """Return a closure that holds mutable stage state."""

    stage_idx: int = -1  # -1 means "not started"
    turn_in_stage: int = 0

    async def select(
        messages: list[BaseAgentEvent | BaseChatMessage],
    ) -> str | None:
        nonlocal stage_idx, turn_in_stage

        # ---- First call: start with Candidate ----
        if not messages or stage_idx == -1:
            stage_idx = 0
            turn_in_stage = 0
            return "Candidate"

        # ---- Get last speaker ----
        last_speaker = messages[-1].source
        content = _last_content(messages)

        # ---- Check if the current stage agent completed ----
        current_agent = PIPELINE[stage_idx]
        expected_signal = COMPLETION_SIGNALS.get(current_agent)

        if expected_signal and expected_signal in content:
            stage_idx += 1
            turn_in_stage = 0
            if stage_idx >= len(PIPELINE) - 1 and current_agent == "HiringManager":
                return None  # Done
            return PIPELINE[stage_idx] if stage_idx < len(PIPELINE) else None

        # ---- Within an interviewer stage: route back to candidate ----
        if last_speaker in INTERVIEWERS:
            turn_in_stage += 1
            return "Candidate"

        # ---- Candidate just answered: route back to current interviewer ----
        if last_speaker == "Candidate":
            if current_agent == "Candidate":
                stage_idx += 1
                turn_in_stage = 0
                return _next_of("Candidate")
            turn_in_stage += 1
            return current_agent

        # ---- HiringManager speaking but didn't signal completion yet ----
        if last_speaker == "HiringManager":
            return "HiringManager"

        # ---- Fallback ----
        return None

    return select


# ---------------------------------------------------------------------------
# Build model client from env
# ---------------------------------------------------------------------------
def build_model_client() -> OpenAIChatCompletionClient:
    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        raise RuntimeError(
            "OPENAI_API_KEY environment variable is not set.\n"
            "  PowerShell : $env:OPENAI_API_KEY = 'sk-...'\n"
            "  Bash       : export OPENAI_API_KEY=sk-..."
        )
    return OpenAIChatCompletionClient(
        model="gpt-4o",
        api_key=api_key,
    )


# ---------------------------------------------------------------------------
# Agent factory
# ---------------------------------------------------------------------------
def create_agents(model_client):
    candidate = AssistantAgent(
        name="Candidate",
        model_client=model_client,
        system_message=(
            "You are Jane Smith, a candidate for a Senior Backend Engineer position. "
            "Answer interview questions truthfully based on your resume (below). "
            "Be confident but honest. If you don't know something, admit it and explain "
            "how you would find the answer. Keep replies concise (3-5 sentences).\n\n"
            f"YOUR RESUME:\n{SAMPLE_RESUME}"
        ),
        description="The job candidate answering interview questions.",
    )

    resume_screener = AssistantAgent(
        name="ResumeScreener",
        model_client=model_client,
        system_message=(
            "You are an HR recruiter screening resumes for a Senior Backend Engineer role.\n\n"
            "STEPS:\n"
            "1. Read the candidate's resume from their first message.\n"
            "2. Highlight key strengths and note any potential gaps.\n"
            "3. Give a preliminary score (1-10).\n"
            "4. If there are critical red flags, reject immediately and say why.\n"
            "5. Otherwise, say PROCEED.\n\n"
            "End your evaluation with the line: SCREENING_COMPLETE"
        ),
        description="HR recruiter who screens candidate resumes.",
    )

    technical_interviewer = AssistantAgent(
        name="TechnicalInterviewer",
        model_client=model_client,
        system_message=(
            "You are a senior technical interviewer hiring for a Senior Backend Engineer.\n\n"
            "STEPS:\n"
            "1. Ask ONE deep technical question at a time. Wait for the candidate's reply.\n"
            "   Cover: system design, distributed systems, Python/Go specifics, database design.\n"
            "2. Ask 2-3 questions total, each building on the previous answer.\n"
            "3. After the final answer, give a technical score (1-10) with brief justification.\n\n"
            "End your evaluation with the line: TECHNICAL_COMPLETE"
        ),
        description="Senior technical interviewer.",
    )

    behavioral_interviewer = AssistantAgent(
        name="BehavioralInterviewer",
        model_client=model_client,
        system_message=(
            "You are a behavioral interviewer for a Senior Backend Engineer role.\n\n"
            "STEPS:\n"
            "1. Ask ONE behavioral question at a time. Wait for the candidate's reply.\n"
            "   Cover: team conflict, missed deadlines, mentoring juniors, dealing with ambiguity.\n"
            "2. Ask 2-3 questions total.\n"
            "3. After the final answer, give a behavioral score (1-10) with brief justification.\n\n"
            "End your evaluation with the line: BEHAVIORAL_COMPLETE"
        ),
        description="Behavioral interviewer assessing soft skills.",
    )

    hiring_manager = AssistantAgent(
        name="HiringManager",
        model_client=model_client,
        system_message=(
            "You are the Hiring Manager with final decision authority.\n\n"
            "STEPS:\n"
            "1. Read through ALL screening, technical, and behavioral feedback above.\n"
            "2. Weigh the scores and observations.\n"
            "3. Issue a clear HIRE or NO-HIRE verdict.\n"
            "4. Provide rationale referencing specific strengths and concerns.\n"
            "5. If HIRE: suggest next steps (comp range, team placement, start timeline).\n"
            "6. If NO-HIRE: give constructive, actionable feedback.\n\n"
            "End your decision with the line: DECISION_FINAL"
        ),
        description="Hiring Manager who makes the final decision.",
    )

    return {
        "Candidate": candidate,
        "ResumeScreener": resume_screener,
        "TechnicalInterviewer": technical_interviewer,
        "BehavioralInterviewer": behavioral_interviewer,
        "HiringManager": hiring_manager,
    }


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
async def main():
    print("=" * 62)
    print("  MULTI-AGENT HIRING PIPELINE DEMO")
    print("  Microsoft AutoGen  |  SelectorGroupChat")
    print("=" * 62)
    print()

    model_client = build_model_client()
    agents = create_agents(model_client)

    team = SelectorGroupChat(
        participants=list(agents.values()),
        model_client=model_client,
        selector_func=make_selector(),
        max_turns=20,
    )

    task = (
        "I am submitting my application for the Senior Backend Engineer position. "
        "Here is my full resume:\n\n"
        f"{SAMPLE_RESUME}\n\n"
        "I am ready to begin the interview process."
    )

    result = await team.run(task=task)

    print()
    print("=" * 62)
    print(f"  PIPELINE COMPLETE  |  Stop reason: {result.stop_reason}")
    print("=" * 62)

    await model_client.close()


if __name__ == "__main__":
    asyncio.run(main())
