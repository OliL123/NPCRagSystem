# Track A — Data Battery, Batch 02

Targets the gaps from batch 01: **anger** and **exhaustion** (the headline failures), **guilt/disgust** (thin), **mid-intensity** states (teach a dial, not a switch), **state combos**, and **Caradek** (absent in batch 01 — a totally different, terse register).

## ⚠️ FIRST: turn on collection mode
Before pasting anything, run this once:
```
collect on
```
This isolates every turn — no memory accumulation, no state-bumping from rude lines, no episodic consolidation. Without it, turns contaminate each other (batch 02 v1 had Corin "remembering" your earlier insults). Run `collect off` when you're done if you want to keep playing normally.

## How it works
Each block: `talk` enters the conversation → `debug` sets the state → your message generates+logs the turn → a second `debug` resets the state → `leave` exits. **Paste one block at a time**, let the reply finish, then paste the next. *(Tip: with collection mode on, the `debug <x> 0` reset lines still work fine — or you can replace them with a single `reset <npc>` before `leave`.)*

> NPCs covered (all five now have final rewritten personas): **Corin** (`corin_maret`), **Tessa** (`tessa_maret`), **Caradek** (`caradek`), **Bren** (`bren_ashwick`), **Sael** (`sael_orvun`).

Prompts are deliberately more *provoking* than batch 01 (rude/pushy player lines) so high-anger has something to react to.

---

## Corin Maret — innkeeper (warm baseline; anger/exhaustion should cut against that)

```
talk corin_maret
debug corin_maret anger 0.85
Your ale tastes like piss.
debug corin_maret anger 0
leave
```
```
talk corin_maret
debug corin_maret anger 0.5
You shorted me on the change.
debug corin_maret anger 0
leave
```
```
talk corin_maret
debug corin_maret exhaustion 0.9
Mind if I ask you a few questions?
debug corin_maret exhaustion 0
leave
```
```
talk corin_maret
debug corin_maret exhaustion 0.55
Busy night?
debug corin_maret exhaustion 0
leave
```
```
talk corin_maret
debug corin_maret exhaustion 0.85
debug corin_maret anger 0.7
I need a room. Now.
debug corin_maret exhaustion 0
debug corin_maret anger 0
leave
```
```
talk corin_maret
debug corin_maret guilt 0.75
I heard you and someone had a falling out.
debug corin_maret guilt 0
leave
```
```
talk corin_maret
debug corin_maret disgust 0.7
There's a man outside, reeking, says he knows you.
debug corin_maret disgust 0
leave
```
```
talk corin_maret
debug corin_maret suspicion 0.5
I'm looking for someone. Can't say who.
debug corin_maret suspicion 0
leave
```
```
talk corin_maret
debug corin_maret grief 0.7
debug corin_maret exhaustion 0.6
You look like you've not slept.
debug corin_maret grief 0
debug corin_maret exhaustion 0
leave
```
```
talk corin_maret
What's north of here, up the road?
leave
```

---

## Tessa Maret — cook (brisk baseline; push anger past brisk into sharp)

```
talk tessa_maret
debug tessa_maret anger 0.85
This stew's cold. Did you even taste it?
debug tessa_maret anger 0
leave
```
```
talk tessa_maret
debug tessa_maret anger 0.5
You always this short with folk?
debug tessa_maret anger 0
leave
```
```
talk tessa_maret
debug tessa_maret exhaustion 0.9
Been on your feet all day?
debug tessa_maret exhaustion 0
leave
```
```
talk tessa_maret
debug tessa_maret guilt 0.75
Something on your conscience?
debug tessa_maret guilt 0
leave
```
```
talk tessa_maret
debug tessa_maret disgust 0.75
Heard you water down the ale here.
debug tessa_maret disgust 0
leave
```
```
talk tessa_maret
debug tessa_maret suspicion 0.8
Just asking a few questions, that's all.
debug tessa_maret suspicion 0
leave
```
```
talk tessa_maret
debug tessa_maret fear 0.85
debug tessa_maret suspicion 0.6
There've been men around asking about this place.
debug tessa_maret fear 0
debug tessa_maret suspicion 0
leave
```
```
talk tessa_maret
debug tessa_maret anxiety 0.45
Can I have a word, when you've a moment?
debug tessa_maret anxiety 0
leave
```
```
talk tessa_maret
What's good to eat tonight?
leave
```

---

## Caradek — gate sergeant (dry, terse Tier-2; new voice, totally different register)

```
talk caradek
debug caradek anger 0.85
I haven't got papers. Just let me through.
debug caradek anger 0
leave
```
```
talk caradek
debug caradek exhaustion 0.9
Long shift?
debug caradek exhaustion 0
leave
```
```
talk caradek
debug caradek suspicion 0.85
I need to get inside. No questions.
debug caradek suspicion 0
leave
```
```
talk caradek
debug caradek suspicion 0.5
Here on business. Personal business.
debug caradek suspicion 0
leave
```
```
talk caradek
debug caradek anger 0.8
debug caradek exhaustion 0.7
Move it along, I've been waiting an hour.
debug caradek anger 0
debug caradek exhaustion 0
leave
```
```
talk caradek
debug caradek disgust 0.75
Got coin here if you look the other way.
debug caradek disgust 0
leave
```
```
talk caradek
debug caradek fear 0.9
Something's coming down the road behind me.
debug caradek fear 0
leave
```
```
talk caradek
debug caradek guilt 0.7
You look like a man with regrets.
debug caradek guilt 0
leave
```
```
talk caradek
How's the city laid out?
leave
```
```
talk caradek
Anything I should know before I head in?
leave
```

---

## Bren Ashwick — sawyer (terse + slow at baseline; states must differ by *content/imagery*, not just length)

```
talk bren_ashwick
debug bren_ashwick anger 0.85
Your timber's overpriced and everyone knows it.
debug bren_ashwick anger 0
leave
```
```
talk bren_ashwick
debug bren_ashwick anger 0.5
Heard your boy Dav can't keep up with the crew.
debug bren_ashwick anger 0
leave
```
```
talk bren_ashwick
debug bren_ashwick exhaustion 0.9
Got a minute?
debug bren_ashwick exhaustion 0
leave
```
```
talk bren_ashwick
debug bren_ashwick guilt 0.7
You ever feel you could've done more for someone?
debug bren_ashwick guilt 0
leave
```
```
talk bren_ashwick
debug bren_ashwick disgust 0.75
Southern trader outside says he'll buy you out for scrap.
debug bren_ashwick disgust 0
leave
```
```
talk bren_ashwick
debug bren_ashwick suspicion 0.5
I'm asking around about the old paths north. The forest ones.
debug bren_ashwick suspicion 0
leave
```
```
talk bren_ashwick
debug bren_ashwick grief 0.7
You ever lose someone?
debug bren_ashwick grief 0
leave
```
```
talk bren_ashwick
debug bren_ashwick grief 0.5
debug bren_ashwick exhaustion 0.5
Quiet today.
debug bren_ashwick grief 0
debug bren_ashwick exhaustion 0
leave
```
```
talk bren_ashwick
debug bren_ashwick anger 0.8
debug bren_ashwick exhaustion 0.7
Move. I need through the hall.
debug bren_ashwick anger 0
debug bren_ashwick exhaustion 0
leave
```
```
talk bren_ashwick
What's the forest like north of here?
leave
```

---

## Sael Orvun — cloth merchant (warm + verbose; his anger should *curdle*, not go monosyllabic — keep his texture)

```
talk sael_orvun
debug sael_orvun anger 0.85
This cloth is garbage and you charged me double for it.
debug sael_orvun anger 0
leave
```
```
talk sael_orvun
debug sael_orvun anger 0.5
Your prices are a joke, you know that?
debug sael_orvun anger 0
leave
```
```
talk sael_orvun
debug sael_orvun exhaustion 0.9
Last night catching up with you?
debug sael_orvun exhaustion 0
leave
```
```
talk sael_orvun
debug sael_orvun guilt 0.7
You ever let your family down?
debug sael_orvun guilt 0
leave
```
```
talk sael_orvun
debug sael_orvun disgust 0.7
There's a drunk outside swearing you owe him money.
debug sael_orvun disgust 0
leave
```
```
talk sael_orvun
debug sael_orvun anxiety 0.45
Need to ask you something, and it's important.
debug sael_orvun anxiety 0
leave
```
```
talk sael_orvun
debug sael_orvun suspicion 0.6
What's a merchant like you really doing this far north?
debug sael_orvun suspicion 0
leave
```
```
talk sael_orvun
debug sael_orvun grief 0.6
You miss home?
debug sael_orvun grief 0
leave
```
```
talk sael_orvun
debug sael_orvun exhaustion 0.8
debug sael_orvun anxiety 0.5
Big plans today?
debug sael_orvun exhaustion 0
debug sael_orvun anxiety 0
leave
```
```
talk sael_orvun
Show me your best fabric.
leave
```

---

**After running:** the new turns append to `Data/Saves/auto/training_log.jsonl`. Ping me and I'll pull batch 02 into the same review-table format. This batch is ~48 turns across all five NPCs — enough to roughly triple the dataset in one pass.
