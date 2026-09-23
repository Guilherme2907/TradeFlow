---
name: checkpoint
description: >-
  Persists study session outcomes, architectural decisions, testing breakthroughs,
  injected failures, and learning progress into the repository's permanent tracking
  documents. Use when the user asks to save a session, create a checkpoint, wrap up
  today's study, or record progress.
---

# Checkpoint Skill — Session State Persistence

This skill handles session wrap-up and persistence. It ensures all insights, decisions, test choices, struggles, and next steps from the active session are preserved in the repository files so future sessions can resume seamlessly.

---

## Operating Protocol

> [!IMPORTANT]
> **No Feature Implementation**: Do **not** implement new application features or write new code during checkpointing. This skill is strictly for recording state, updating tracking registries, and creating session checkpoints.

### Synchronization Steps

When this skill is activated:

1. **Review Session Transcript & Trajectory**:
   - Synthesize all implementation, testing discussions, failure drills, architectural choices, and conceptual quiz results from the current conversation.

2. **Generate Session Checkpoint File**:
   - Create a new log file in [`docs/sessions/`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/sessions/) named:
     `YYYY-MM-DD-session-<topic-name>.md`
   - Adhere strictly to the standard template defined in [`docs/sessions/README.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/sessions/README.md):
     - **Implementation Progress**
     - **Architectural Decisions Made** (linking ADRs if created)
     - **Testing Decisions Made** (levels chosen, trade-offs, what was NOT tested)
     - **Important Tests Added & Protected Behavior**
     - **What Was Understood Well**
     - **What Was Struggled With / Conceptual Gaps**
     - **Important Technical Concepts Explored**
     - **Injected Failures & Investigations**
     - **Unanswered & Open Questions**
     - **Professional English & Interview Practice Notes**
     - **Next Learning Step**

3. **Update Persistent Tracking Files**:
   - **[`docs/PROJECT-CONTEXT.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/PROJECT-CONTEXT.md)**: Update current stage, active components, approved ADRs, and known technical debt.
   - **[`docs/LEARNING-STATE.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/LEARNING-STATE.md)**: Update ratings for concepts studied, update the Testing Mastery Matrix, log diagnostic question results, record anti-patterns, and note English communication practice.
   - **[`docs/OPEN-QUESTIONS.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/OPEN-QUESTIONS.md)**: Update question statuses (`New` → `Needs Review` → `Good` → `Strong`) and add any new unresolved dilemmas surfaced during the session.
   - **[`docs/adr/`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/)**: If any ADR was formulated or modified, update its status in the ADR registry.

4. **Present Checkpoint Summary**:
   - Present a concise closing summary to the developer highlighting:
     - Checkpoint file created
     - Updated files
     - Mastered vs. struggling concepts
     - The recommended starting point for the next session
