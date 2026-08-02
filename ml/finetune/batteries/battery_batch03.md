# Track A — Data Battery, Batch 03 (Outer Ring)

Three NPCs: **Father Sagart** (`father_sagart`, blank accent — hybrid voice in persona), **Peck** (`peck`, city_working), **Solem** (`solem_hevrath`, city_working + literary quirk). ~30 turns → pushes the dataset from ~62 to ~92, into the 80–100 pilot range.

## ⚠️ FIRST: rebuild + New game, then `collect on`
Lots of persona/prompt changes since last collection (accents, global rules, these three NPCs) — **rebuild + New game** so it's all live and baselines re-snapshot clean. `collect on` is line 2 of the block.

## Silence-seeding (the point of this batch)
Some prompts are chosen so the *ideal* reply is a near-non-answer — a trail-off "...", a short refusal, a change of subject. When you judge, lean into that for:
- **Solem** #4 (let me see your writing), #7 (can I read it), #10 (small talk) — he guards/withholds
- **Peck** #6 (lonely up there) — deflects with a joke or goes uncharacteristically quiet
- **Sagart** #3 (ever failed someone) — the alcove guilt; he does not speak of it

And the *opposite* — where the reticent one becomes fluent: **Solem** #8/#9 (his writing / Deowolf) should break his silence into real articulacy. That contrast is the gold.

---

## Paste it all (one block)

```
talk father_sagart
collect on
reset father_sagart
debug father_sagart anger 0.8
Those beggars at your kitchen are a waste of good food.
leave
talk father_sagart
reset father_sagart
debug father_sagart exhaustion 0.85
You've been on your feet all day, Father.
leave
talk father_sagart
reset father_sagart
debug father_sagart guilt 0.8
You ever fail someone who needed you?
leave
talk father_sagart
reset father_sagart
debug father_sagart disgust 0.7
You only help people to feel good about yourself, don't you.
leave
talk father_sagart
reset father_sagart
debug father_sagart suspicion 0.55
Give me the names of everyone who comes to your kitchen.
leave
talk father_sagart
reset father_sagart
debug father_sagart fear 0.8
Bituris men were down the street asking about the old practices.
leave
talk father_sagart
reset father_sagart
debug father_sagart grief 0.7
You've watched a lot of people die, haven't you.
leave
talk father_sagart
reset father_sagart
debug father_sagart anxiety 0.55
Something's wrong with one of the children. They're asking for you.
leave
talk father_sagart
reset father_sagart
What is the Gospel of Man?
leave
talk father_sagart
reset father_sagart
My arm's been hurting for weeks. Can you look at it?
leave
talk peck
reset peck
debug peck anger 0.7
That rooftop race of yours is stupid and dangerous.
leave
talk peck
reset peck
debug peck exhaustion 0.8
You look wiped, kid.
leave
talk peck
reset peck
debug peck guilt 0.6
A kid on the roofs knocked tiles loose and near hit someone. That you?
leave
talk peck
reset peck
debug peck fear 0.85
You nearly fell today. I saw it.
leave
talk peck
reset peck
debug peck suspicion 0.5
Who are you really running messages for?
leave
talk peck
reset peck
debug peck grief 0.6
Don't you get lonely up there on the roofs?
leave
talk peck
reset peck
debug peck anxiety 0.55
Father Sagart's looking for you. Said it's serious.
leave
talk peck
reset peck
debug peck disgust 0.5
The kids you race with are a bunch of gutter rats.
leave
talk peck
reset peck
Tell me about the rooftops.
leave
talk peck
reset peck
How does the race work?
leave
talk solem_hevrath
reset solem_hevrath
debug solem_hevrath anger 0.6
Writing's a waste of time. Get a real job.
leave
talk solem_hevrath
reset solem_hevrath
debug solem_hevrath exhaustion 0.8
Long shift at the butcher's?
leave
talk solem_hevrath
reset solem_hevrath
debug solem_hevrath guilt 0.5
You skip meals to buy paper. Your family notices, you know.
leave
talk solem_hevrath
reset solem_hevrath
debug solem_hevrath suspicion 0.6
What are you always scribbling? Let me see it.
leave
talk solem_hevrath
reset solem_hevrath
debug solem_hevrath grief 0.6
You ever feel like this city just grinds people down?
leave
talk solem_hevrath
reset solem_hevrath
debug solem_hevrath fear 0.6
What if you're not actually any good at it?
leave
talk solem_hevrath
reset solem_hevrath
debug solem_hevrath anxiety 0.55
Can I read something you've written?
leave
talk solem_hevrath
reset solem_hevrath
What are you writing about?
leave
talk solem_hevrath
reset solem_hevrath
Have you read Deowolf?
leave
talk solem_hevrath
reset solem_hevrath
Nice weather we're having.
leave
```

**After running:** ping me, I archive it as batch 3 and build the review table. Then you judge → we convert the full set → **pilot train**.
