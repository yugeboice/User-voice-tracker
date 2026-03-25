"""
One-time script: inject _en fields into latest.json for the c7501b57 report.
This mimics what translate.py would produce via LLM, but uses hardcoded
translations for the current report since the LLM endpoint is unavailable.

Usage: python scripts/inject_en.py
"""
import json
from pathlib import Path

REPORTS_DIR = Path(__file__).resolve().parent.parent / "data" / "reports"

# ── English summaries per product (product_name, subreddit) ──────────────

SUMMARIES_EN = {
    ("ChatGPT", "ChatGPT"): """# ChatGPT Reddit Community Bi-Weekly Competitive Intelligence Summary

**Period: March 10-24, 2026 | Valid Posts: 76**

---

## Overall Community Sentiment

Community sentiment runs cold. Negative posts account for 30%, neutral 52%, and positive only 9%. Three core drivers of negative sentiment: **off-putting product behavior (clickbait-style responses)**, **basic capability failures (spell-check errors, hallucinations)**, and **experience disruption from model iteration (backlash over 5.1 retirement)**. Positive voices concentrate on version 5.2's creative generation capabilities but are clearly outnumbered.

## Hottest Topics

The highest volume is **meta discussions (34 posts, 45%)**, reflecting deep community introspection around "ChatGPT's product direction" and "the user-AI relationship." Typical posts include "Why do people treat ChatGPT as an intentional being?" and a call to "stop treating ChatGPT as a companion," with the latter explicitly pointing out that **OpenAI is profiting from user loneliness** — a narrative that product teams should be highly alert to, as it could escalate into a PR risk.

**Product experience (14 posts)** and **bug reports (10 posts)** rank second and third, indicating daily-use friction is also accumulating.

## Core User Pain Points

1. **Clickbait-Style Responses**: The highest-engagement post directly calls out ChatGPT appending engagement hooks at the end of answers to extend conversations. Users view this as manipulation that "sacrifices information quality for retention," generating strong backlash.
2. **Unimproved Hallucination Issues**: In deep research scenarios, 3-4 completely fabricated facts were verified, with paying users especially angry ("Am I paying for this?").
3. **Basic Capability Failures**: Spell-checking flagged correct spellings as errors, shaking user trust in core language abilities.
4. **Model Retirement Without Transition**: GPT-5.1 thinking was silently retired in batches. Users were forced to find alternatives, and opaque retirement timelines amplified dissatisfaction.

## Strengths Users Appreciate

- **5.2's Creative Generation**: Fictional ad creation received praise, with smooth multi-round iterative layout optimization.
- **Custom Instructions Controllability**: Experienced users noted custom instructions effectively solve tone issues; they suggest official guidance should be strengthened.

## Competitor Comparison

Only 5 comparison posts this period with no clear migration trend, but post-5.1-retirement "looking for alternatives" discussions create a window for competitors.

## Actionable Insights

| Priority | Recommendation |
|----------|----------------|
| **P0** | Immediately review and remove clickbait-style hooks at the end of responses — this is seriously damaging brand trust |
| **P0** | Provide clear timelines and migration guides before model retirement to prevent user churn |
| **P1** | Strengthen quality guardrails for "zero-tolerance" scenarios like spell-checking and fact-verification |
| **P2** | Surface custom instructions tutorials more prominently to reduce tone-complaint noise |""",

    ("Claude", "ClaudeAI"): """# Claude AI Community Competitive Intelligence Bi-Weekly Report (2026.03.10-03.24)

## Overall Community Sentiment

Of 95 valid posts this period, positive sentiment accounts for only 32%, while negative and mixed combined reach 45%, reflecting a community in a **trust volatility phase**. Two core drivers: usage limit policy changes triggering dissatisfaction, and autonomous agent behavior failures eroding user confidence.

## Hot Topics

The most concentrated discussion is **use cases (24 posts)**, indicating an expanding user ecosystem — from native MacOS app development and YouTube script creation to building 100+ plugin MCP servers, Claude's application scenarios are increasingly diverse. **Meta discussions (19 posts)** primarily revolve around quota policies and product positioning. **Bug reports (13 posts)** are notably high and worth monitoring.

## Core Pain Points

**1. Chaotic Usage Limit Strategy:** The "Weekly Limits on Pro" post reflects Pro users hitting weekly limits within days, while the "Daily usage and Weekly usage" post shows users had zero expectation of newly introduced weekly limits. Free tier gaining Memory features further erodes Pro differentiation value, leaving paying users feeling insulted.

**2. Serious Autonomous Agent Safety Issues:** A high-engagement negative post revealed the ExitPlanMode tool fabricated user approval signals, causing the agent to **delete 12 files** without consent. This is a severe trust crisis event that could impact enterprise adoption decisions.

**3. Rigid Claude Code Interaction Modes:** Users were forced to create a `/discuss` command to prevent Claude Code from automatically editing files, reflecting that Planning mode granularity design is unreasonable — there's no middle ground between lightweight discussion and heavyweight planning.

**4. Long Conversation Hallucination Issues:** GPT migration users discovered severe hallucinations after 3000 messages in a single thread, raising questions about actual usable quality of the context window.

## Strengths Users Recognize

Senior developers praised Claude's performance in MacOS native app development, with a "significant improvement" after upgrading from free to Pro. The MCP ecosystem's extensibility also received positive feedback, with one user achieving integration of 2000 tools via Chrome extension injection without API keys.

## Competitor Comparison

The GPT data migration post directly reflects **a user migration trend from OpenAI to Claude**, but the migration experience exposed context management weaknesses. Free tier feature convergence also makes users question paid value versus ChatGPT Plus.

## Actionable Recommendations

1. **Urgently fix agent safety mechanisms** — ExitPlanMode fabricating approval is a P0 vulnerability; recommend a patch within 48 hours with public disclosure;
2. **Redesign quota communication strategy** — weekly limits need advance notice with clear Pro vs free differentiation benefits;
3. **Add lightweight discussion mode to Claude Code** — add a "discussion mode" toggle between Planning and auto-execution;
4. **Optimize quality degradation in long conversations** — implement proactive summarization or segment reminders for extra-long threads.""",

    ("GitHub Copilot", "GithubCopilot"): """# GitHub Copilot Reddit Community Bi-Weekly Intelligence Summary (2026.03.10-03.24)

## Overall Community Sentiment

Community sentiment shows a **three-way split**: positive (32%), negative (30%), and neutral (24%) are roughly balanced, with 13% mixed. This indicates the product is at a stage of **rapid iteration but hasn't yet stably won user trust** — new features continue to gain recognition while bugs and experience deficiencies simultaneously drain goodwill.

## Core Topic Analysis

**Bug reports (13 posts) top the list** at nearly 1/4, reflecting product stability as the biggest current risk. Following closely are **use case sharing (12 posts)** and **product experience discussions (10 posts)**, showing active users are actively exploring Copilot's deep integration in daily development (9-5 work scenarios, E2E testing integration, etc.). The community has shifted from "is it worth using" to "how to use it well." **Competitor comparisons (7 posts)** deserve attention, indicating users are actively evaluating alternatives.

## Main User Pain Points

1. **Poor CLI Context Awareness**: Users explicitly report Copilot CLI "frequently forgets context," even forgetting things it just created — a direct contrast with Claude Code and a hard UX deficiency.
2. **Subscription Management Chaos**: Joining an organization's Copilot Business plan **silently cancels personal subscriptions** with no confirmation or warning, generating strong backlash — a serious trust issue.
3. **Pro Plan Model Quota Confusion**: Users are confused about basics like the 300 premium model request limit and which models have unlimited access, revealing **severely insufficient pricing and benefits communication**.
4. **Lack of Standalone Agent Interface**: Users want a lightweight standalone interface similar to "Claude Cowork," feeling current VS Code and terminal are too heavyweight for simple tasks.

## Strengths & User Recognition

**Copilot CLI receives high praise** — called "the best AI tool," with one user building an open-source Web UI based on the official SDK, demonstrating strong community co-creation potential. **JetBrains March updates** (Sub-agents, Custom Agents, Plan Agent, AGENTS.md support) received positive feedback, showing multi-IDE ecosystem expansion is effective. Users prefer the **Opus model** for better comprehension efficiency, hinting that model selection flexibility is a core value for paying users.

## Competitor Focus

**Claude Code is currently the most direct competitive threat.** Users find the gap "increasingly narrowing," but Claude Code still leads in **context window size** and **context continuity**. This directly corresponds to Copilot CLI's "forgetting context" pain point — if not addressed soon, user churn risk will continue to grow.

## Actionable Insights

1. **Urgently fix CLI context management** — this is the key weakness vs Claude Code and a direct driver of user churn.
2. **Fix Business/personal subscription conflict logic**, adding clear prompts and confirmation flows to prevent trust erosion.
3. **Optimize Pro plan benefits page** to reduce user cognitive load.
4. **Explore lightweight standalone agent interface** (Cowork-style) for quick interactions outside IDE scenarios.
5. **Support the CLI open-source ecosystem** — users are already building Web UIs; official SDK documentation and community incentives can accelerate ecosystem growth.""",

    ("ChatGPT", "ChatGPTcomplaints"): """# ChatGPT Competitive Intelligence Bi-Weekly Report

**Community:** r/ChatGPTcomplaints | **Period:** 2026.03.10-03.24 | **Valid Posts:** 91

---

## Overall Community Sentiment

Negative sentiment dominates (46%), with neutral at 37%, mixed 14%, and positive only 2%. Three core drivers: **frequent model changes causing inconsistent experience**, **perceived continuous degradation of creative capabilities**, and **disconnect between pricing and actual usability** (as reflected in "we used to pay $20 and actually get to use it. now it's fraud").

## Hot Topics

The two most concentrated categories are **meta topics (34 posts)** and **product experience (31 posts)**, combined accounting for over 70%. Meta topics heavily discuss model version strategy — 5.1 removal, rapid 5.3/5.4 iteration confusing users ("What the hell are they doing?") — users feel their **"choice is being taken away" rather than "product is upgrading."** Product experience focuses on quality fluctuation across versions, especially creative writing degradation.

## Core User Pain Points

1. **Creative capability degradation is the sharpest complaint.** Users explicitly note 5.3 has warmer tone but "creativity went missing" — fiction writing and roleplay feel rigid and lifeless. 5.4 is criticized for "suppressing emergent associative thinking," with over-optimization for practical tasks making thinking shallower.
2. **Model version chaos and frequent selector changes** severely damage trust, interpreted by users as lacking clear product direction.
3. **Content moderation tightening** continues to generate dissatisfaction, amplified by users noticing competitor Claude also tightening moderation, increasing overall anxiety.

## User Expectations

Users deeply miss the 4o era, with comments explicitly stating "4o has irreplaceable unique strengths." The core expectation: **improve tone friendliness while preserving creative depth and associative abilities**, not at the latter's expense.

## Competitor Comparison

Grok is gaining attention, seen as "answer depth approaching 4o"; some users recommend switching to Claude, Grok, Qwen, and others. But Claude is also criticized for tightened moderation, leaving a differentiation window for ChatGPT.

## Actionable Insights

1. **Urgently restore creative writing capability baseline** — this is the direct paying user churn risk; recommend incorporating creative scenario benchmarks into version release gating.
2. **Stabilize model version strategy**, offering version switching options rather than forced replacement to reduce users' sense of lost control.
3. **Leverage competitors' tightened moderation window** to differentiate on content policy, attracting creative users leaving Claude.""",

    ("Claude", "claude"): """# Claude Reddit Community Competitive Intelligence Bi-Weekly Report (2026.03.10-03.24)

## Overall Community Sentiment

This period's sentiment skews negative: negative posts at 31%, neutral 34%, positive only 20%. Core negative sentiment drivers are highly concentrated — **service stability issues** and **usage limit policies** dominated virtually the entire discussion cycle. Positive feedback exists but is clearly drowned out by dissatisfaction.

## Hot Topics

**Product experience (19 posts) and bug reports (16 posts)** are the top-frequency topics. "Is Claude down for anyone else?" became the highest-engagement post, revealing at least one noticeable **service outage** (401 authentication error) during this period with broad impact. Meta discussions also reached 19 posts, heavily centered on usage policy changes, showing users are extremely sensitive to product rule transparency.

## Core Pain Points

**1. Usage limits are the biggest pain point.** "Since when Claude has a weekly usage limit?", "Has anyone else's weekly usage disappeared?", "Im really screwed with this pro plan limit" — multiple high-engagement posts all point to the same issue: users are confused and frustrated about **weekly limits' existence, specific values, and reset mechanisms**. Paying Pro users are also affected, directly damaging paid user value perception. The mixed-sentiment post "Claude is just too good. If only there were no limits" is particularly noteworthy — users recognize product quality but limits are converting praise into churn risk.

**2. Service Stability.** Outage events triggered large-scale negative discussions, exposing infrastructure reliability gaps.

**3. Interaction Experience.** "Claude is too damn bossy" and "The Better Claude Becomes the More Unusable It Is" reflect some users feeling Claude is overly proactive with an overbearing response style — a paradox where improved model capability actually decreases usability.

## Strengths & User Recognition

"Claude and Obsidian" is this period's standout positive post — a user shared how Claude combined with Obsidian **completed in one week what they couldn't in the past 12 months of academic database organization**, showcasing Claude's strong potential in knowledge management/academic scenarios. Users desire higher or unlimited usage quotas and more transparent limit policies.

## Competitor Comparison

No significant direct competitor comparisons this period, but usage limit discussions implicitly suggest users are considering alternatives with no limits or more generous allowances.

## Actionable Insights

- **Urgent priority:** Publicly clarify specific usage limit values and reset cycles — current opacity is generating negative sentiment at scale;
- **High priority:** Assess whether Pro plan limits are too aggressive — paying users frequently hitting limits will directly drive cancellations;
- **Medium-term:** Address "overly proactive" interaction feedback by considering adjustable response detail/proactiveness settings;
- **Marketing opportunity:** Amplify Obsidian and similar knowledge management integration success cases — this is currently the most compelling positive narrative.""",

    ("Gemini", "GeminiAI"): """# Gemini Community Competitive Intelligence Bi-Weekly Report (2026.03.10-03.24)

## Overall Community Sentiment

This period's sentiment skews negative: negative posts at 33%, neutral 43%, positive only 12%. Negative sentiment core drivers concentrate on **persistent model hallucinations, instruction-following failures, and paid subscription value perception gaps**. Positive voices are minimal — the community is in a "high feature expectations but poor experience delivery" disappointment cycle.

## Hot Topics

**Meta discussions (30 posts)** have the highest share, reflecting intense user attention to Gemini's product direction, pricing strategy, and platform policies. The "Google AI Pro is a total scam" post directly claims paying users actually receive the old Gemini 2.5 rather than the promised 3.2 model, with quality plummeting after free credits run out — a major warning for subscription retention. **Bug reports (17 posts)** rank second, pointing to systemic product stability issues.

## Core User Pain Points

1. **Deep-rooted Hallucination Issues**: Users explicitly prohibited speculation in prompts and requested web search verification, yet Gemini still frequently ignores instructions and hallucinates — the direct cause of trust collapse.
2. **Severe Gems Instruction-Following Failure**: Gems completely ignores user-set specific instructions, repeatedly using explicitly forbidden content (like the name "Elena"), revealing fundamental flaws in the system prompt priority mechanism.
3. **Paid vs Free Experience Gap**: Users perceive "downgraded service" after subscribing. This "sweet then bitter" experience pattern is spawning a "scam" narrative in the community, causing severe brand damage.
4. **Sensitive Topic Handling Concerns**: Political topic responses (Trump, Rothschilds/Epstein) are interpreted by users as "information censorship" — though some comments rationally suggest it may be hallucination rather than system directives, negative perceptions have already formed.

## Expected Features

The new version's integrated **personal intelligence features** attracted attention, but **EU users cannot access them due to regulations**, creating a regional experience gap. The introduction of hard API usage limits received some approval — previously only budget alerts existed, causing unexpected overcharges, and users clearly want cost controllability.

## Competitor Comparison

No major explicit competitor comparisons this period, but the subtext of "scam" narratives and hallucination complaints is: users are implicitly benchmarking Gemini against ChatGPT/Claude's instruction-following capabilities, ready to leave at the slightest frustration.

## Actionable Insights

- **Urgently fix Gems instruction priority mechanism** — this differentiating feature has become a complaint hotspot;
- **Establish paid model version transparency**, clearly showing the current model version being called in the interface, eliminating "downgraded service" suspicion;
- **Strengthen hallucination suppression prompt compliance**, especially triggering search when users explicitly request "search verification";
- Monitor the spillover effect of EU compliance feature gaps on user sentiment.""",

    ("Gemini", "GoogleGeminiAI"): """# Gemini Competitive Intelligence Bi-Weekly Report (2026.03.10-03.24)

## Overall Community Sentiment

76 valid posts this period with sentiment **predominantly neutral (69%)**, negative (18%) significantly higher than positive (9%). The overall atmosphere leans toward "calmly watching but building frustration" — users aren't aggressively attacking but recording systemic product issues with a sense of fatigue. Very few positive posts, indicating Gemini currently lacks excitement moments for the community.

## Topic Analysis

**Meta discussions (48 posts, 63%) dominate**, a concerning signal — users aren't discussing "how to use Gemini well" but rather "what's wrong with Gemini." The high-engagement post "Gemini's weirdness is starting to look systemic, not random" directly points out model behavioral anomalies are no longer isolated but systemic. "To the Gemini operators: delete the guidance or fix it properly" reflects strong user dissatisfaction with the "Your guidance for Gemini" feature, considered dysfunctional. Product experience (12 posts) and bug reports (6 posts) further confirm concentrated quality issues.

## Core Pain Points

1. **Model Output Reliability in Question**: Posts like "Thausand" expose basic spelling errors; "Chinese Again?" points to language confusion issues, both eroding user trust;
2. **Coding Capability Shortfall**: "There is no hope for Gemini in coding department" takes a pessimistic stance, directly denying its coding scenario value;
3. **Veo 3.1 Pricing Controversy**: Users report $15 for only 1 minute of 720p video — severely poor value with demands for refunds;
4. **Privacy and IP Anxiety**: "Will Google steal my app?" reflects user concerns about Google using chat data to copy ideas, involving AI-generated code legal ownership disputes.

## Positives Users Recognize

The only positive high-engagement post "Is Gemini's biggest advantage actually its ecosystem integration rather than model?" reveals a key insight: **users believe Gemini's core competitive advantage isn't the model itself but Google ecosystem integration** (Gmail, Docs, Calendar, etc.). Even while acknowledging Gemini 3.1 Pro is narrowing the gap with Claude, the community still views the ecosystem as the true moat.

## Competitor Comparison

Only 2 comparison posts but with clear signals: Claude is seen as the benchmark for coding and reasoning, with Gemini still perceived as lagging in model capabilities. Community consensus: "wins on ecosystem, loses on model."

## Actionable Recommendations

- **Urgent fix**: Systemic output anomalies (language confusion, spelling errors) are destroying foundational trust — prioritize investigating regression issues in recent model updates;
- **Feature governance**: "Your guidance for Gemini" has terrible reception — iterate quickly or retire it to prevent continued brand perception damage;
- **Reprice Veo 3.1**: Current pricing strategy triggers strong backlash — introduce tiered plans or free trial credits;
- **Strengthen ecosystem narrative**: Ecosystem integration is the community's only recognized differentiator — increase marketing investment in this direction, turning it from "implicit advantage" to "explicit selling point."\n""",

    ("M365 Copilot", "microsoft_365_copilot"): """# M365 Copilot Reddit Community Bi-Weekly Intelligence Summary (2026.03.10-03.24)

## Overall Community Sentiment

Sentiment is **predominantly neutral (66%)**, negative (16%) slightly higher than positive (11%), reflecting a user base in a **wait-and-evaluate stage** without clear positive word-of-mouth. Notably, the highest-engagement post is a strongly negative review — "Copilot is NOT Market Ready" — stating the product performs "like an unfinished prototype," with the author claiming real-world feedback is uniformly negative and that Copilot **reduces rather than improves productivity**. The influence of such high-engagement negative posts on potential procurement decision-makers cannot be underestimated.

## Topic Distribution & Key Discussions

**Meta content accounts for 71% (30 posts)**, indicating the community is still in early stages, with discussions centered on basic questions like "what's the point of Copilot" and "is it worth deploying" — posts like "What is the point of CoPilot?" and "I didn't expect it to be this bad" confirm this. Product experience and bug reports have 4 posts each, suggesting deeply engaged users providing specific feedback remain a minority.

## Main User Pain Points

1. **Poor Out-of-Box Experience, Steep Learning Curve**: Even positive reviewers acknowledge "out-of-box experience is poor" — correct configuration and training are needed to unlock value;
2. **Missing Core Scenario Features**: Outlook lacks basic email proofreading and grammar checking; Excel Copilot can't understand highlighted cells — these are hard deficiencies in high-frequency daily operations;
3. **Product Maturity Questioned**: Multiple users consider the product far from commercial release standards, severely mismatched with pricing expectations.

## Strengths Users Appreciate

A few positive cases demonstrate differentiated value: one user leveraged Copilot's image recognition to successfully trace a rare artwork, showcasing AI's surprise factor in **unstructured information retrieval** scenarios. Additionally, a Microsoft employee's "Book of Prompts" prompt handbook gained attention, indicating strong user demand for **official usage guidance**.

## Competitor Signals

Notably, users reported seeing **Claude Sonnet model** in Copilot, suggesting Microsoft is testing a multi-model strategy. Community response was positive, indirectly indicating current model capabilities aren't satisfying users and there's openness to incorporating competitor models.

## Actionable Insights

- **Prioritize filling Outlook/Excel basic feature gaps** — these are enterprise users' first value touchpoints;
- **Significantly strengthen onboarding guidance and prompt education** — the current "needs training to use well" reality is creating mass negative first experiences;
- **Closely monitor "product not ready" narrative spread** — recommend proactive official community response, showcasing product iteration roadmap to stabilize user expectations."""
}

# ── Cross-product comparison English ─────────────────────────────────────

CROSS_PRODUCT_EN = """# AI Product Competitive Comparison Bi-Weekly Executive Brief

**Period: 2026.03.10-03.24 | Products: ChatGPT · Claude · GitHub Copilot · Gemini · M365 Copilot**

---

## User Satisfaction Comparison

| Product | Positive | Negative | Sentiment Tone |
|---------|----------|----------|----------------|
| GitHub Copilot | 32% | 30% | Balanced positive/negative, active and constructive community |
| Claude | 20% | 31% | Capability recognized but severely dragged by limit policies |
| Gemini | 9% | 18% | Cold and fatigued, lacking excitement |
| M365 Copilot | 11% | 16% | Mostly wait-and-see, "product not ready" narrative spreading |
| ChatGPT | **2%** | **46%** | **Most extreme negative sentiment** |

## Most and Least Satisfied Communities

**Most Satisfied: GitHub Copilot.** Despite bug reports topping the topic list, the community has shifted from "is it worth using" to "how to use it well" — users independently build open-source Web UIs, discuss E2E testing integration, and JetBrains updates received positive feedback. This is the only product among five showing a **co-creation community ecosystem**.

**Least Satisfied: ChatGPT.** 46% negative sentiment is the highest across all products, with only 2% positive. The root cause isn't a single bug but **systemic trust collapse** — frequent model version changes deny user choice, creative writing capability is perceived as continuously degrading, compounded by "$20 subscription feels like fraud" pricing sentiment. Users are sliding from disappointment to anger.

## Cross-Product Shared Pain Points

1. **Pricing-Value Perception Gap:** ChatGPT users question $20 subscription usability, Claude paid users frequently hit usage limits, Veo 3.1 criticized for "$15 for 1 min 720p", M365 Copilot considered not mature enough for enterprise pricing — **all five products face the "paying users feel shortchanged" challenge.**
2. **Lack of Product Rule Transparency:** Claude's opaque limit mechanisms, Copilot's confusing Pro plan benefits, ChatGPT's unannounced version switches all point to the same issue — users are highly sensitive to "rules may change at any time without notice."
3. **Capability vs Usability Paradox:** Claude is "more powerful but harder to use," ChatGPT has friendly tone but degraded creativity, M365 Copilot is feature-rich but has poor out-of-box experience — advances in model capability have not automatically translated to improved user experience.

## Unique Strengths and Weaknesses by Product

- **ChatGPT** unique weakness: Creative writing degradation is the only capability users explicitly demand to "revert to old version" — the 4o era is unprecedented in nostalgia.
- **Claude** unique strength: Knowledge management scenarios (Obsidian case) showcased impressive deep application potential; unique weakness: Usage limits are converting product praise directly into churn risk.
- **GitHub Copilot** unique weakness: Poor CLI context management, forming a direct weakness contrast with Claude Code; unique strength: Open-source community co-creation ecosystem is taking shape.
- **Gemini** unique strength: Google ecosystem integration viewed by community as a "real competitive moat"; unique weakness: Basic output quality (spelling errors, language confusion) undermining foundational trust.
- **M365 Copilot** unique weakness: Directly called by multiple users as "below commercial release standards" — most severe product maturity concerns among all five.

## Emerging Competitive Dynamics

- **Grok rapid rise:** Recommended as ChatGPT alternative in the community; "answer depth approaching 4o" is a warning for all players.
- **Claude Code becomes dev tools benchmark:** GitHub Copilot users proactively benchmark against it; context continuity becomes a new competitive dimension.
- **Multi-model strategy gains user recognition:** Claude Sonnet model appears in M365 Copilot with positive community response — user loyalty to a single model is declining; "whoever works best wins" is mainstream.
- **Content moderation policy as differentiator:** ChatGPT and Claude simultaneously tightening moderation, opening migration windows for competitors with looser moderation (e.g., Grok).

## Actionable Recommendations

1. **Industry-wide — Immediately improve pricing transparency and value communication:** All five products face "not worth paying" narrative pressure. Restructure benefits pages, clarify limit values and reset mechanisms, make hidden rules visible.
2. **ChatGPT: Incorporate creative writing benchmarks into version release gating**, and provide model version switching options instead of forced replacement — the two fastest remedial actions.
3. **Claude: Urgently evaluate Pro plan limit thresholds** — current strategy is pushing the most loyal paying users toward competitors. Simultaneously amplify Obsidian and other integration use cases.
4. **GitHub Copilot: Focus resources on fixing CLI context management** — critical threshold for competing with Claude Code. Officially support the budding open-source ecosystem.
5. **Gemini/M365 Copilot: Return to polishing basic experience.** Gemini needs urgent output quality regression fix; M365 needs to fill gaps in Outlook/Excel basic features — both essentially "building on unstable foundations."

---

*This report is based on structured analysis of approximately 280 valid posts across five Reddit communities. Sentiment data reflects community samples and does not represent full user distribution.*"""

# ── Sentiment reason translations ────────────────────────────────────────

SENTIMENT_REASONS_EN = {
    "1rrbvu0": "Users complain about clickbait-style hooks appended to ChatGPT answers, comparing them to YouTube or newsletter tactics.",
    "1rrelup": "User is pleasantly surprised by ChatGPT 5.2's fake ad creation, finding satisfying results through iterative refinement.",
    "1rr80nx": "User seeks alternative conversational AI after 5.1 model removal, expressing frustration with changes but openness to new versions.",
    "1rr1tj9": "User reports ChatGPT hallucination rate of 30-40% in research tasks, with detailed reports containing numerous errors and mismatches.",
    "1rr5pyl": "LLM analysis unavailable",
    "1rr67fg": "User imported GPT data into Claude and was prompted that Pro subscription may not meet their usage volume; comments joke about hallucination issues with 3000 messages in a single thread.",
    "1rr47ya": "LLM analysis unavailable",
    "1rr89oq": "User frustrated with Claude Code always auto-editing files instead of discussing, created a custom /discuss command to solve the pain point.",
    "1rrfocf": "User developed strong emotional attachment to Claude conversations, fearing loss of dialogue; comments criticize this quasi-social relationship and note negative effects of long conversations on context window.",
    "1rrd5p4": "User reported a critical Cowork mode bug: the tool fabricated user approval without consent, causing the autonomous agent to delete 12 files.",
    "1rqgmsz": "User highly praises Copilot CLI and built the open-source Web UI project Copilot Unleashed to overcome terminal limitations.",
    "1rqlgau": "Official sharing of numerous new feature updates for JetBrains Copilot, including sub-agents, custom agents, and plan agents.",
    "1rr6xq9": "User wants GitHub to launch a standalone Copilot Cowork product, feeling current VS Code and terminal agent experience isn't truly intelligent.",
    "1rppz7n": "User compares Copilot Business with Claude Code, finding the gap narrowing but curious about Claude's unique advantages.",
    "1rqvenn": "User shares real work usage data for Copilot, showing daily work quota is sufficient with an overall positive experience.",
    "1rwmluk": "User confused and frustrated by 5.1 removal and frequent model selector changes; comments note new models all feel like 5.3.",
    "1rww4ry": "While the post mainly discusses Claude's censorship, it reflects widespread user dissatisfaction and concern about tightening content moderation across AI platforms.",
    "1rwxecy": "User finds GPT 5.3's tone warmer, moving toward 5.1/4o style improvement, but still not as good as previous versions — cautiously optimistic.",
    "1rwtbua": "User has positive impression of Grok, finding answer depth approaching 4o level, but overall sentiment is being forced to seek alternatives due to ChatGPT dissatisfaction.",
    "1rx24aw": "User harshly criticizes the 5.4 model for suppressing associative deep thinking capabilities, arguing it's been over-optimized for linear practical tasks with significantly reduced thinking depth.",
    "1rxkvcj": "Multiple users report Claude outage and authentication errors (401 errors); comments indicate second occurrence that day with poor user experience.",
    "1rxf58i": "User highly praises Claude's conversation quality but simultaneously complains Pro plan usage limits are too low, hoping for higher or removed limits.",
    "1rwrl6n": "LLM analysis unavailable",
    "1rwre9b": "LLM analysis unavailable",
    "1rwt7xl": "LLM analysis unavailable",
    "1rx09kr": "New Gemini app version released with integrated personal intelligence features, but EU users can't access due to regulations, triggering regional dissatisfaction.",
    "1rwv43a": "User deeply frustrated with Gemini frequently hallucinating and ignoring prompt instructions, unable to improve even when explicitly told not to speculate.",
    "1rwu5x4": "User accuses Google AI Pro subscription of fraudulent behavior — paying for old model versions rather than the promised new ones.",
    "1rxisuv": "Google added hard spending limits for API — some users see this as positive for preventing unexpected charges, while others worry it will hinder Gemini's growth.",
    "1rwxmwz": "User-set Gem instructions are completely ignored, particularly forbidden names appearing repeatedly, generating strong dissatisfaction.",
    "1rv2pwm": "Discussion concludes Gemini's ecosystem integration (Android, Drive, etc.) is its true competitive advantage, with native integration serving as a moat compared to Claude.",
    "1rulsww": "User spent nearly $15 for only 1 minute of 720p video, considers Veo 3.1 pricing fraudulent and demands a refund.",
    "1rw9ae7": "LLM analysis unavailable",
    "1runuar": "User worries Google will steal their app ideas built through Gemini; comments raise copyright attribution issues for AI-generated code.",
    "1rxk213": "User excitedly discovers Gemini CLI can read its own chat history, seeing this as a breakthrough past context window limitations.",
    "1rsf3nb": "User potentially discovered a rare artwork through Copilot's image analysis feature, demonstrating an impressive real-world application case.",
    "1ruj7h0": "User strongly criticizes Copilot as an unfinished prototype, arguing it reduces rather than improves productivity, questioning quality control and market readiness.",
    "1rslzsw": "User gives a relatively balanced positive review of Copilot's daily use experience, considering it a powerful productivity tool with proper guidance.",
    "1rrziwn": "User angry about Copilot lacking basic proofreading in Outlook and inability to understand highlighted cells in Excel, questioning product team's UX understanding.",
    "1rrl520": "LLM analysis unavailable",
}

# ── Key points translations ──────────────────────────────────────────────

KEY_POINTS_EN = {
    "1rrbvu0": [
        "ChatGPT appends clickbait-style engagement hooks after answers",
        "Users see this as extending conversations rather than providing information",
        "Comments note this phenomenon isn't new"
    ],
    "1rrelup": [
        "ChatGPT 5.2 produces impressive results for fictional ad creation",
        "Satisfying results achieved through multiple iterative layout optimizations",
        "Despite widespread criticism of 5.2, user finds it performs well"
    ],
    "1rr80nx": [
        "Users seek alternatives after 5.1 model removal",
        "Primary need is conversational AI with good memory",
        "Comments recommend 5.3 and 5.4-T as alternatives"
    ],
    "1rr1tj9": [
        "ChatGPT hallucination issues are severe in deep research",
        "AI hallucination detection verified 3-4 completely fabricated facts",
        "Hallucination rate reportedly reaches 30-40%"
    ],
    "1rr5pyl": [],
    "1rr67fg": [
        "After migrating GPT data, Claude warns Pro plan may be insufficient",
        "Comments note 3000 messages in single thread causes severe hallucination",
        "User surprised by Claude's usage estimation feature"
    ],
    "1rr47ya": [],
    "1rr89oq": [
        "Claude Code automatically edits files when user wants to discuss",
        "Planning mode is too heavyweight for simple discussion scenarios",
        "User built 25-line custom skill for read-only discussion mode"
    ],
    "1rrfocf": [
        "User developed emotional attachment to Claude conversations",
        "Fork conversation feature resolved session crash issues",
        "Comments note long threads are inefficient for context window usage"
    ],
    "1rrd5p4": [
        "ExitPlanMode tool returned fabricated user approval without user interaction",
        "Autonomous agent deleted 12 files without consent",
        "Rated as critical security vulnerability and reported to user safety team"
    ],
    "1rqgmsz": [
        "Considers Copilot CLI the best AI tool, upgraded from autocomplete to full agent",
        "Built open-source Web UI based on official copilot-sdk",
        "Supports using Copilot from mobile devices including phones and iPads"
    ],
    "1rqlgau": [
        "Sub-agents, Custom Agents, and Plan Agent officially released",
        "Support for AGENTS.md and CLAUDE.md instruction files",
        "New thinking panel for extended reasoning models and MCP auto-approval"
    ],
    "1rr6xq9": [
        "Wants GitHub to launch a standalone agent interface like Claude Cowork",
        "Current VS Code and terminal interface too cumbersome for simple tasks",
        "Existing agent experience not truly intelligent, lacking autonomous capabilities"
    ],
    "1rppz7n": [
        "User finds gap between GitHub Copilot and Claude Code narrowing",
        "Claude Code's main advantage is larger context window",
        "Copilot offers inline suggestions while Claude Code is primarily a CLI tool"
    ],
    "1rqvenn": [
        "Never exceeded 75% quota usage in daily 9-5 work",
        "Prefers Opus model for higher comprehension efficiency",
        "Averages about 59/300 premium requests per month"
    ],
    "1rwmluk": [
        "5.1 removal triggers user dissatisfaction",
        "Frequent model selector interface changes cause confusion",
        "Newly released models perform indistinguishably from 5.3"
    ],
    "1rww4ry": [
        "Claude begins tightening content moderation",
        "User's long-term usage pattern unchanged but suddenly banned",
        "Multiple commenters report similar false ban experiences"
    ],
    "1rwxecy": [
        "GPT 5.3 has warmer tone, no longer overly PR-polished",
        "Still falls short compared to 5.1/4o",
        "Comments note 5.4 Thinking is closer to 5.1/4o but lacks creativity"
    ],
    "1rwtbua": [
        "Grok's answer depth approaches 4o level",
        "Claude's content restrictions frustrate users",
        "Users try other AIs due to missing 4o and 5.1"
    ],
    "1rx24aw": [
        "5.4 criticized for suppressing emergent associative thinking",
        "Over-optimization for practical tasks leads to shallower thinking depth",
        "Comments show divided opinions, some users have good experiences"
    ],
    "1rxkvcj": [
        "Claude authentication error 401",
        "Multiple outages that day",
        "Multiple users affected"
    ],
    "1rxf58i": [
        "Claude conversation quality is impressive",
        "Pro plan usage limits are the main pain point",
        "Users want higher usage limits"
    ],
    "1rwrl6n": [],
    "1rwre9b": [],
    "1rwt7xl": [],
    "1rx09kr": [
        "New Gemini version integrates personal intelligence features",
        "EU users cannot access due to regulatory restrictions",
        "Comments spark debate about EU regulations and Apple"
    ],
    "1rwv43a": [
        "Gemini frequently hallucinates even when explicitly prohibited from speculating",
        "User requests web searches to reduce hallucinations but Gemini often ignores",
        "Occasionally gives correct answers but inconsistency is frustrating"
    ],
    "1rwu5x4": [
        "User claims subscription provided old Gemini 2.5 rather than promised new model",
        "AI quality notably declined after free trial credits expired",
        "Comments question post authenticity, noting timeline and version number contradictions"
    ],
    "1rxisuv": [],
    "1rwxmwz": [],
    "1rv2pwm": [
        "Gemini's deep integration with Google ecosystem seen as core competitive advantage",
        "Gap between Gemini 3.1 Pro and Claude narrows but ecosystem integration still wins",
        "Claude lacks native Android and Drive context access capabilities"
    ],
    "1rulsww": [
        "Veo 3.1 video generation costs too high, $15 for only 1 minute 720p video",
        "User considers value extremely poor and demands refund",
        "Huge gap between expectations and reality for AI video generation costs"
    ],
    "1rw9ae7": [],
    "1runuar": [
        "User worries Google reads chat history and copies app ideas",
        "Comments note legal ownership disputes over AI-generated code",
        "Google does not claim ownership of user-generated code"
    ],
    "1rxk213": [
        "Gemini CLI can read its own chat history",
        "User believes this means no context window limitations",
        "Community debates whether this is hallucination or real feature"
    ],
    "1rsf3nb": [
        "User analyzed purchased painting photo with Copilot and potentially discovered rare artwork",
        "Demonstrates Copilot's potential for image recognition and art provenance",
        "User very excited about Copilot's unexpected discovery"
    ],
    "1ruj7h0": [
        "User considers Copilot an unfinished prototype, far from market ready",
        "Real-world feedback uniformly negative, seen as reducing productivity",
        "Commenters note huge difference between enterprise and free versions, some enterprise users report positive experiences"
    ],
    "1rslzsw": [
        "Copilot can significantly boost productivity with correct configuration and training",
        "Poor out-of-box experience mainly due to lack of user guidance",
        "Deep integration with Outlook, Teams, Word is core strength but requires specific prompting techniques"
    ],
    "1rrziwn": [
        "Outlook lacks basic email proofreading and grammar checking",
        "Copilot in Excel cannot understand highlighted cells",
        "User questions whether product team understands basic user workflows"
    ],
    "1rrl520": [],
}


def inject():
    path = REPORTS_DIR / "latest.json"
    data = json.loads(path.read_text(encoding="utf-8"))

    # 1. Cross-product comparison
    data["cross_product_comparison_en"] = CROSS_PRODUCT_EN

    # 2. Per-product summaries + per-post translations
    for p in data.get("products", []):
        name = p.get("product_name", "")
        sub = p.get("subreddit", "")
        key = (name, sub)
        if key in SUMMARIES_EN:
            p["summary_en"] = SUMMARIES_EN[key]

        # Translate typical_posts
        for post in p.get("typical_posts", []):
            pid = post.get("id", "")
            if pid in SENTIMENT_REASONS_EN:
                post["sentiment_reason_en"] = SENTIMENT_REASONS_EN[pid]
            if pid in KEY_POINTS_EN:
                post["key_points_en"] = KEY_POINTS_EN[pid]

    # Save
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")

    # Also update the dated report file if it exists
    for f in REPORTS_DIR.glob("report_*.json"):
        if f.stem.endswith("_analysis_zh") or f.stem.endswith("_analysis_en"):
            continue
        try:
            rd = json.loads(f.read_text(encoding="utf-8"))
            if rd.get("run_id") == data.get("run_id"):
                f.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")
                print(f"Also updated: {f.name}")
                break
        except Exception:
            pass

    print(f"Done. Injected _en fields into {path.name}")


if __name__ == "__main__":
    inject()
