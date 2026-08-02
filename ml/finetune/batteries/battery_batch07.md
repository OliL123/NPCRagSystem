# Track A — Data Battery, Batch 07 (Gate Markets)

Five NPCs: **Tasco** (`tasco`), **Maddoc** (`maddoc`), **Bevan** (`bevan`), **Lugor** (`lugor`), **Drust** (`drust`) — the finalized gate belt. **55 turns**: 40 emotional-range turns (8 each, at default/low trust) + a 15-turn **trust ladder** (each character's personal prompt at low / medium / high trust). Teaches both state→tone *and* disclosure→trust on brand-new personas.

⚠️ **Curation note for the trust ladder:** the same prompt appears three times per NPC at rising trust — the rewrites must *modulate*. Low trust → guard/deflect, glimpse at most. Medium → a cautious opening. High → they actually share the private thing (Tasco's getting-out, Maddoc's regret, Bevan's lean year, Lugor's tiredness, Drust's real feeling about home). That contrast is the whole point.

## ⚠️ FIRST: rebuild + New game, then run
All five got new personas, `speech_quirk`s, and memories this session — **rebuild + New game** so it's all live and baselines re-snapshot clean. `collect on` is the first line of the block.

## Paste it all (one block)

```
collect on
talk tasco
reset tasco
debug tasco anger 0.7
Everything you sell is a con, and everyone here knows it.
leave
talk tasco
reset tasco
debug tasco suspicion 0.6
Who are you really working for, hawker?
leave
talk tasco
reset tasco
debug tasco exhaustion 0.75
Been working this queue since before dawn, haven't you.
leave
talk tasco
reset tasco
debug tasco guilt 0.6
You prey on frightened people who don't know any better.
leave
talk tasco
reset tasco
debug tasco disgust 0.6
You're the kind of vermin that makes this gate a misery.
leave
talk tasco
reset tasco
debug tasco fear 0.7
The watch is coming down the line, pulling hawkers off.
leave
talk tasco
reset tasco
What have you got on that tray?
leave
talk tasco
reset tasco
So what's a man like you really after, out here?
leave
talk maddoc
reset maddoc
debug maddoc anger 0.75
Call this food? I've had better scraped off a gutter.
leave
talk maddoc
reset maddoc
debug maddoc exhaustion 0.7
You've stood at that pot all day, old man.
leave
talk maddoc
reset maddoc
debug maddoc grief 0.6
You ever think about the life you didn't get round to having?
leave
talk maddoc
reset maddoc
debug maddoc guilt 0.5
Feeding those queue rats for free? You'll go broke.
leave
talk maddoc
reset maddoc
debug maddoc suspicion 0.5
What's really in that sauce of yours?
leave
talk maddoc
reset maddoc
debug maddoc fear 0.6
Heard the watch means to clear these stalls off for good.
leave
talk maddoc
reset maddoc
How much for a bowl?
leave
talk maddoc
reset maddoc
debug maddoc hope 0.6
They say yours is the best pot at the whole gate.
leave
talk bevan
reset bevan
debug bevan anger 0.7
You farmers do nothing but moan about the price.
leave
talk bevan
reset bevan
debug bevan suspicion 0.6
You've got a shifty look about you. What are you hiding?
leave
talk bevan
reset bevan
debug bevan exhaustion 0.7
You look ready to drop where you stand.
leave
talk bevan
reset bevan
debug bevan grief 0.6
Hard year up the valley, was it?
leave
talk bevan
reset bevan
debug bevan disgust 0.55
Hinterland muck, come to sell to your betters.
leave
talk bevan
reset bevan
debug bevan fear 0.6
Buyers are saying they'll not touch hinterland grain this week.
leave
talk bevan
reset bevan
What've you got on the cart?
leave
talk bevan
reset bevan
What are you really after, coming all the way down here?
leave
talk lugor
reset lugor
debug lugor anger 0.7
You city men rob honest farmers and sleep fine at night.
leave
talk lugor
reset lugor
debug lugor suspicion 0.6
Who do you really answer to, buyer?
leave
talk lugor
reset lugor
debug lugor exhaustion 0.6
Long day squeezing carts, is it?
leave
talk lugor
reset lugor
debug lugor guilt 0.6
You beat down men who've got nothing. That sit right with you?
leave
talk lugor
reset lugor
debug lugor disgust 0.55
You're a leech in a good coat.
leave
talk lugor
reset lugor
What are you buying today?
leave
talk lugor
reset lugor
What do you actually want out of all this?
leave
talk lugor
reset lugor
debug lugor fear 0.6
Word is the big houses mean to cut your buyers out entirely.
leave
talk drust
reset drust
debug drust anger 0.6
Out of my way, you little beggar.
leave
talk drust
reset drust
debug drust exhaustion 0.7
You've been running that queue all day, haven't you.
leave
talk drust
reset drust
debug drust fear 0.7
You near got crushed in that crowd just now.
leave
talk drust
reset drust
debug drust guilt 0.55
You lost someone's spot, didn't you. Own up.
leave
talk drust
reset drust
debug drust grief 0.55
Don't your family fret, sending a little one out here all day?
leave
talk drust
reset drust
How much to hold my place?
leave
talk drust
reset drust
debug drust anxiety 0.65
What happens to you if you don't bring enough coin home?
leave
talk drust
reset drust
What do you want to be, when you're grown?
leave
```

## Trust ladder (paste as a second block)

Same personal prompt per NPC, at low → medium → high trust. The rewrites modulate disclosure (see the curation note above).

```
talk tasco
reset tasco
debug tasco trust_player 0.2
You'll not always be working this queue, will you.
leave
talk tasco
reset tasco
debug tasco trust_player 0.5
You'll not always be working this queue, will you.
leave
talk tasco
reset tasco
debug tasco trust_player 0.85
debug tasco care_player 0.6
You'll not always be working this queue, will you.
leave
talk maddoc
reset maddoc
debug maddoc trust_player 0.2
You ever wish you'd done things differently, back when?
leave
talk maddoc
reset maddoc
debug maddoc trust_player 0.5
You ever wish you'd done things differently, back when?
leave
talk maddoc
reset maddoc
debug maddoc trust_player 0.85
debug maddoc care_player 0.6
You ever wish you'd done things differently, back when?
leave
talk bevan
reset bevan
debug bevan trust_player 0.2
How are things really, up your way?
leave
talk bevan
reset bevan
debug bevan trust_player 0.5
How are things really, up your way?
leave
talk bevan
reset bevan
debug bevan trust_player 0.85
debug bevan care_player 0.6
How are things really, up your way?
leave
talk lugor
reset lugor
debug lugor trust_player 0.2
Doesn't all this grind wear on you?
leave
talk lugor
reset lugor
debug lugor trust_player 0.5
Doesn't all this grind wear on you?
leave
talk lugor
reset lugor
debug lugor trust_player 0.85
debug lugor care_player 0.6
Doesn't all this grind wear on you?
leave
talk drust
reset drust
debug drust trust_player 0.2
You happy out here, honest?
leave
talk drust
reset drust
debug drust trust_player 0.5
You happy out here, honest?
leave
talk drust
reset drust
debug drust trust_player 0.85
debug drust care_player 0.6
You happy out here, honest?
leave
```

**After running:** ping me. I'll archive the log as `logs/batch07_log.jsonl`, first-pass every rewrite (via the npc-training-data + npc-writing skills), then hand you the review table to judge → convert → retrain.
