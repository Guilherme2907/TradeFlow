---
name: study-session
description: >-
  Initializes or resumes a TradeFlow study session. Reconstructs full project context
  from persistent repository documents and performs a structured Context Check before
  starting work. Use whenever the user asks to start today's session, continue from where
  they stopped, or begin a study block.
---

# Study Session Skill

This skill governs how to kick off or resume a study session in the TradeFlow repository. It ensures the AI agent independently reconstructs context without requiring the developer to repeat background information.

---

## Autonomous Context Reconstruction

When this skill is activated, execute the following file inspection sequence before responding:

1. **Read Project Rules**:
   - [`AGENTS.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/AGENTS.md)
   - [`.agents/rules/study-harness.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/.agents/rules/study-harness.md)
2. **Read Charter & System Vision**:
   - [`docs/PROJECT-CHARTER.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/PROJECT-CHARTER.md)
3. **Read Current System State**:
   - [`docs/PROJECT-CONTEXT.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/PROJECT-CONTEXT.md)
4. **Read Developer Mastery & Diagnostic State**:
   - [`docs/LEARNING-STATE.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/LEARNING-STATE.md)
5. **Read Roadmap & Current Milestone**:
   - [`docs/ROADMAP.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/ROADMAP.md)
6. **Read Active Architectural Dilemmas**:
   - [`docs/OPEN-QUESTIONS.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/OPEN-QUESTIONS.md)
7. **Inspect Targeted ADRs & Code**:
   - Inspect only ADRs in [`docs/adr/`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/) and source files directly relevant to the current stage.

---

## Session Kickoff Procedure

### 1. Perform and Display the Context Check
Output a crisp summary of current project status:

```markdown
### 🧭 Session Context Check
- **Current Project Stage**: [e.g., Phase 0: Bootstrap | Phase 1: Core Domain]
- **Current Architecture**: [e.g., Bootstrap | Monolith Baseline | Modular Monolith]
- **Technologies Currently Implemented**: [Active tools/frameworks]
- **Important Previous Decisions**: [Key approved ADRs]
- **Current Learning Objectives**: [Target concepts for this session]
- **Known Technical Debt**: [Logged compromises from PROJECT-CONTEXT.md]
- **Current Task**: [Immediate problem to tackle]
- **Unresolved Questions**: [Relevant open questions from OPEN-QUESTIONS.md]
- **Recommended Next Step**: [Mentor's proposed direction for today]
```

### 2. Establish the Engineering Problem
- **Do not write application code immediately.**
- Clearly formulate the business problem and the engineering dilemma to be solved today.
- Frame the opening architectural or testing question to prompt the developer's reasoning before introducing solutions.
