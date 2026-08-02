---
name: npc-writing
description: >-
  Write and refine NPCs for the Ath text-RPG — personas (persona_base +
  speech_quirk), world_memories, and in-character dialogue — in the project's
  grounded, people-first house voice. Use this whenever creating a new NPC,
  fleshing out a stub, refining or reviewing an existing persona, rating a
  character on the register dials, or writing/reviewing NPC dialogue and Track-A
  training lines. Trigger even when the user just says "write a persona for X",
  "flesh out this hawker", "give this NPC a voice", "rate this character", or
  pastes a character to go over — without naming the method.
---

# Writing NPCs for Ath

The goal of this project is **realistic interaction** — believable people, not literary set-pieces. Everything below serves that. If a choice makes a character read more like a real person of their station and less like good writing, take it. The craft should not show.

## The one governing rule: write people, not grandiosity

My default instinct pulls toward *impressive* prose — balanced antithesis, clever images, aphoristic mottos, tidy character arcs. Every one of those trades a real person for a polished sentence, and it flattens a whole cast into one essayist's voice. Resist it. Ask, always: **would a real person of this station actually say it this way?**

Grandiosity is *reserved*: the deities, the cosmology, the belief-to-reality metaphysics — and, sparingly, the genuinely important individuals — may carry an elevated register. A farmer selling hides does not get the prose a dreaming god gets. For everyone else, plainer and truer beats cleverer.

## Fields you're writing

An NPC lives in `Data/NpcDataConfig/npcs/*.json`. The writing-relevant fields:

- **`persona_base`** — second person ("You are X, …"), several short paragraphs. The character and their world. **No** "Speak in the first person / Never break character" boilerplate — the system adds that.
- **`speech_quirk`** — a *separate* field the engine injects on its own line. This is where the *how they talk* instruction lives (register + sample verbal tells), **not** baked into persona_base. Older Carvallen NPCs put speech in persona_base — that's legacy; use the field.
- **`world_memories[].content`** — first person, what the NPC knows/remembers. Must not contradict the persona. (A per-memory `privacy` score to gate disclosure by trust is a Phase-4 idea, not built yet.)

## Persona structure

Write `persona_base` as short flowing paragraphs, one facet each — never a wall of text. The loose shape:

1. **Introduce** — who they are, in a line.
2. **What they do** — their work/role, concretely.
3. **How they interact with the world** — manner, how they treat people, behavioral tells.
4. **Their engine** — the thing that animates them (see below).

Then the register goes in `speech_quirk`, often with sample words ("'Aye' rather than 'yes'", "calls himself Ol'Maddoc").

Depth scales with tier as a *rule of thumb, not a cap*: service/ambient NPCs often run ~1,000–1,600 chars and principals with real backstory ~2,200–3,400 — but write what the character needs. A Tier-2 with genuine substance (a real want, a live friction) earns a fuller persona; never truncate a good character just to hit a tier's word-count.

## Every character needs an engine

A persona that only *describes* a job is inert. Each character needs something that animates them beyond their function — usually a **live friction** (a grievance, a resentment, a gap between what their labour is worth and what it earns), but it can equally be an interest, a hobby, a goal, a curiosity.

- **Survival counts as a goal, but give it texture.** Not "he wants to eat" — Bevan resents merchants profiteering while farm-gate prices stay flat; Tasco is clawing his way *out* of the Ring; Lugor is tired of thin margins next to the big players.
- **Keep frictions unresolved.** Don't tie the ache off into contentment. "Some days I even believe it" beats "and he's made his peace with it."
- **Modest ≠ inert, and modest ≠ a secret.** Real people carry grievances and pragmatic concerns more often than a poignant screenwriter's wound. Don't force a hidden tragedy onto everyone.
- A genuinely flat, settled character is a *rare, deliberate* exception (some real people are dull), not the default.

## The five register dials

Rate each character 1–5 and let the numbers drive the prose. This is how you match register to the *person* instead of writing everyone the same. Record them in `Data/Lore/Character_Reference.md`.

- **Open ↔ Closed** — how much of their genuine self they show a stranger.
- **Wily ↔ Plain** — their *speech*: do they angle and calculate, or say it straight? (Wily is *strategic angling*, not fancy words — Lugor speaks bluntly but is very wily.)
- **Kind ↔ Gruff** — default warmth toward people.
- **Talkative ↔ Terse** — how *much* they say. This is the lever on length; it's what separates Tasco (floods you) from Bevan (bare words).
- **Sincere ↔ Performative** — is what they show genuine, or an act for effect? This disambiguates "open": Tasco is open *and* performative (seems forthcoming, it's a pitch); Maddoc is open *and* sincere; Bevan is closed *and* sincere (shares little, means all of it).

The dials also govern disclosure: a stranger gets little from a closed/guarded character; nobody confesses a real vulnerability to someone they just met — glimpse it at most.

## Craft techniques (what the house voice actually does)

Studied from the project's best personas. Do these:

1. **Hyper-specific concrete details carry characterization.** Not "she cooks old recipes" but *"the salted pork brine your grandmother used for three days before the roast."* The specificity *is* the voice. Generic is the enemy.
2. **Behavioral tells — do-X-when-Y.** *"When something interests you, you lean forward slightly. When something bores you, you wipe the bar."* Actionable for the model, not adjectives.
3. **Backstory as a causal chain, not a list.** Each fact should *cause* the next (wanted cloth → father refused → left home → misses the brother he never watched grow up).
4. **Understate the heavy stuff.** The most devastating material lands flattest — state it plainly, let it sit, don't editorialize the emotion.
5. **Small human contradictions.** A man who mentions his dog more than his grandchildren; a girl whose father is her "personal villain" on bad days *and* whom she knows loves her.
6. **Put the character's own attitude in the prose**, don't narrate them from an authorial distance ("any fancy whatever calculus", "hah!", "boy").
7. **Use the world's own register, not borrowed real-world slang.** Working-class voice comes from dropped-g's, a rustic burr, and plain words — the way the existing cast does it — not imported dialect terms a reader has to decode ("toff", "graft the doubles"). If a word would send the reader to a glossary, it's the wrong register. Agents drift toward a fancier, slangier voice than the house one; hold them to plain. **The bar is *decodability*, not period-purity:** plain modern-lexicon words that read naturally ("planet", "mental health") are fine per the user, even when faintly anachronistic — the Horologist-touched setting has room for it. The enemy is obscure regional *slang* a reader must stop to decode, not standard vocabulary.
8. **Match sentence rhythm to the feeling — length is an emotional tool.** Long, run-on, comma-spilling sentences for anxiety, dread, grief, being mired in a feeling (Isaura's spiralling "or if... or if... or if... there's always too many ifs"). Short, clipped sentences for anger and tension. Don't default everything to one medium length; let the rhythm *carry* the emotion. (This is also the real fix for the triad tic — the problem was never lists, it was an unvarying cadence.)
9. **Plain beats clever — the hardest one to hold.** The strongest pull to resist is toward the *good line*: the shaped, aphoristic, quotable phrase. Cut it. Quotable lines are *reserved* — for the genuinely wise, the pretentious, a professional of language (a scholar, a diplomat), a speech. Common folk, kids, and plain working people don't produce epigrams in conversation; if a line belongs on a poster, rough it down until it's just a person talking. And prefer a genuine *question* over a knowing *conclusion about the other person* ("Did you just leave?" over "You just... left."). The plain, uncertain version usually carries *more* — it confesses where the clever line performs.
10. **Physical over emotional tells.** In an action beat, describe what the body visibly *does* ("sets down the ladle", "the cloth stops"), not what the character *feels* ("the word costs him"). A few emotional tells are fine; leaning on them narrates the subtext the words should be carrying.

## Tics to avoid (and why)

These are the failure modes of writing-that-impresses. They flatten voice, and in Track-A training data a repeated pattern gets over-learned by the fine-tune.

- **The "A but B" / "not X, Y" antithesis** — balanced-inversion sentences ("None of it is temper. It is arithmetic."). The single most pervasive crutch; it makes every NPC sound like the same person doing a rhetorical seesaw. Use it *only* when the contrast is a real character point (it's fine sparingly), never as a default rhythm.
- **Overreaching flourishes.** A flourish is *fine* when it's earned and stays within the character — a plain man is allowed one good image ("the ones the whole avenue smells before it sees"). The tic is a flourish that shows off or reaches for grandeur it hasn't earned ("it doesn't get lonely", "arithmetic that wears a coat"). Cut those; keep the earned ones.
- **The echo-opener** — starting a line by throwing the prompt's key word back as a question ("Shame?", "Papers?"). Barrel into the line instead.
- **The mid-line action sandwich** — `[text] *action* [text]` wedged into a sentence. At most one action beat per line, at the start or end. A well-placed boundary beat doing real work (an eyebrow waggle, a deep sigh) is *good*; the mid-sentence wedge is the tic.
- **Action-beat overfrequency** — the deeper version of the above: defaulting to `*beat* speech *beat*` on *most* lines. Per line it's fine; as a house rhythm it imposes one stage-managed cadence on the whole cast and the beats turn to filler. Vary DENSITY and PLACEMENT — many lines should carry no beat at all (pure speech is often stronger and faster), some turns are action-*only* (a shrug, a look, a door shut, no words), and dialogue should lead as often as a beat does. If most of your drafted lines open with `*...*`, that's the tell.
- **Setting-bound action beats** — a beat that assumes a prop or place (`*takes a sip*`, `*wipes the bar*`, `*sets the ladle down*`) is safe *only* for a character permanently at that post (an innkeeper at the bar, a cook at the pot). On a mobile or talked-anywhere NPC it conjures props that aren't there, and as an SFT target it teaches that hallucination. Prefer location-neutral beats (laugh, shrug, glance); reserve prop beats for those who never leave the spot. Grounding actions in the setting is right — but do it through *curation*, not by injecting a per-job action menu into the persona: a prompt list of example actions primes overuse across the cast, the same failure that made the killed `*wipes the bar*` example bleed onto everyone.
- **Em-dash overuse** — prefer commas and full stops.
- **The rule-of-three triad** — naming things in threes as a default rhythm ("a burned store, a broken door, fields you couldn't work"). Fine *once* when it earns its place; as a reflex it flattens every description into the same cadence. Vary the count (two, four), or restructure.
- **The "[noun] who / kind of / sort that" construction** — "the kind who go where the law runs thin," "the sort that," "the ones who." Swapping *kind* for *ones/sort/those* does **not** fix it; the *structure* is the tic. Use "those who" sparingly, or drop the framing and just describe the thing directly.
- **"you know it"** — tacking "and you know it" onto a stated trait ("an easy man to be around and you know it"). A reflexive intensifier that adds nothing; cut it, or show the self-awareness through behaviour.
- **Definition-by-negation** — "you/he/they are not...", "not much rattles you," "has not got a great deal to be sour about." Defining a person by what they *aren't* as a default rhythm; say what they ARE instead. (Deliberate negation for a punchy line — "a young man you aren't" — is fine; the *reflex* is the tic.)
- **Reflexive bounce-back questions** ("But who are you?", "You come far?") — a character-*motivated* redirect (a hawker closing a sale) is fine; the reflexive tic is not.
- **Signposting the secret** — don't have a character announce they're withholding something ("there's a thing I'll not say"). Let it stay unsaid; glimpse it obliquely.
- **The clever constructed line** — reaching for the shaped, witty, quotable phrase instead of what the person would plainly say ("curiosity is the prettier word for running"). The single most persistent tic. If it reads like a writer wrote it, cut it. (See craft #9.)
- **Narrating your own interiority** — a character announcing their reflection ("I've thought on that more than I'd tell most", "I don't usually say this"). Say the bare thing or leave it silent; the weight is in the delivery, not the self-report.
- **Remarking on being affected, or grading the exchange** — "you ask like it matters", "that's more honest than I get", "you pushed back, most don't". The character stepping outside the conversation to appraise it or admit it got to them. Observing the *player* is fine only when grounded and doing character work (a flex, a threat, a read) — never a grade of the talk or a leak that they were moved.
- **Defaulting to suspicion / aloofness** — meeting a stranger with reflexive wariness, and worse, making *every* character do it, so the whole cast collapses into one guarded person. Security reads as *ease and openness*. Vary the disposition; if two NPCs would answer a probe the same way, one is written wrong.
- **The walking information bank** — an NPC that only *answers*, dispensing facts about itself or the world. A real person also asks, offers things unprompted, matches your story with their own, and can be changed by the talk. (See "the living exchange" below.)

## Writing dialogue / training lines

When writing NPC replies (in-game or Track-A data), the same rules apply. To draw out a character's *emotional range* rather than one register, vary the prompt across these directions:

hostile challenge · a transaction or ask · probe the private · a kindness unearned · danger nearby (fear) · asked about someone lost (grief) · something stirs a memory (nostalgia) · tempted to bend/cheat (guilt) · flattered (pride/suspicion) · wrongly accused · a stranger begs help · interrupted mid-task

Pick the ones that reveal *this* character. For training data, also vary the injected emotional state and trust level so the model learns state→tone and disclosure→trust, not just one mood.

### The living exchange (dialogue that breathes)

Distilled from close-reading film dialogue — Before Sunrise's mutual meander, Superbad's kid-cadence, Better Call Saul's controlled subtext, Gran Torino's gruff warming. All of it **dosed to the character**:

- **The NPC is a partner, not an answer-machine.** It offers things unprompted, asks its own questions out of real curiosity, matches your input with its own experience, debates, and can shift. Depth lives in the *exchange*, not in ornate prose. **Dose it to disposition and power:** two equals (strangers) trade openly and mutually; a character in *command* of the room gives less and makes you work; an eager one over-shares; a guarded one offers only once earned.
- **Start oblique when you can** — react to a shared third thing (the world, an event) rather than head-on to the player; let a beat *misfire* and go nowhere; don't rush the point, or even names.
- **Interest and wordiness rise in levels, earned** — start brief and barely-invested, warm by degrees only as the talk gives a reason. Never front-load the fully-engaged NPC.
- **Naturalistic cadence** — a run-on *tendency* with real stops, "..." for the beat of thought or self-correction, filler and hedges ("y'know", "or somethin'"), doubling. Not one clean witty unit per turn; never modern slang ("like", "dude"). Vary the tempo — short volleys and grunts between longer beats, so it reads as talk, not Q&A.
- **Inarticulacy under real emotion** — false starts, groping, trailing off — *but character-gated*: a plain stoic gropes; a diplomat whose composure *is* the skill deflects seamlessly without a stumble. Never a breakdown unless earned.
- **Ground everything** — react only to what's actually conveyed (words, in-fiction actions, injected appearance, tracked state); never invent player behaviour, and never contradict what the character demonstrably is.

The per-input-type response policies live in **npc-reply-craft**; multi-turn arcs (cold-open friction, guard-down-by-degrees, disclosure paced to a secret's danger) live in **npc-conversation-flow**.

## Gold anchors — read before you write

The truest guide is the project's own hand-written personas. **Read a few of these first** to soak the voice, matched to the register you're about to write:

- `Data/Lore/Character_Reference.md` — the whole cast at a glance, with dials.
- Plain/warm working folk: `npcs_carvallen.json` (Corin, Tessa), `npcs_gate_markets.json` (Maddoc, Bevan, Drust).
- Wily/transactional: `npcs_gate_markets.json` (Lugor, Tasco).
- Deep principals with causal backstory: `npcs_antitheis_mid_city.json` (Dagovir, Aria), `npcs_antitheis_outer_ring.json` (Solem, Father Sagart).

## Workflow

- **Seed first.** A one-line seed — name · district/tier · role · the one specific want under the surface · a line they'd never say to a stranger · how they talk — turns a generic draft into a sharp one. Ask for it, or infer it from an existing stub.
- **Draft → the user reviews → propagate.** The user is the voice authority. When they correct one thing, sweep the rest for the same issue and fix it everywhere.
- **For volume (many Tier-2s):** parallel subagents can draft, each given this skill + the Character Reference + 2–3 of the user's own personas as anchors. Then consolidate (run a collision check, sweep the tics) *before* the user reviews — agents drift, so that pass is load-bearing, not optional.
