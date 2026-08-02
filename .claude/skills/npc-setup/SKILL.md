---
name: npc-setup
description: >-
  Wire an NPC into the Ath text-RPG world and fill its structural fields —
  schedules, locations, happenings, home_door/knock wiring, baseline
  emotional/physical/relationship states, physical_description, intros, tier and
  config flags, the inter-NPC relationships graph — plus validate that it all
  resolves. Use this whenever creating a new NPC's setup, placing an NPC in the
  world, editing schedules or home doors, setting a character's starting mood,
  writing a physical description or intro lines, wiring NPC relationships, or
  checking an NPC record is consistent. The companion to npc-writing (which owns
  the voice); this owns the wiring. Trigger even when the user just says "put
  this NPC at the market", "give them a schedule", "set their starting state",
  "wire up their home door", or "check this NPC is set up right".
---

# Wiring an NPC into Ath

This is the **structural** half of an NPC — everything that isn't voice. Voice (`persona_base`, `speech_quirk`, `world_memories`, dialogue) belongs to the **npc-writing** skill; this one handles placement, state, description, config, and validation. The overriding aim here is **consistency**: an NPC that references a location that doesn't exist, or has a schedule with holes, silently misbehaves at runtime.

**Start from a working template.** NPCs live in `Data/NpcDataConfig/npcs/*.json`. Before hand-building a record, copy the full field set from an existing well-wired NPC (e.g. any in `npcs_gate_markets.json`) so you don't miss a field. The fields below are the ones that need judgment; the rest (`orphan_memories`, `suspect_memories`, `episodic_memories`) are runtime-managed — leave them as empty arrays.

## Placing the NPC in the world

Three mechanisms, and they work together:

1. **`default_location` + `schedule`** — where the NPC is, when.
   - A schedule entry: `{ "location": <id>, "start_hour": N, "end_hour": N, "days": <...>, "farewell": "..." }`.
   - `days` is either the string `"all"` or an array of weekday integers (`0` = Sunday … `6` = Saturday), e.g. `[1,2,3,4,5,6,0]`.
   - **The `location` must be a real id from `Data/NpcDataConfig/locations.json`.** That file is the source of truth — read it for the valid ids. An unknown id silently fails placement. Regions are `carvallen`, `lathvel`, `wilderness`, `antitheis`; ids include e.g. `antitheis_outskirts`, `antitheis_outer_ring`, `mid_city_plaza`, `worn_lintel`, `bluebells_garden`, `sleeping_hound_bar`, `carvallen_market`.
   - Cover the hours the NPC should be present; gaps mean they vanish then.

2. **Happenings** (in `locations.json`) — grouped "scenes" at a location during set hours that list `npc_ids`. This is how several NPCs get presented together (e.g. the `gate_markets` happening at `antitheis_outskirts` lists `tasco, maddoc, lugor, bevan`). If a new NPC belongs to an existing scene, **add their id to that happening's `npc_ids`** as well as giving them a schedule — otherwise they're present but not part of the grouped scene. (Watch for this gap: an NPC scheduled at a location but missing from its happening.)

3. **`home_door` / `home_door_label` / `home_life`** — the knock/rouse system. When an NPC is *off-schedule*, players can knock at their `home_door` (a **location id**) to rouse them; `home_door_label` is the door shown ("a green door", "the room above the bar"); `home_life` is what they're doing when roused ("asleep", "eating supper"). Leave all three empty for NPCs with no private home to knock at (e.g. the gate hawkers, who are simply at the market or gone).

## Baseline states — make them reflect the person

These are the NPC's resting values, and they should **express the persona**, not be copy-pasted. They're what the voice sits on top of.

- **`emotional_state`** — eight axes (`fear, grief, hope, suspicion, anger, anxiety, disgust, guilt`), each 0–1. Set a resting profile that matches disposition: Bevan sits high on `suspicion`; Drust high on `hope` and `anxiety`; a content old innkeeper low on everything. Most values stay low (0–0.2); bump the one or two that define them.
- **`physical_state`** — `exhaustion, pain, intoxication, hunger, illness`, 0–1. A footsore farmer has real `exhaustion`; a hungry child real `hunger`; an old man some `pain`.
- **`player_relationship`** — `trust_player, care_player, gullibility, infatuation_player, player_erratic_behaviour`. A stranger meets most NPCs at low trust (~0.15–0.35). `gullibility` tracks how easily they're taken in (a trusting child high, a wary buyer low).

## Description and intro fields

- **`physical_description`** — **only what is perceivable on sight.** No exact ages, no names or facts a stranger couldn't know by looking, no interior life. "A sun-browned man with dirt-creased hands, standing close by a laden cart" — not "a 44-year-old farmer worried about his debts."
- **`anon_intro`** — the short label before the player knows their name ("a scrawny boy by the queue", "an old man at a food stand"). Perceivable-only, same rule.
- **`default_farewell`** and the schedule `farewell`s — the line when they end the conversation, in their voice.
- **`locational_intros` / `emotional_intros`** — optional greeting lines keyed to place or mood; leave `[]` / `{}` if not needed.

## Config flags

- **`tier`** — depth tier: `1` Principal (deep, driven), `2` Service (functional, bounded), `3` Ambient (minimal). Governs how much model budget they warrant.
- **`known_at_start`** — is the player introduced to them by name from the outset (`false` for most strangers).
- **`nonverbal`** — `true` for NPCs who don't speak.
- **`household_head`** — for the knock system, who answers the door.
- **`sleep_start_hour` / `sleep_end_hour`** — when they're asleep (affects rousing).

## The relationships graph

`relationships` is a list of `{ npc_id, trust, confide, last_contact, shared_secrets }` linking NPCs to each other. Wire in the obvious ties (Drust ↔ Maddoc, Drust ↔ Caradek). `trust` and `confide` are 0–1; `npc_id` must be a real NPC id. This feeds gossip and social consistency.

## Validation checklist (run before calling it done)

- The file **parses** as JSON (`py -c "import json; json.load(open(path, encoding='utf-8-sig'))"`).
- Every `location` in the schedule, `default_location`, and `home_door` **resolves** to an id in `locations.json`.
- Every `npc_id` in `relationships` (and any happening you edited) is a real NPC.
- The schedule **covers** the hours/days the NPC should be present, no unintended gaps.
- If the NPC belongs to a location's happening, they're **listed in it**.
- `emotional_state` / `physical_state` **express the persona**, not defaults.
